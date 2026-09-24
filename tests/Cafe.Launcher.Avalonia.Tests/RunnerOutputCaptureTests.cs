using System;
using System.IO;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Services.GameRuntime;
using Cafe.Launcher.Avalonia.Testing;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class RunnerOutputCaptureTests : IDisposable
{
    private readonly TestDirectory tempDir = TestDirectory.Create();

    [Fact]
    public async Task Begin_WritesBothStreamsToTheCaptureFile()
    {
        var capture = new RunnerOutputCapture(tempDir.DataRoot);

        await capture.Begin(new StringReader("ready\nlaunching"), new StringReader("prefix warning"));

        var text = await File.ReadAllTextAsync(capture.FilePath);
        Assert.Contains("[out] ready", text, StringComparison.Ordinal);
        Assert.Contains("[out] launching", text, StringComparison.Ordinal);
        Assert.Contains("[err] prefix warning", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Begin_WhenOutputExceedsTheLimit_MarksTheCaptureTruncated()
    {
        var capture = new RunnerOutputCapture(tempDir.DataRoot);

        await capture.Begin(
            new StringReader(new string('x', RunnerOutputCapture.MaxCharacters + 100)),
            new StringReader(""));

        var text = await File.ReadAllTextAsync(capture.FilePath);
        Assert.Contains("... output truncated ...", text, StringComparison.Ordinal);
        Assert.True(text.Length < RunnerOutputCapture.MaxCharacters + 256);
    }

    [Fact]
    public async Task Begin_WhenCalledAgain_OverwritesThePreviousCapture()
    {
        var capture = new RunnerOutputCapture(tempDir.DataRoot);

        await capture.Begin(new StringReader("first launch"), new StringReader(""));
        await capture.Begin(new StringReader("second launch"), new StringReader(""));

        var text = await File.ReadAllTextAsync(capture.FilePath);
        Assert.Contains("[out] second launch", text, StringComparison.Ordinal);
        Assert.DoesNotContain("first launch", text, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        tempDir.Dispose();
    }
}
