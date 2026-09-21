using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class CrossProcessLaunchBridgeTests
{
    [Fact]
    public void TryEnterSingleInstance_WhenGateIsFree_WinsAndBindsEndpoints()
    {
        var (launchName, showName, lockName, mutexName) = UniqueNames();
        using var bridge = new CrossProcessLaunchBridge(launchName, showName, lockName, TestDataRoot.ForCurrentProcess() );

        var won = bridge.TryEnterSingleInstance(mutexName, []);

        Assert.True(won);
        Assert.False(bridge.Signal.WaitOne(TimeSpan.FromMilliseconds(150)));
        Assert.False(bridge.ShowSignal.WaitOne(TimeSpan.FromMilliseconds(150)));
    }

    [Fact]
    public void TryEnterSingleInstance_WhenSingleInstanceAlreadyHeld_ForwardsLaunchGameToFirstInstance()
    {
        var (launchName, showName, lockName, mutexName) = UniqueNames();
        using var first = new CrossProcessLaunchBridge(launchName, showName, lockName, TestDataRoot.ForCurrentProcess() );
        Assert.True(first.TryEnterSingleInstance(mutexName, []));

        using var second = new CrossProcessLaunchBridge(launchName, showName, lockName, TestDataRoot.ForCurrentProcess() );
        var won = second.TryEnterSingleInstance(mutexName, ["--launch-game"]);

        Assert.False(won);
        Assert.True(first.Signal.WaitOne(TimeSpan.FromMilliseconds(500)));
    }

    [Fact]
    public void TryEnterSingleInstance_WhenHeldWithoutLaunchArgument_DoesNotForward()
    {
        var (launchName, showName, lockName, mutexName) = UniqueNames();
        using var first = new CrossProcessLaunchBridge(launchName, showName, lockName, TestDataRoot.ForCurrentProcess() );
        Assert.True(first.TryEnterSingleInstance(mutexName, []));

        using var second = new CrossProcessLaunchBridge(launchName, showName, lockName, TestDataRoot.ForCurrentProcess() );
        var won = second.TryEnterSingleInstance(mutexName, []);

        Assert.False(won);
        Assert.False(first.Signal.WaitOne(TimeSpan.FromMilliseconds(250)));
    }

    [Fact]
    public void TryEnterSingleInstance_WhenSingleInstanceAlreadyHeld_RaisesShowWindowSignalToFirstInstance()
    {
        var (launchName, showName, lockName, mutexName) = UniqueNames();
        using var first = new CrossProcessLaunchBridge(launchName, showName, lockName, TestDataRoot.ForCurrentProcess() );
        Assert.True(first.TryEnterSingleInstance(mutexName, []));

        using var second = new CrossProcessLaunchBridge(launchName, showName, lockName, TestDataRoot.ForCurrentProcess() );
        var won = second.TryEnterSingleInstance(mutexName, []);

        Assert.False(won);
        Assert.True(first.ShowSignal.WaitOne(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void TryEnterSingleInstance_WhenStaleLockFileRemains_WinsAndTakesOver()
    {
        // 崩溃残留：锁文件还在、背后没有监听者。下一个实例必须能接管，
        // 而不是被残留文件永久堵在门外。
        // 注意 Dispose 一个已绑定的套接字会顺带删除套接字文件（优雅退出零残留），
        // 因此用「绑定后不 Listen、保持打开」模拟 SIGKILL 留下的残留。
        var (launchName, showName, lockName, mutexName) = UniqueNames();
        var root = TestDataRoot.ForCurrentProcess();
        var lockPath = CrossProcessLaunchSignal.GetSocketFilePath(root.Root, lockName);
        if (OperatingSystem.IsWindows())
        {
            using var bridge = new CrossProcessLaunchBridge(launchName, showName, lockName, root);
            Assert.True(bridge.TryEnterSingleInstance(mutexName, []));
            return;
        }

        var dead = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            dead.Bind(new UnixDomainSocketEndPoint(lockPath));
            Assert.True(Path.Exists(lockPath));

            using var bridge = new CrossProcessLaunchBridge(launchName, showName, lockName, root);

            Assert.True(bridge.TryEnterSingleInstance(mutexName, []));
        }
        finally
        {
            dead.Dispose();
        }
    }

    [Fact]
    public void TryEnterSingleInstance_AfterFirstInstanceReleases_NextInstanceTakesOver()
    {
        // 锁随进程退场释放（Dispose / 崩溃残留恢复），下一个实例必须立即接管；
        // 跨 POSIX 会话的跨进程部分由实机 setsid 复验与 CI Linux job 覆盖（ADR-034）。
        var (launchName, showName, lockName, mutexName) = UniqueNames();
        using var first = new CrossProcessLaunchBridge(launchName, showName, lockName, TestDataRoot.ForCurrentProcess() );
        Assert.True(first.TryEnterSingleInstance(mutexName, []));
        first.Dispose();

        // 首实例退场后，同名的下一个实例必须立即接管（锁随进程死亡释放）。
        using var next = new CrossProcessLaunchBridge(launchName, showName, lockName, TestDataRoot.ForCurrentProcess() );
        Assert.True(next.TryEnterSingleInstance(mutexName, []));
    }

    private static (string Launch, string Show, string Lock, string Mutex) UniqueNames()
    {
        var suffix = Guid.NewGuid().ToString("N");
        return (
            $@"Local\Cafe_Launcher_Test_Launch_{suffix}",
            $@"Local\Cafe_Launcher_Test_Show_{suffix}",
            $@"Local\Cafe_Launcher_Test_Lock_{suffix}",
            $@"Local\Cafe_Launcher_Test_Mutex_{suffix}");
    }
}
