using System;
using System.Threading;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class CrossProcessLaunchBridgeTests
{
    [Fact]
    public void TryEnterSingleInstance_WhenMutexIsFree_WinsAndBindsEndpoint()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Named mutex/event objects are Windows features.");

        var (launchName, showName, mutexName) = UniqueNames();
        using var bridge = new CrossProcessLaunchBridge(launchName, showName);

        var won = bridge.TryEnterSingleInstance(mutexName, []);

        Assert.True(won);
        Assert.False(bridge.Signal.WaitOne(TimeSpan.FromMilliseconds(150)));
    }

    [Fact]
    public void TryEnterSingleInstance_WhenMutexAlreadyHeld_ForwardsLaunchGameToFirstInstance()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Named mutex/event objects are Windows features.");

        var (launchName, showName, mutexName) = UniqueNames();
        using var first = new CrossProcessLaunchBridge(launchName, showName);
        Assert.True(first.TryEnterSingleInstance(mutexName, []));

        using var second = new CrossProcessLaunchBridge(launchName, showName);
        var won = second.TryEnterSingleInstance(mutexName, ["--launch-game"]);

        Assert.False(won);
        Assert.True(first.Signal.WaitOne(TimeSpan.FromMilliseconds(500)));
    }

    [Fact]
    public void TryEnterSingleInstance_WhenMutexHeldWithoutLaunchArgument_DoesNotForward()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Named mutex/event objects are Windows features.");

        var (launchName, showName, mutexName) = UniqueNames();
        using var first = new CrossProcessLaunchBridge(launchName, showName);
        Assert.True(first.TryEnterSingleInstance(mutexName, []));

        using var second = new CrossProcessLaunchBridge(launchName, showName);
        var won = second.TryEnterSingleInstance(mutexName, []);

        Assert.False(won);
        Assert.False(first.Signal.WaitOne(TimeSpan.FromMilliseconds(250)));
    }

    private static (string Launch, string Show, string Mutex) UniqueNames()
    {
        var suffix = Guid.NewGuid().ToString("N");
        return (
            $@"Local\Cafe_Launcher_Test_Launch_{suffix}",
            $@"Local\Cafe_Launcher_Test_Show_{suffix}",
            $@"Local\Cafe_Launcher_Test_Mutex_{suffix}");
    }
}
