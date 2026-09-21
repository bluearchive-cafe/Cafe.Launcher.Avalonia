using System;
using System.Threading;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class CrossProcessLaunchBridgeTests
{
    [Fact]
    public void TryEnterSingleInstance_WhenMutexIsFree_WinsAndBindsEndpoint()
    {
        var (launchName, showName, mutexName) = UniqueNames();
        using var bridge = new CrossProcessLaunchBridge(launchName, showName, TestDataRoot.ForCurrentProcess() );

        var won = bridge.TryEnterSingleInstance(mutexName, []);

        Assert.True(won);
        Assert.False(bridge.Signal.WaitOne(TimeSpan.FromMilliseconds(150)));
        Assert.False(bridge.ShowSignal.WaitOne(TimeSpan.FromMilliseconds(150)));
    }

    [Fact]
    public void TryEnterSingleInstance_WhenMutexAlreadyHeld_ForwardsLaunchGameToFirstInstance()
    {
        var (launchName, showName, mutexName) = UniqueNames();
        using var first = new CrossProcessLaunchBridge(launchName, showName, TestDataRoot.ForCurrentProcess() );
        Assert.True(first.TryEnterSingleInstance(mutexName, []));

        using var second = new CrossProcessLaunchBridge(launchName, showName, TestDataRoot.ForCurrentProcess() );
        var won = second.TryEnterSingleInstance(mutexName, ["--launch-game"]);

        Assert.False(won);
        Assert.True(first.Signal.WaitOne(TimeSpan.FromMilliseconds(500)));
    }

    [Fact]
    public void TryEnterSingleInstance_WhenMutexHeldWithoutLaunchArgument_DoesNotForward()
    {
        var (launchName, showName, mutexName) = UniqueNames();
        using var first = new CrossProcessLaunchBridge(launchName, showName, TestDataRoot.ForCurrentProcess() );
        Assert.True(first.TryEnterSingleInstance(mutexName, []));

        using var second = new CrossProcessLaunchBridge(launchName, showName, TestDataRoot.ForCurrentProcess() );
        var won = second.TryEnterSingleInstance(mutexName, []);

        Assert.False(won);
        Assert.False(first.Signal.WaitOne(TimeSpan.FromMilliseconds(250)));
    }

    [Fact]
    public void TryEnterSingleInstance_WhenMutexAlreadyHeld_RaisesShowWindowSignalToFirstInstance()
    {
        var (launchName, showName, mutexName) = UniqueNames();
        using var first = new CrossProcessLaunchBridge(launchName, showName, TestDataRoot.ForCurrentProcess() );
        Assert.True(first.TryEnterSingleInstance(mutexName, []));

        using var second = new CrossProcessLaunchBridge(launchName, showName, TestDataRoot.ForCurrentProcess() );
        var won = second.TryEnterSingleInstance(mutexName, []);

        Assert.False(won);
        Assert.True(first.ShowSignal.WaitOne(TimeSpan.FromSeconds(5)));
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
