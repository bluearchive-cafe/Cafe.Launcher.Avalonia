using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Core.Constants;
using Cafe.Launcher.Core.Services.Diagnostics;

namespace Cafe.Launcher.Core.Services.GameRuntime;

/// <summary>
/// 把一次运行器启动的 stdout/stderr 捕获到数据根下一个有大小上限的文件里（每次启动覆盖），
/// 让诊断导出能解释失败发生在哪一层（运行器不存在 / 版本探测失败 / Prefix 初始化失败 /
/// 反作弊拦截 / 游戏崩溃）。这是纯增量：运行器输出在此之前完全没有落点。
/// </summary>
/// <remarks>
/// <para>捕获是尽力而为的后台工作，绝不能影响启动：任何读取或写入失败都吞掉。达到上限后仍继续
/// 把管道读干（不读会让子进程写满管道而卡住），只是不再写文件。录制文件打不开时同样继续排空
/// 管道，并以 Warn 记一条诊断——静默丢管道会让兼容层在缓冲写满后卡住游戏且无人知晓
/// （AUD-GAME-001）。</para>
/// <para>新一次 <see cref="BeginAsync"/> 会先取消上一次捕获并等待它释放录制文件（有上限）再覆盖
/// 写入；旧捕获若在该预算内没有结束，新捕获退化为只排空不录制，而不是因共享冲突静默失败。</para>
/// <para>文件路径由 <see cref="LauncherDataRoot"/> 拥有；本类不解析进程级静态。</para>
/// </remarks>
internal sealed class RunnerOutputCapture
{
    /// <summary>写入文件的上限；超过即停止记录并在末尾标注截断。</summary>
    internal const int MaxCharacters = 256 * 1024;

    /// <summary>等待上一次捕获释放录制文件的预算。</summary>
    private static readonly TimeSpan ReplacementWaitTimeout = TimeSpan.FromSeconds(5);

    /// <summary>录制文件不可用时的诊断标题。</summary>
    internal const string RecordingUnavailableTitle = "RunnerOutputCapture";

    private readonly object gate = new();
    private readonly LauncherDataRoot dataRoot;
    private readonly ILauncherDiagnostics? diagnostics;
    private CancellationTokenSource? current;
    private Task currentCapture = Task.CompletedTask;

    public RunnerOutputCapture(LauncherDataRoot dataRoot, ILauncherDiagnostics? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(dataRoot);
        this.dataRoot = dataRoot;
        this.diagnostics = diagnostics;
    }

    /// <summary>捕获文件的绝对路径。</summary>
    public string FilePath => dataRoot.RunnerOutputPath;

    /// <summary>
    /// 开始把 <paramref name="standardOutput"/> 与 <paramref name="standardError"/> 抽干进
    /// <see cref="FilePath"/>，返回两个流结束（进程退出）时完成的 <see cref="Task"/>。上一次捕获会被取消，
    /// 新捕获在它释放录制文件后才覆盖写入。
    /// </summary>
    public Task BeginAsync(TextReader standardOutput, TextReader standardError)
    {
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);

        CancellationTokenSource cancellation;
        Task capture;
        lock (gate)
        {
            var previous = currentCapture;
            current?.Cancel();
            current = cancellation = new CancellationTokenSource();
            capture = CaptureAsync(standardOutput, standardError, cancellation, previous);
            currentCapture = capture;
        }

        return capture;
    }

    private async Task CaptureAsync(
        TextReader standardOutput,
        TextReader standardError,
        CancellationTokenSource cancellation,
        Task previous)
    {
        var cancellationToken = cancellation.Token;
        StreamWriter? writer = null;
        try
        {
            await WaitForReplacementAsync(previous, cancellationToken).ConfigureAwait(false);
            // 被更新的一次 BeginAsync 取代时不再碰录制文件，交给新的捕获。
            cancellationToken.ThrowIfCancellationRequested();

            writer = TryOpenWriter();
            var state = writer is null ? null : new BoundedWriter(writer);

            await Task.WhenAll(
                    PumpAsync(standardOutput, "[out] ", state, cancellationToken),
                    PumpAsync(standardError, "[err] ", state, cancellationToken))
                .ConfigureAwait(false);

            if (state is not null && writer is not null)
            {
                if (state.Truncated)
                {
                    await writer.WriteLineAsync("... output truncated ...").ConfigureAwait(false);
                }

                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            // Best effort: a diagnostics capture must never take down the launch.
        }
        finally
        {
            if (writer is not null)
            {
                try
                {
                    await writer.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Best effort.
                }
            }

            lock (gate)
            {
                if (ReferenceEquals(current, cancellation))
                {
                    current = null;
                }
            }

            cancellation.Dispose();
        }
    }

    /// <summary>等上一次捕获释放录制文件；超时就放弃覆盖，退化为只排空。</summary>
    private static async Task WaitForReplacementAsync(Task previous, CancellationToken cancellationToken)
    {
        if (previous.IsCompleted)
        {
            return;
        }

        try
        {
            await previous.WaitAsync(ReplacementWaitTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // 旧捕获不会抛出（内部已吞掉）；超时或取消都只意味着不能安全覆盖。
        }
    }

    /// <summary>打开录制文件；失败时记一条 Warn 并返回 null，调用方改为只排空管道。</summary>
    private StreamWriter? TryOpenWriter()
    {
        try
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var stream = new FileStream(
                FilePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.Read | FileShare.Delete);
            return new StreamWriter(stream, Utf8NoBom);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            diagnostics?.LogMessage(
                LogEntrySeverity.Warn,
                RecordingUnavailableTitle,
                $"{FilePath}: {exception.Message}");
            return null;
        }
    }

    private static async Task PumpAsync(
        TextReader reader,
        string prefix,
        BoundedWriter? state,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                return;
            }

            if (line is null)
            {
                return;
            }

            state?.WriteLine(prefix + line);
        }
    }

    /// <summary>两个管道共用一个写入器，写入串行化并按字符数封顶。</summary>
    private sealed class BoundedWriter(StreamWriter writer)
    {
        private readonly object writeGate = new();
        private int characters;

        public bool Truncated { get; private set; }

        public void WriteLine(string line)
        {
            lock (writeGate)
            {
                if (Truncated)
                {
                    return;
                }

                if (characters + line.Length + 1 > MaxCharacters)
                {
                    Truncated = true;
                    return;
                }

                writer.WriteLine(line);
                characters += line.Length + 1;
            }
        }
    }

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
}
