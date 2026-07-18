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
            AnimationTimings.ExitAnimationDuration = TimeSpan.FromMilliseconds(30);
            var overlay = new Grid();
            MotionVisibility.SetIsMotionEnabled(overlay, true);
            MotionVisibility.SetIsOpen(overlay, true);
            MotionVisibility.SetIsOpen(overlay, false);

            MotionVisibility.SetIsOpen(overlay, true);
            await MotionVisibility.WaitForPendingExitAsync(overlay);

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
    public void DisableMotion_DuringPendingExit_HidesImmediatelyAndClearsPendingWork()
    {
        var originalDuration = AnimationTimings.ExitAnimationDuration;
        try
        {
            AnimationTimings.ExitAnimationDuration = TimeSpan.FromSeconds(1);
            var overlay = new Grid();
            MotionVisibility.SetIsMotionEnabled(overlay, true);
            MotionVisibility.SetIsOpen(overlay, true);
            MotionVisibility.SetIsOpen(overlay, false);

            MotionVisibility.SetIsMotionEnabled(overlay, false);

            Assert.False(overlay.IsVisible);
            Assert.DoesNotContain("motion-exit", overlay.Classes);
            Assert.True(MotionVisibility.WaitForPendingExitAsync(overlay).IsCompleted);
        }
        finally
        {
            AnimationTimings.ExitAnimationDuration = originalDuration;
        }
    }
}

[CollectionDefinition(nameof(MotionVisibilityTests), DisableParallelization = true)]
public sealed class MotionVisibilityTestGroup;
