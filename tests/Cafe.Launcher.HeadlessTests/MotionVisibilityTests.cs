using System.Collections.Concurrent;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Cafe.Launcher.UI.Controls;
using Cafe.Launcher.UI.Helpers;

namespace Cafe.Launcher.HeadlessTests;

[Collection(nameof(MotionVisibilityTests))]
public sealed class MotionVisibilityTests
{
    [AvaloniaFact]
    public async Task CloseWithMotion_BeforeExitCompletes_RemainsVisible()
    {
        var originalDuration = AnimationTimings.ExitAnimationDuration;
        try
        {
            AnimationTimings.ExitAnimationDuration = Timeout.InfiniteTimeSpan;
            var overlay = new Grid();
            MotionVisibility.SetIsMotionEnabled(overlay, true);
            MotionVisibility.SetIsOpen(overlay, true);

            MotionVisibility.SetIsOpen(overlay, false);
            var pendingExit = MotionVisibility.WaitForPendingExitAsync(overlay);

            Assert.True(overlay.IsVisible);
            Assert.Contains("motion-exit", overlay.Classes);
            Assert.False(pendingExit.IsCompleted);

            MotionVisibility.SetIsOpen(overlay, true);
            await pendingExit.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(overlay.IsVisible);
            Assert.Contains("motion-enter", overlay.Classes);
            Assert.DoesNotContain("motion-exit", overlay.Classes);

            AnimationTimings.ExitAnimationDuration = TimeSpan.Zero;
            MotionVisibility.SetIsOpen(overlay, false);
            await MotionVisibility.WaitForPendingExitAsync(overlay).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.False(overlay.IsVisible);
            Assert.DoesNotContain("motion-exit", overlay.Classes);
        }
        finally
        {
            AnimationTimings.ExitAnimationDuration = originalDuration;
        }
    }

    [AvaloniaFact]
    public void CloseWithoutMotion_WhenRequested_HidesImmediately()
    {
        var originalDuration = AnimationTimings.ExitAnimationDuration;
        try
        {
            AnimationTimings.ExitAnimationDuration = TimeSpan.FromSeconds(1);
            var overlay = new Grid();
            MotionVisibility.SetIsMotionEnabled(overlay, false);
            MotionVisibility.SetIsOpen(overlay, true);

            MotionVisibility.SetIsOpen(overlay, false);

            Assert.False(overlay.IsVisible);
            Assert.DoesNotContain("motion-enter", overlay.Classes);
            Assert.DoesNotContain("motion-exit", overlay.Classes);
            Assert.True(MotionVisibility.WaitForPendingExitAsync(overlay).IsCompleted);
        }
        finally
        {
            AnimationTimings.ExitAnimationDuration = originalDuration;
        }
    }

    [AvaloniaFact]
    public async Task Reopen_DuringPendingExit_RemainsVisible()
    {
        var originalDuration = AnimationTimings.ExitAnimationDuration;
        try
        {
            var overlay = new Grid();
            MotionVisibility.SetIsMotionEnabled(overlay, true);
            MotionVisibility.SetIsOpen(overlay, true);
            var queuedExit = await BeginQueuedExitAsync(overlay);

            MotionVisibility.SetIsOpen(overlay, true);
            queuedExit.Context.RunPostedCallbacks();
            await queuedExit.PendingExit.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(overlay.IsVisible);
            Assert.Contains("motion-enter", overlay.Classes);
            Assert.DoesNotContain("motion-exit", overlay.Classes);
        }
        finally
        {
            AnimationTimings.ExitAnimationDuration = originalDuration;
        }
    }

    [AvaloniaFact]
    public async Task DisableMotion_DuringPendingExit_HidesImmediatelyAndClearsPendingWork()
    {
        var originalDuration = AnimationTimings.ExitAnimationDuration;
        try
        {
            var overlay = new Grid();
            MotionVisibility.SetIsMotionEnabled(overlay, true);
            MotionVisibility.SetIsOpen(overlay, true);
            var queuedExit = await BeginQueuedExitAsync(overlay);

            MotionVisibility.SetIsMotionEnabled(overlay, false);

            Assert.False(overlay.IsVisible);
            Assert.DoesNotContain("motion-exit", overlay.Classes);
            Assert.True(MotionVisibility.WaitForPendingExitAsync(overlay).IsCompleted);

            MotionVisibility.SetIsOpen(overlay, true);
            queuedExit.Context.RunPostedCallbacks();
            await queuedExit.PendingExit.WaitAsync(TimeSpan.FromSeconds(5));

            // Reduced 档打开不带入场锚点：之后开启动效时选择器无从重新匹配。
            Assert.True(overlay.IsVisible);
            Assert.DoesNotContain("motion-enter", overlay.Classes);
            Assert.DoesNotContain("motion-exit", overlay.Classes);
        }
        finally
        {
            AnimationTimings.ExitAnimationDuration = originalDuration;
        }
    }

    [AvaloniaFact]
    public void OpenWithMotionDisabled_ThenEnableMotion_DoesNotReplayEntrance()
    {
        var overlay = new Grid();
        MotionVisibility.SetIsMotionEnabled(overlay, false);
        MotionVisibility.SetIsOpen(overlay, true);

        Assert.True(overlay.IsVisible);
        Assert.DoesNotContain("motion-enter", overlay.Classes);

        MotionVisibility.SetIsMotionEnabled(overlay, true);

        Assert.True(overlay.IsVisible);
        Assert.DoesNotContain("motion-enter", overlay.Classes);
    }

