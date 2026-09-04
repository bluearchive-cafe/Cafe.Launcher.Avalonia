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

    [Fact]
    public void ListenAt_WhenWaitOneCalledBeforeBound_ReturnsFalseImmediately()
    {
        // Unix 域套接字传输：EnsureBound 之前不存在 pending 状态，
        // “先 Set 后启动监听”的信号按设计丢弃（Windows 命名事件的对应契约见下方 Windows 专属用例）。
        var name = UniqueName();
        using var signal = CrossProcessLaunchSignal.ListenAt(name, tempDir);

        Assert.False(signal.WaitOne(TimeSpan.FromMilliseconds(50)));
    }

    [Fact]
    public void WaitOne_WhenEachSignalIsConsumedBeforeNextRaise_DeliversOneWakePerRaise()
    {
        var name = UniqueName();
        using var signal = CrossProcessLaunchSignal.ListenAt(name, tempDir);
        signal.EnsureBound();

        CrossProcessLaunchSignal.RaiseAt(name, tempDir);
        Assert.True(signal.WaitOne(TimeSpan.FromSeconds(5)));
        Assert.False(signal.WaitOne(TimeSpan.FromMilliseconds(100)));

        // AutoReset 复位之后，下一次 Raise 仍然交付一次独立的唤醒。
        CrossProcessLaunchSignal.RaiseAt(name, tempDir);
        Assert.True(signal.WaitOne(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void EnsureBound_WhenCalledTwice_KeepsWorkingListener()
    {
        var name = UniqueName();
        using var signal = CrossProcessLaunchSignal.ListenAt(name, tempDir);
        signal.EnsureBound();

        // 第二次绑定按实现是幂等空操作，不得破坏已建立的监听。
        signal.EnsureBound();

        CrossProcessLaunchSignal.RaiseAt(name, tempDir);
        Assert.True(signal.WaitOne(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Dispose_WhenCalledTwice_IsIdempotent()
    {
        var name = UniqueName();
        var signal = CrossProcessLaunchSignal.ListenAt(name, tempDir);
        signal.EnsureBound();

        signal.Dispose();
        signal.Dispose();
    }

    [Fact]
    public void GetSocketFilePath_WithSameAndDifferentNames_IsStableShortAndDistinct()
    {
        var first = CrossProcessLaunchSignal.GetSocketFilePath(tempDir, "name-a");
        var firstAgain = CrossProcessLaunchSignal.GetSocketFilePath(tempDir, "name-a");
        var second = CrossProcessLaunchSignal.GetSocketFilePath(tempDir, "name-b");

        Assert.Equal(first, firstAgain);
        Assert.NotEqual(first, second);
        // Unix 域套接字路径总长必须显著低于 108 字节，文件名保持短小是硬约束。
        Assert.True(Path.GetFileName(first).Length < 40, $"socket file name too long: {first}");
    }

    [Fact]
    public void Listen_WhenNamedEventSignaledBeforeListen_PreservesSignalAndAutoResets()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Named event objects are Windows features.");
        if (!OperatingSystem.IsWindows())
        {
            return; // CA1416 平台守卫：平台分析器不识别 SkipUnless，非 Windows 由上一行汇报跳过。
        }

        var name = UniqueName();
        // 模拟另一个进程先 create-or-open 并 Set：只要该句柄仍然存活，
        // 命名内核对象及其触发状态都会保留，之后的 Listen 按契约打开同一对象。
        using var preset = new EventWaitHandle(false, EventResetMode.AutoReset, name);
        preset.Set();

        using var signal = CrossProcessLaunchSignal.Listen(name);

        Assert.True(signal.WaitOne(TimeSpan.Zero));
        // AutoReset：一次唤醒之后立即回到未触发状态。
        Assert.False(signal.WaitOne(TimeSpan.Zero));
    }

    [Fact]
    public void Listen_WhenEventSetMultipleTimesBeforeWait_CoalescesIntoSingleWake()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Named event objects are Windows features.");
        if (!OperatingSystem.IsWindows())
        {
            return; // CA1416 平台守卫。
        }

        var name = UniqueName();
        using var signal = CrossProcessLaunchSignal.Listen(name);
        using var external = EventWaitHandle.OpenExisting(name);

        // 等待之前的多次 Set 合并为一次唤醒：内核命名事件是二态对象。
        external.Set();
        external.Set();

        Assert.True(signal.WaitOne(TimeSpan.FromMilliseconds(100)));
        Assert.False(signal.WaitOne(TimeSpan.FromMilliseconds(50)));
    }

    [Fact]
    public void Listen_WhenRaiseHappenedWithoutAnyListener_StartsFromUnsignaledState()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Named event objects are Windows features.");
        if (!OperatingSystem.IsWindows())
        {
            return; // CA1416 平台守卫。
        }

        var name = UniqueName();
        // Raise 在没有任何监听者时创建→Set→关闭句柄；最后一个句柄关闭后命名对象被销毁，
        // 因此之后的 Listen 拿到的是全新的未触发事件——这正是生产代码必须
        // “先建事件、后抢互斥量”的原因（转发请求可能落空的窗口）。
        CrossProcessLaunchSignal.Raise(name);

        using var signal = CrossProcessLaunchSignal.Listen(name);

        Assert.False(signal.WaitOne(TimeSpan.Zero));
    }
}
