using System;
using System.Threading.Tasks;
using Cafe.Launcher.UI.Helpers;

namespace Cafe.Launcher.Tests;

/// <summary>
/// 验证活动生命周期原语的契约（ADR-051）：租约计数、排空握手、停机后拒绝新工作、
/// 取消与释放的分离，以及释放后的防御性 no-op。所有等待都通过排空任务或有界
/// WaitAsync，不依赖真实时间。
/// </summary>
public sealed class ActivityLifetimeTests
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public void BeginShutdown_WithoutLease_ReturnsCompletedTask()
    {
        using var lifetime = new ActivityLifetime();

        Assert.True(lifetime.BeginShutdown().IsCompleted);
        Assert.True(lifetime.IsShuttingDown);
    }

    [Fact]
    public async Task BeginShutdown_WhileLeaseHeld_CompletesOnlyAfterLeaseReleases()
    {
        using var lifetime = new ActivityLifetime();
        var lease = lifetime.TryBegin();
        Assert.NotNull(lease);

        var drain = lifetime.BeginShutdown();
        Assert.False(drain.IsCompleted);

        lease!.Dispose();
        await drain.WaitAsync(WaitTimeout);
    }

    [Fact]
    public async Task BeginShutdown_WithMultipleLeases_CompletesOnlyAfterLastRelease()
    {
        using var lifetime = new ActivityLifetime();
        var first = lifetime.TryBegin();
        var second = lifetime.TryBegin();
        Assert.NotNull(first);
        Assert.NotNull(second);

        var drain = lifetime.BeginShutdown();
        Assert.False(drain.IsCompleted);

        first!.Dispose();
        Assert.False(drain.IsCompleted);

        second!.Dispose();
        await drain.WaitAsync(WaitTimeout);
    }

    [Fact]
    public void TryBegin_AfterShutdown_ReturnsNull()
    {
        using var lifetime = new ActivityLifetime();
        _ = lifetime.BeginShutdown();

        Assert.Null(lifetime.TryBegin());
    }

    [Fact]
    public void BeginShutdown_Twice_ReturnsTheSameDrainTask()
    {
        using var lifetime = new ActivityLifetime();
        using var lease = lifetime.TryBegin();

        var first = lifetime.BeginShutdown();

        Assert.Same(first, lifetime.BeginShutdown());
        Assert.False(first.IsCompleted);
    }

    [Fact]
    public void Cancel_DoesNotCloseNewWork()
    {
        using var lifetime = new ActivityLifetime();

        lifetime.Cancel();

        Assert.True(lifetime.LifetimeToken.IsCancellationRequested);
        Assert.False(lifetime.IsShuttingDown);

        var lease = lifetime.TryBegin();
        Assert.NotNull(lease);
        lease!.Dispose();
    }

    [Fact]
    public void Dispose_WithoutLease_CancelsTokenAndRejectsNewWork()
    {
        var lifetime = new ActivityLifetime();

        lifetime.Dispose();

        Assert.True(lifetime.IsShuttingDown);
        Assert.True(lifetime.LifetimeToken.IsCancellationRequested);
        Assert.Null(lifetime.TryBegin());

        // 重复释放与释放后取消都是 no-op。
        lifetime.Dispose();
        lifetime.Cancel();
    }

    [Fact]
    public async Task Dispose_WhileLeaseHeld_CancelsTokenButStillWaitsForLease()
    {
        var lifetime = new ActivityLifetime();
        var lease = lifetime.TryBegin();
        Assert.NotNull(lease);
        var drain = lifetime.BeginShutdown();

        lifetime.Dispose();

        Assert.True(lifetime.LifetimeToken.IsCancellationRequested);
        Assert.False(drain.IsCompleted);

        lease!.Dispose();
        await drain.WaitAsync(WaitTimeout);
        lifetime.Dispose();
    }

    [Fact]
    public void Lease_DisposeTwice_ReleasesOnce()
    {
        using var lifetime = new ActivityLifetime();
        var first = lifetime.TryBegin()!;
        var second = lifetime.TryBegin()!;
        var drain = lifetime.BeginShutdown();

        first.Dispose();
        first.Dispose();

        Assert.False(drain.IsCompleted);

        second.Dispose();
        Assert.True(drain.IsCompleted);
    }
}