    [AvaloniaFact]
    public void DisableMotionWhileOpen_StripsEntranceAnchor_AndReenableDoesNotRestoreIt()
    {
        var overlay = new Grid();
        MotionVisibility.SetIsMotionEnabled(overlay, true);
        MotionVisibility.SetIsOpen(overlay, true);
        Assert.Contains("motion-enter", overlay.Classes);

        MotionVisibility.SetIsMotionEnabled(overlay, false);

        Assert.True(overlay.IsVisible);
        Assert.DoesNotContain("motion-enter", overlay.Classes);

        MotionVisibility.SetIsMotionEnabled(overlay, true);

        Assert.True(overlay.IsVisible);
        Assert.DoesNotContain("motion-enter", overlay.Classes);
    }

    private static async Task<QueuedExit> BeginQueuedExitAsync(Grid overlay)
    {
        AnimationTimings.ExitAnimationDuration = TimeSpan.FromMilliseconds(100);
        var context = new QueuedSynchronizationContext();
        var originalContext = SynchronizationContext.Current;
        Task pendingExit;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            MotionVisibility.SetIsOpen(overlay, false);
            pendingExit = MotionVisibility.WaitForPendingExitAsync(overlay);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }

        await context.WaitForPostAsync().WaitAsync(TimeSpan.FromSeconds(5));
        return new QueuedExit(context, pendingExit);
    }

    private sealed record QueuedExit(
        QueuedSynchronizationContext Context,
        Task PendingExit);

    private sealed class QueuedSynchronizationContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> callbacks =
            new();
        private readonly TaskCompletionSource postCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override void Post(SendOrPostCallback callback, object? state)
        {
            callbacks.Enqueue((callback, state));
            postCompletion.TrySetResult();
        }

        public Task WaitForPostAsync() => postCompletion.Task;

        public void RunPostedCallbacks()
        {
            while (callbacks.TryDequeue(out var callback))
            {
                callback.Callback(callback.State);
            }
        }
    }
}

[Collection(nameof(MotionVisibilityTests))]
public sealed class MotionEntranceTests
{
    [AvaloniaFact]
    public async Task TriggeredBeforeAttach_AppliesAnchorOncePresentedAndRetiresAfterEntranceWindow()
    {
        var overlay = new Grid();
        MotionEntrance.SetIsTriggered(overlay, true);
        var window = new Window { Content = overlay };
        try
        {
            // 未呈现前不得挂锚点：动画会停在起势帧（Opacity=0）且摘类不回退。
            Assert.DoesNotContain("motion-enter", overlay.Classes);
            window.Show();

            await HeadlessTestHost.WaitUntilAsync(
                () => overlay.Classes.Contains("motion-enter"),
                TimeSpan.FromSeconds(3),
                "Entrance anchor was not applied after the surface was presented.");
            await HeadlessTestHost.WaitUntilAsync(
                () => !overlay.Classes.Contains("motion-enter"),
                TimeSpan.FromSeconds(3),
                "Entrance anchor did not retire after the entrance window.");
            Assert.Equal(1d, overlay.Opacity);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task TriggeredWhileHidden_SkipsEntranceAndResumesOnNextTrigger()
    {
        var overlay = new Grid();
        var host = new Border { Child = overlay, IsVisible = false };
        var window = new Window { Content = host };
        window.Show();
        try
        {
            // 祖先隐藏时触发（叠层还没打开）不挂锚点：隐藏中挂类，动画既不会在显现时
            // 重放，还可能落到不可见状态；Avalonia 也不为有效可见性变化发通知。
            MotionEntrance.SetIsTriggered(overlay, true);
            Assert.DoesNotContain("motion-enter", overlay.Classes);

            host.IsVisible = true;
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain("motion-enter", overlay.Classes);
            Assert.Equal(1d, overlay.Opacity);

            // 之后的一次真实触发（如切换设置分类）正常入场并在窗期后摘除。
            MotionEntrance.SetIsTriggered(overlay, false);
            MotionEntrance.SetIsTriggered(overlay, true);
            Assert.Contains("motion-enter", overlay.Classes);
            await HeadlessTestHost.WaitUntilAsync(
                () => !overlay.Classes.Contains("motion-enter"),
                TimeSpan.FromSeconds(3),
                "Entrance anchor did not retire after the entrance window.");
            Assert.Equal(1d, overlay.Opacity);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Retrigger_AfterRetirement_ReplaysAnchorOnlyOnNewTrigger()
    {
        var overlay = new Grid();
        var window = new Window { Content = overlay };
        window.Show();
        try
        {
            MotionEntrance.SetIsTriggered(overlay, true);
            Assert.Contains("motion-enter", overlay.Classes);
            await HeadlessTestHost.WaitUntilAsync(
                () => !overlay.Classes.Contains("motion-enter"),
                TimeSpan.FromSeconds(3),
                "Entrance anchor did not retire after the entrance window.");

            // 锚点摘除后属性保持 true 不得重新挂类——这正是重开动效偏好的场景。
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain("motion-enter", overlay.Classes);

            MotionEntrance.SetIsTriggered(overlay, false);
            MotionEntrance.SetIsTriggered(overlay, true);
            Assert.Contains("motion-enter", overlay.Classes);
        }
        finally
        {
            window.Close();
        }
    }
}

[CollectionDefinition(nameof(MotionVisibilityTests), DisableParallelization = true)]
public sealed class MotionVisibilityTestGroup;
