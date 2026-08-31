using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class CrossProcessLaunchSignalTests : IDisposable
{
    private readonly string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public CrossProcessLaunchSignalTests()
    {
        Directory.CreateDirectory(tempDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(tempDir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup of the per-test socket directory.
        }
    }

    private static string UniqueName() => "Local\\CafeTest_Signal_" + Guid.NewGuid().ToString("N");

    [Fact]
    public void Raise_WhenFirstInstanceListens_ReturnsOnceAndAutoResets()
    {
        var name = UniqueName();
        using var signal = CrossProcessLaunchSignal.Listen(name);
        signal.EnsureBound();

        CrossProcessLaunchSignal.Raise(name);

        Assert.True(signal.WaitOne(TimeSpan.FromSeconds(5)));
        Assert.False(signal.WaitOne(TimeSpan.FromMilliseconds(50)));
    }

    [Fact]
    public void Raise_WhenNoFirstInstance_DoesNotThrow()
    {
        var name = UniqueName();

        CrossProcessLaunchSignal.Raise(name);
    }

    [Fact]
    public void ListenAt_WhenUnixSocketRaised_ReturnsOnceAndAutoResets()
    {
        var name = UniqueName();
        using var signal = CrossProcessLaunchSignal.ListenAt(name, tempDir);
        signal.EnsureBound();

        CrossProcessLaunchSignal.RaiseAt(name, tempDir);

        Assert.True(signal.WaitOne(TimeSpan.FromSeconds(5)));
        Assert.False(signal.WaitOne(TimeSpan.FromMilliseconds(50)));
    }

    [Fact]
    public void ListenAt_WhenRaisedTwice_DeliversAtLeastOneSignal()
    {
        var name = UniqueName();
        using var signal = CrossProcessLaunchSignal.ListenAt(name, tempDir);
        signal.EnsureBound();

        CrossProcessLaunchSignal.RaiseAt(name, tempDir);
        CrossProcessLaunchSignal.RaiseAt(name, tempDir);

        // 与 Windows 命名事件一致：连续的 Raise 是否合并取决于到达时序，
        // 但至少保证一次唤醒且不抛异常。
        Assert.True(signal.WaitOne(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void ListenAt_WhenStaleSocketFileRemains_RecoversAndListens()
    {
        var name = UniqueName();
        var socketPath = CrossProcessLaunchSignal.GetSocketFilePath(tempDir, name);
        File.WriteAllText(socketPath, "stale");
        using var signal = CrossProcessLaunchSignal.ListenAt(name, tempDir);

        signal.EnsureBound();
        CrossProcessLaunchSignal.RaiseAt(name, tempDir);

        Assert.True(signal.WaitOne(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Dispose_AfterListening_StopsAcceptingWithoutThrowing()
    {
        var name = UniqueName();
        var signal = CrossProcessLaunchSignal.ListenAt(name, tempDir);
        signal.EnsureBound();

        signal.Dispose();

        CrossProcessLaunchSignal.RaiseAt(name, tempDir);
    }
}
