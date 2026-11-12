using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.UI.Services.GameRuntime;
using Cafe.Launcher.Testing;
using Cafe.Launcher.Core.Services.GameRuntime;

namespace Cafe.Launcher.Tests;

public sealed class RunnerOutputCaptureTests : IDisposable
{
    private readonly TestDirectory tempDir = TestDirectory.Create();

    [Fact]
    public async Task BeginAsync_WritesBothStreamsToTheCaptureFile()
    {
        var capture = new RunnerOutputCapture(tempDir.DataRoot);

        await capture.BeginAsync(new StringReader("ready\nlaunching"), new StringReader("prefix warning"));

        var text = await File.ReadAllTextAsync(capture.FilePath);
        Assert.Contains("[out] ready", text, StringComparison.Ordinal);
        Assert.Contains("[out] launching", text, StringComparison.Ordinal);
        Assert.Contains("[err] prefix warning", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BeginAsync_WhenOutputExceedsTheLimit_MarksTheCaptureTruncated()
    {
        var capture = new RunnerOutputCapture(tempDir.DataRoot);

        await capture.BeginAsync(
            new StringReader(new string('x', RunnerOutputCapture.MaxCharacters + 100)),
            new StringReader(""));

        var text = await File.ReadAllTextAsync(capture.FilePath);
        Assert.Contains("... output truncated ...", text, StringComparison.Ordinal);
        Assert.True(text.Length < RunnerOutputCapture.MaxCharacters + 256);
    }

    [Fact]
    public async Task BeginAsync_WhenCalledAgain_OverwritesThePreviousCapture()
    {
        var capture = new RunnerOutputCapture(tempDir.DataRoot);

        await capture.BeginAsync(new StringReader("first launch"), new StringReader(""));
        await capture.BeginAsync(new StringReader("second launch"), new StringReader(""));

        var text = await File.ReadAllTextAsync(capture.FilePath);
        Assert.Contains("[out] second launch", text, StringComparison.Ordinal);
        Assert.DoesNotContain("first launch", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BeginAsync_WhenPreviousCaptureStillBlockedOnItsPipe_ReplacesItWithoutSharingTheFile()
    {
        var diagnostics = new RecordingDiagnostics();
        var capture = new RunnerOutputCapture(tempDir.DataRoot, diagnostics);
        var blocked = new BlockingReader();

        var first = capture.BeginAsync(blocked, new StringReader(""));
        var second = capture.BeginAsync(new StringReader("second launch"), new StringReader(""));

        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10));

        var text = await File.ReadAllTextAsync(capture.FilePath);
        Assert.Contains("[out] second launch", text, StringComparison.Ordinal);
        Assert.DoesNotContain("first launch output", text, StringComparison.Ordinal);
        // 正常替换不是录制失败：只有旧捕获被取消，没有 Warn。
        Assert.Empty(diagnostics.Messages);
    }

    [Fact]
    public async Task BeginAsync_WhenRecordingPathUnavailable_StillDrainsBothPipesAndWarns()
    {
        var diagnostics = new RecordingDiagnostics();
        var capture = new RunnerOutputCapture(tempDir.DataRoot, diagnostics);
        // 用目录占住录制路径：打开文件必然失败（Windows 上是拒绝访问，其它平台是 I/O 错误）。
        Directory.CreateDirectory(capture.FilePath);
        try
        {
            var output = new TrackingReader("out line");
            var error = new TrackingReader("err line");

            await capture.BeginAsync(output, error).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.True(output.IsDrained);
            Assert.True(error.IsDrained);
            Assert.Contains(
                diagnostics.Messages,
                message => message.Contains(RunnerOutputCapture.RecordingUnavailableTitle, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(capture.FilePath);
        }
    }

    public void Dispose()
    {
        tempDir.Dispose();
    }

    /// <summary>第一次读取一直挂起，直到被取消；用来模拟仍在录制的旧捕获。</summary>
    private sealed class BlockingReader : TextReader
    {
        private readonly TaskCompletionSource<string?> firstLine =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int reads;

        public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref reads) > 1)
            {
                return null;
            }

            using var registration = cancellationToken.Register(
                static state => ((TaskCompletionSource<string?>)state!).TrySetCanceled(),
                firstLine);
            return await firstLine.Task.ConfigureAwait(false);
        }
    }

    /// <summary>读完即标记，证明录制失败时管道仍被排空。</summary>
    private sealed class TrackingReader(string content) : TextReader
    {
        private readonly StringReader inner = new(content);

        public bool IsDrained { get; private set; }

        public override string? ReadLine()
        {
            var line = inner.ReadLine();
            if (line is null)
            {
                IsDrained = true;
            }

            return line;
        }

        public override ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken) => new(ReadLine());
    }
}
