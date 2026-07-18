using System.Collections.Concurrent;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Cafe.Launcher.Avalonia.Controls;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.HeadlessTests;

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
            await pendingExit;

            Assert.True(overlay.IsVisible);
            Assert.Contains("motion-enter", overlay.Classes);
            Assert.DoesNotContain("motion-exit", overlay.Classes);

            AnimationTimings.ExitAnimationDuration = TimeSpan.Zero;
            MotionVisibility.SetIsOpen(overlay, false);
            await MotionVisibility.WaitForPendingExitAsync(overlay);

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
            await queuedExit.PendingExit;

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
            await queuedExit.PendingExit;

            Assert.True(overlay.IsVisible);
            Assert.Contains("motion-enter", overlay.Classes);
            Assert.DoesNotContain("motion-exit", overlay.Classes);
        }
        finally
        {
            AnimationTimings.ExitAnimationDuration = originalDuration;
        }
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

        await context.WaitForPostAsync();
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

[CollectionDefinition(nameof(MotionVisibilityTests), DisableParallelization = true)]
public sealed class MotionVisibilityTestGroup;
