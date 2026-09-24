using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Constants;

namespace Cafe.Launcher.Avalonia.Services.GameRuntime;

/// <summary>
/// 把一次运行器启动的 stdout/stderr 捕获到数据根下一个有大小上限的文件里（每次启动覆盖），
/// 让诊断导出能解释失败发生在哪一层（运行器不存在 / 版本探测失败 / Prefix 初始化失败 /
/// 反作弊拦截 / 游戏崩溃）。这是纯增量：运行器输出在此之前完全没有落点。
/// </summary>
/// <remarks>
/// <para>捕获是尽力而为的后台工作，绝不能影响启动：任何读取或写入失败都吞掉。达到上限后仍继续
/// 把管道读干（不读会让子进程写满管道而卡住），只是不再写文件。</para>
/// <para>文件路径由 <see cref="LauncherDataRoot"/> 拥有；本类不解析进程级静态。</para>
/// </remarks>
public sealed class RunnerOutputCapture
{
    /// <summary>写入文件的上限；超过即停止记录并在末尾标注截断。</summary>
    internal const int MaxCharacters = 256 * 1024;

    private readonly object gate = new();
    private readonly LauncherDataRoot dataRoot;
    private CancellationTokenSource? current;

    public RunnerOutputCapture(LauncherDataRoot dataRoot)
    {
        ArgumentNullException.ThrowIfNull(dataRoot);
        this.dataRoot = dataRoot;
    }

    /// <summary>捕获文件的绝对路径。</summary>
    public string FilePath => dataRoot.RunnerOutputPath;

    /// <summary>
    /// 开始把 <paramref name="standardOutput"/> 与 <paramref name="standardError"/> 抽干进
    /// <see cref="FilePath"/>，返回两个流结束（进程退出）时完成的 <see cref="Task"/>。上一次捕获会被取消。
    /// </summary>
    public Task Begin(TextReader standardOutput, TextReader standardError)
    {
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);

        CancellationTokenSource cancellation;
        lock (gate)
        {
            current?.Cancel();
            current?.Dispose();
            current = cancellation = new CancellationTokenSource();
        }

        return CaptureAsync(standardOutput, standardError, cancellation.Token);
    }

    private async Task CaptureAsync(TextReader standardOutput, TextReader standardError, CancellationToken cancellationToken)
    {
        try
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var stream = new FileStream(
                FilePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.Read | FileShare.Delete);
            await using var writer = new StreamWriter(stream, Utf8NoBom);
            var state = new BoundedWriter(writer);

            await Task.WhenAll(
                    PumpAsync(standardOutput, "[out] ", state, cancellationToken),
                    PumpAsync(standardError, "[err] ", state, cancellationToken))
                .ConfigureAwait(false);

            if (state.Truncated)
            {
                await writer.WriteLineAsync("... output truncated ...").ConfigureAwait(false);
            }

            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Best effort: a diagnostics capture must never take down the launch.
        }
    }

    private static async Task PumpAsync(
        TextReader reader,
        string prefix,
        BoundedWriter state,
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

            state.WriteLine(prefix + line);
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
