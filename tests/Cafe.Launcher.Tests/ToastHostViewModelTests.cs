using Cafe.Launcher.UI.Constants;
using System.Collections.Concurrent;
using System.Globalization;
using Cafe.Launcher.Composition;
using Cafe.Launcher.UI.Features.Settings;
using Cafe.Launcher.UI.Helpers;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.Core.Services.Diagnostics;
using Cafe.Launcher.Testing;
using Cafe.Launcher.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Cafe.Launcher.Core.Models;

namespace Cafe.Launcher.Tests;

[Collection(nameof(LocalizationServiceTestIsolation))]
public sealed class ToastHostViewModelTests : IDisposable
{
    private readonly object invokeGate = new();
    private readonly TestDirectory tempDir = TestDirectory.Create();

    [Fact]
    public async Task ToastRaised_WithAction_DoesNotStartDisplayDelay()
    {
        await using var provider = CreateProvider();
        var toastService = provider.GetRequiredService<ToastService>();
        var delayCalls = 0;
        using var viewModel = new ToastHostViewModel(
            toastService,
            provider.GetRequiredService<LocalizationService>(),
            new LocalDiagnostics(),
            InvokeSerially,
            (_, _) =>
            {
                delayCalls++;
                return Task.CompletedTask;
            });

        toastService.Show(CreateActionOptions());
        await WaitUntilAsync(() => viewModel.ActiveToasts.Count == 1);

        Assert.Equal(0, delayCalls);
    }

    [Fact]
    public async Task ToastRaised_WithFiniteDuration_ExpiresAfterDuration()
    {
        await using var provider = CreateProvider();
        var toastService = provider.GetRequiredService<ToastService>();
        var delays = new ControlledDelay();
        using var viewModel = new ToastHostViewModel(
            toastService,
            provider.GetRequiredService<LocalizationService>(),
            new LocalDiagnostics(),
            InvokeSerially,
            delays.WaitAsync);

        toastService.Show(new ToastOptions
        {
            Message = "saved",
            Duration = ToastDuration.Brief
        });
        await WaitUntilAsync(() => viewModel.ActiveToasts.Count == 1);
        await delays.ReleaseNextAsync();
        await WaitUntilAsync(() => viewModel.ActiveToasts.Count == 0);
    }

    [Fact]
    public async Task ToastRaised_WithAction_DoesNotStartCountdown()
    {
        await using var provider = CreateProvider();
        var toastService = provider.GetRequiredService<ToastService>();
        var delayCalls = 0;

        using var viewModel = new ToastHostViewModel(
            toastService,
            provider.GetRequiredService<LocalizationService>(),
            new LocalDiagnostics(),
            InvokeSerially,
            (_, _) =>
            {
                delayCalls++;
                return Task.CompletedTask;
            });

        toastService.Show(CreateActionOptions());
        await WaitUntilAsync(() => viewModel.ActiveToasts.Count == 1);
        var toast = viewModel.ActiveToasts.Single();

        Assert.True(toast.HasActions);
        Assert.Equal(0, delayCalls);
    }

    [Fact]
    public async Task DismissToast_WhileDurationIsPending_CancelsPendingDelay()
    {
        await using var provider = CreateProvider();
        var toastService = provider.GetRequiredService<ToastService>();
        var delays = new ControlledDelay();
        using var viewModel = new ToastHostViewModel(
            toastService,
            provider.GetRequiredService<LocalizationService>(),
            new LocalDiagnostics(),
            InvokeSerially,
            delays.WaitAsync);

        toastService.Show(new ToastOptions { Message = "saved", Duration = ToastDuration.Brief });
        await WaitUntilAsync(() => viewModel.ActiveToasts.Count == 1);
        var toast = viewModel.ActiveToasts.Single();

        await viewModel.DismissToastCommand.ExecuteAsync(toast.Id);
        await delays.ReleaseNextAsync();
        await WaitUntilAsync(() => viewModel.ActiveToasts.Count == 0);

        Assert.Equal(1, delays.RequestCount);
    }

    [Fact]
    public async Task ExecutePrimaryToastAction_WhenSuccessful_ExecutesOnceAndDismisses()
    {
        await using var provider = CreateProvider();
        var toastService = provider.GetRequiredService<ToastService>();
        var calls = 0;
        var release = new TaskCompletionSource<ToastActionResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var viewModel = new ToastHostViewModel(
            toastService,
            provider.GetRequiredService<LocalizationService>(),
            new LocalDiagnostics(),
            InvokeSerially,
            static (_, _) => Task.CompletedTask);
        toastService.Show(CreateActionOptions(async _ =>
        {
            calls++;
            return await release.Task;
        }));
        await WaitUntilAsync(() => viewModel.ActiveToasts.Count == 1);
        var toast = viewModel.ActiveToasts[0];

        var first = viewModel.ExecutePrimaryToastActionCommand.ExecuteAsync(toast.Id);
        var duplicate = viewModel.ExecutePrimaryToastActionCommand.ExecuteAsync(toast.Id);
        await WaitUntilAsync(() => toast.IsActionExecuting);
        release.SetResult(ToastActionResult.Success());
        await Task.WhenAll(first, duplicate);

        Assert.Equal(1, calls);
        Assert.Empty(viewModel.ActiveToasts);
    }

    [Fact]
    public async Task ExecutePrimaryToastAction_WhenFailure_ReturnsToastToInteractiveErrorState()
    {
        await using var provider = CreateProvider();
        var toastService = provider.GetRequiredService<ToastService>();
        using var viewModel = new ToastHostViewModel(
            toastService,
            provider.GetRequiredService<LocalizationService>(),
            new LocalDiagnostics(),
            InvokeSerially,
            static (_, _) => Task.CompletedTask);
        toastService.Show(CreateActionOptions(_ => Task.FromResult(
            ToastActionResult.Failure("Still offline", "Retry failed"))));
        await WaitUntilAsync(() => viewModel.ActiveToasts.Count == 1);
        var toast = viewModel.ActiveToasts[0];

        await viewModel.ExecutePrimaryToastActionCommand.ExecuteAsync(toast.Id);

        Assert.Equal(ToastSeverity.Error, toast.Severity);
        Assert.Equal("Retry failed", toast.Title);
        Assert.Equal("Still offline", toast.Message);
        Assert.False(toast.IsActionExecuting);
        Assert.Contains(toast, viewModel.ActiveToasts);
    }

    [Fact]
    public async Task ExecuteSecondaryToastAction_WhenUnexpectedException_UsesLocalizedFallback()
    {
        await using var provider = CreateProvider();
        var localizer = provider.GetRequiredService<LocalizationService>();
        var toastService = provider.GetRequiredService<ToastService>();
        using var viewModel = new ToastHostViewModel(
            toastService,
            localizer,
            new LocalDiagnostics(),
            InvokeSerially,
            static (_, _) => Task.CompletedTask);
        toastService.Show(CreateActionOptions(
            secondary: _ => throw new InvalidOperationException("secret detail")));
        await WaitUntilAsync(() => viewModel.ActiveToasts.Count == 1);
        var toast = viewModel.ActiveToasts[0];

        await viewModel.ExecuteSecondaryToastActionCommand.ExecuteAsync(toast.Id);

        Assert.Equal(localizer.T(LocalizationKeys.ToastActionFailedTitle), toast.Title);
        Assert.Equal(localizer.T(LocalizationKeys.ToastActionFailedMessage), toast.Message);
        Assert.DoesNotContain("secret detail", toast.Message, StringComparison.Ordinal);
        Assert.False(toast.IsActionExecuting);
    }

    [Fact]
    public async Task ToastRaised_WhenNotificationsAreEnabled_AddsThenExpiresToast()
    {
        await using var provider = CreateProvider();
        var settings = provider.GetRequiredService<SettingsViewModel>();
        settings.Editor.ApplySnapshot(new LauncherSettings
        {
        });
        var toastService = provider.GetRequiredService<ToastService>();
        var delay = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var viewModel = new ToastHostViewModel(
            toastService,
            provider.GetRequiredService<LocalizationService>(),
            new LocalDiagnostics(),
            InvokeSerially,
            (_, cancellationToken) => delay.Task.WaitAsync(cancellationToken));

        toastService.ShowSuccess("saved");
        await WaitUntilAsync(() => viewModel.ActiveToasts.Count == 1);

        Assert.Equal(ToastSeverity.Success, viewModel.ActiveToasts[0].Severity);
        Assert.NotEmpty(viewModel.ActiveToasts[0].SeverityLabel);

        delay.TrySetResult();
        await WaitUntilAsync(() => viewModel.ActiveToasts.Count == 0);
    }

    [Fact]
    public async Task ToastRaised_AddsToastWithoutNotificationPreference()
    {
        await using var provider = CreateProvider();
        var toastService = provider.GetRequiredService<ToastService>();
        var delay = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var viewModel = new ToastHostViewModel(
            toastService,
            provider.GetRequiredService<LocalizationService>(),
            new LocalDiagnostics(),
            InvokeSerially,
            (_, cancellationToken) => delay.Task.WaitAsync(cancellationToken));

        toastService.Show("shown");
        await WaitUntilAsync(() => viewModel.ActiveToasts.Count == 1);

        Assert.Equal("shown", viewModel.ActiveToasts.Single().Message);

        delay.TrySetResult();
        await WaitUntilAsync(() => viewModel.ActiveToasts.Count == 0);
    }

    [Fact]
    public async Task DismissToastCommand_WithFullMotion_MarksExitingBeforeRemovingToast()
    {
        await using var provider = CreateProvider();
        var settings = provider.GetRequiredService<SettingsViewModel>();
        settings.Editor.ApplySnapshot(new LauncherSettings
        {
        });
        var toastService = provider.GetRequiredService<ToastService>();
        var displayDelay = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var exitDelay = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var viewModel = new ToastHostViewModel(
            toastService,
            provider.GetRequiredService<LocalizationService>(),
            new LocalDiagnostics(),
            InvokeSerially,
            (delay, cancellationToken) =>
                (delay == AnimationTimings.ExitAnimationDuration ? exitDelay : displayDelay)
                    .Task.WaitAsync(cancellationToken));
        viewModel.ApplyMotionPreference(reduceMotion: false);
        toastService.ShowWarning("warning");
        await WaitUntilAsync(() => viewModel.ActiveToasts.Count == 1);
        var toast = viewModel.ActiveToasts[0];

        var dismissTask = viewModel.DismissToastCommand.ExecuteAsync(toast.Id);
        await WaitUntilAsync(() => toast.IsExiting);

        Assert.True(toast.IsExiting);
        Assert.Contains(toast, viewModel.ActiveToasts);

        exitDelay.TrySetResult();
        await dismissTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(viewModel.ActiveToasts);
        displayDelay.TrySetResult();
    }

    [Fact]
    public async Task DismissToastCommand_WithReducedMotion_RemovesImmediatelyWithoutExitDelay()
    {
        await using var provider = CreateProvider();
        var settings = provider.GetRequiredService<SettingsViewModel>();
        settings.Editor.ApplySnapshot(new LauncherSettings
        {
        });
        var toastService = provider.GetRequiredService<ToastService>();
        var displayDelay = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var exitDelayCalls = 0;
        using var viewModel = new ToastHostViewModel(
            toastService,
            provider.GetRequiredService<LocalizationService>(),
            new LocalDiagnostics(),
            InvokeSerially,
            (delay, cancellationToken) =>
            {
                if (delay == AnimationTimings.ExitAnimationDuration)
                {
                    Interlocked.Increment(ref exitDelayCalls);
                    return Task.CompletedTask;
                }

                return displayDelay.Task.WaitAsync(cancellationToken);
            });
        viewModel.ApplyMotionPreference(reduceMotion: true);
        toastService.Show("reduced");
        await WaitUntilAsync(() => viewModel.ActiveToasts.Count == 1);
        var toast = viewModel.ActiveToasts[0];

        await viewModel.DismissToastCommand.ExecuteAsync(toast.Id);

        Assert.Empty(viewModel.ActiveToasts);
        Assert.False(toast.IsExiting);
        Assert.Equal(0, exitDelayCalls);
        displayDelay.TrySetResult();
    }

    [Fact]
    public async Task ToastRaised_WhenDisplayDurationEnds_UsesAnimatedExitPath()
    {
        await using var provider = CreateProvider();
        var settings = provider.GetRequiredService<SettingsViewModel>();
        settings.Editor.ApplySnapshot(new LauncherSettings
        {
        });
        var toastService = provider.GetRequiredService<ToastService>();
        var displayDelay = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var exitDelay = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var viewModel = new ToastHostViewModel(
            toastService,
            provider.GetRequiredService<LocalizationService>(),
            new LocalDiagnostics(),
            InvokeSerially,
            (delay, cancellationToken) =>
                (delay == AnimationTimings.ExitAnimationDuration ? exitDelay : displayDelay)
                    .Task.WaitAsync(cancellationToken));
        viewModel.ApplyMotionPreference(reduceMotion: false);
        toastService.Show("automatic");
        await WaitUntilAsync(() => viewModel.ActiveToasts.Count == 1);
        var toast = viewModel.ActiveToasts[0];

        displayDelay.TrySetResult();
        await WaitUntilAsync(() => toast.IsExiting);

        Assert.Contains(toast, viewModel.ActiveToasts);

        exitDelay.TrySetResult();
        await WaitUntilAsync(() => viewModel.ActiveToasts.Count == 0);
    }

    [Fact]
    public async Task ToastExit_WhenAutomaticAndManualRequestsOverlap_WaitsAndRemovesOnce()
    {
        await using var provider = CreateProvider();
        var settings = provider.GetRequiredService<SettingsViewModel>();
        settings.Editor.ApplySnapshot(new LauncherSettings
        {
        });
        var toastService = provider.GetRequiredService<ToastService>();
        var displayDelay = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var exitDelay = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var exitDelayCalls = 0;
        using var viewModel = new ToastHostViewModel(
            toastService,
            provider.GetRequiredService<LocalizationService>(),
            new LocalDiagnostics(),
            InvokeSerially,
            (delay, cancellationToken) =>
            {
                if (delay != AnimationTimings.ExitAnimationDuration)
                {
                    return displayDelay.Task.WaitAsync(cancellationToken);
                }

                Interlocked.Increment(ref exitDelayCalls);
                return exitDelay.Task.WaitAsync(cancellationToken);
            });
        viewModel.ApplyMotionPreference(reduceMotion: false);
        toastService.Show("overlap", ToastSeverity.Info, ToastDuration.Brief);
        await WaitUntilAsync(() => viewModel.ActiveToasts.Count == 1);
        var toast = viewModel.ActiveToasts[0];
        var removeCount = 0;
        viewModel.ActiveToasts.CollectionChanged += (_, args) =>
        {
            if (args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Remove)
            {
                Interlocked.Increment(ref removeCount);
            }
        };

        displayDelay.TrySetResult();
        await WaitUntilAsync(() => toast.IsExiting);
        var dismissTask = viewModel.DismissToastCommand.ExecuteAsync(toast.Id);

        Assert.Equal(1, exitDelayCalls);
        Assert.False(dismissTask.IsCompleted);

        exitDelay.TrySetResult();
        await dismissTask.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitUntilAsync(() => viewModel.ActiveToasts.Count == 0);

        Assert.Equal(1, removeCount);
        Assert.Equal(1, exitDelayCalls);
    }

    [Fact]
    public async Task DismissToastCommand_WhenAnotherToastIsExiting_StartsIndependentExit()
    {
        await using var provider = CreateProvider();
        var settings = provider.GetRequiredService<SettingsViewModel>();
        settings.Editor.ApplySnapshot(new LauncherSettings
        {
        });
        var toastService = provider.GetRequiredService<ToastService>();
        var displayDelay = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var exitDelay = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var viewModel = new ToastHostViewModel(
            toastService,
            provider.GetRequiredService<LocalizationService>(),
            new LocalDiagnostics(),
            InvokeSerially,
            (delay, cancellationToken) =>
                (delay == AnimationTimings.ExitAnimationDuration ? exitDelay : displayDelay)
                    .Task.WaitAsync(cancellationToken));
        viewModel.ApplyMotionPreference(reduceMotion: false);
        toastService.Show("first");
        toastService.Show("second");
        await WaitUntilAsync(() => viewModel.ActiveToasts.Count == 2);
        var firstToast = viewModel.ActiveToasts[0];
        var secondToast = viewModel.ActiveToasts[1];

        var firstDismissTask = viewModel.DismissToastCommand.ExecuteAsync(firstToast.Id);
        Assert.True(firstToast.IsExiting);
        Assert.True(viewModel.DismissToastCommand.CanExecute(secondToast.Id));

        var secondDismissTask = viewModel.DismissToastCommand.ExecuteAsync(secondToast.Id);

        Assert.True(secondToast.IsExiting);

        exitDelay.TrySetResult();
        await Task.WhenAll(firstDismissTask, secondDismissTask);

        Assert.Empty(viewModel.ActiveToasts);
        displayDelay.TrySetResult();
    }

    [Fact]
    public async Task ShowToastAsync_WhenNewNotificationArrives_InsertsNewestToastFirst()
    {
        await using var provider = CreateProvider();
        var settings = provider.GetRequiredService<SettingsViewModel>();
        settings.Editor.ApplySnapshot(new LauncherSettings());
        var toastService = provider.GetRequiredService<ToastService>();
        var displayDelay = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var viewModel = new ToastHostViewModel(
            toastService,
            provider.GetRequiredService<LocalizationService>(),
            new LocalDiagnostics(),
            InvokeSerially,
            (_, cancellationToken) => displayDelay.Task.WaitAsync(cancellationToken));

        toastService.Show("first");
        await WaitUntilAsync(() => viewModel.ActiveToasts.Count == 1);
        var firstToast = viewModel.ActiveToasts.Single();

        toastService.Show("second");
        await WaitUntilAsync(() => viewModel.ActiveToasts.Count == 2);

        Assert.Equal("second", viewModel.ActiveToasts[0].Message);
        Assert.Same(firstToast, viewModel.ActiveToasts[1]);
    }

    [Fact]
    public async Task ToastRaised_WithSameMergeKey_MergesIntoTheExistingCard()
    {
        await using var provider = CreateProvider();
        var localizer = provider.GetRequiredService<LocalizationService>();
        var toastService = provider.GetRequiredService<ToastService>();
        var delays = new ControlledDelay();
        using var viewModel = new ToastHostViewModel(
            toastService,
            localizer,
            new LocalDiagnostics(),
            InvokeSerially,
            delays.WaitAsync);

        toastService.Show(new ToastOptions
        {
            Message = "first failure",
            Key = "verify-failed",
            Duration = ToastDuration.Brief
        });
        await WaitUntilAsync(() => viewModel.ActiveToasts.Count == 1);
        await WaitUntilAsync(() => delays.RequestCount == 1);
        var first = viewModel.ActiveToasts.Single();

        toastService.Show(new ToastOptions
        {
            Message = "second failure",
            Key = "verify-failed",
            Duration = ToastDuration.Brief
        });
        await WaitUntilAsync(() => first.MergeCount == 2);

        var merged = Assert.Single(viewModel.ActiveToasts);
        Assert.Same(first, merged);
        Assert.Equal("second failure", merged.Message);
        Assert.Equal(
            localizer.T(LocalizationKeys.ToastInfo)
                + string.Format(CultureInfo.CurrentCulture, localizer.T(LocalizationKeys.ToastMergeCountSuffix), 2),
            merged.Title);

        // 合并重置倒计时：在途等待被打断，随后按完整档位重开一轮。
        await WaitUntilAsync(() => delays.RequestCount == 2);
        Assert.Equal(1, delays.CancellationCount);
        Assert.Equal(ToastDurations.Resolve(ToastDuration.Brief), delays.RequestedDurations[1]);

        delays.ReleaseAll();
        await WaitUntilAsync(() => viewModel.ActiveToasts.Count == 0);
    }

    [Fact]
    public async Task ToastRaised_WithDistinctMergeKeys_AddsSeparateCards()
    {
        await using var provider = CreateProvider();
        var toastService = provider.GetRequiredService<ToastService>();
        var delays = new ControlledDelay();
        using var viewModel = new ToastHostViewModel(
            toastService,
            provider.GetRequiredService<LocalizationService>(),
            new LocalDiagnostics(),
            InvokeSerially,
            delays.WaitAsync);

        toastService.Show(new ToastOptions { Message = "alpha", Key = "key-a", Duration = ToastDuration.Brief });
        toastService.Show(new ToastOptions { Message = "beta", Key = "key-b", Duration = ToastDuration.Brief });
        await WaitUntilAsync(() => viewModel.ActiveToasts.Count == 2);

        Assert.All(viewModel.ActiveToasts, toast => Assert.Equal(1, toast.MergeCount));
        Assert.Equal(
            new[] { "beta", "alpha" },
            viewModel.ActiveToasts.Select(toast => toast.Message));

        delays.ReleaseAll();
        await WaitUntilAsync(() => viewModel.ActiveToasts.Count == 0);
    }

    [Fact]
    public async Task ToastRaised_WithSameMergeKey_OnCardWithActions_MergesWithoutAddingACardOrCountdown()
    {
        await using var provider = CreateProvider();
        var toastService = provider.GetRequiredService<ToastService>();
        var delayCalls = 0;
        using var viewModel = new ToastHostViewModel(
            toastService,
            provider.GetRequiredService<LocalizationService>(),
            new LocalDiagnostics(),
            InvokeSerially,
            (_, _) =>
            {
                delayCalls++;
                return Task.CompletedTask;
            });

        toastService.Show(new ToastOptions
        {
            Message = "first failure",
            Key = "task-failed",
            PrimaryAction = new ToastAction(
                "Retry",
                _ => Task.FromResult(ToastActionResult.Success()))
        });
        await WaitUntilAsync(() => viewModel.ActiveToasts.Count == 1);
        var first = viewModel.ActiveToasts.Single();

        toastService.Show(new ToastOptions { Message = "second failure", Key = "task-failed" });
        await WaitUntilAsync(() => first.MergeCount == 2);

        var merged = Assert.Single(viewModel.ActiveToasts);
        Assert.Same(first, merged);
        Assert.Equal("second failure", merged.Message);
        Assert.Equal(0, delayCalls);
    }

    [Fact]
    public async Task ToastStack_BeyondFourActiveToasts_EvictsTheEarliestWithoutActions()
    {
        await using var provider = CreateProvider();
        var toastService = provider.GetRequiredService<ToastService>();
        var delays = new ControlledDelay();
        using var viewModel = new ToastHostViewModel(
            toastService,
            provider.GetRequiredService<LocalizationService>(),
            new LocalDiagnostics(),
            InvokeSerially,
            delays.WaitAsync);

        for (var index = 1; index <= 5; index++)
        {
            toastService.Show(new ToastOptions { Message = $"m{index}", Duration = ToastDuration.Brief });
        }

        await WaitUntilAsync(() =>
            viewModel.ActiveToasts.Count == 4
            && viewModel.ActiveToasts.Any(toast => toast.Message == "m5"));

        Assert.DoesNotContain(viewModel.ActiveToasts, toast => toast.Message == "m1");
        Assert.Equal(
            new[] { "m5", "m4", "m3", "m2" },
            viewModel.ActiveToasts.Select(toast => toast.Message));
    }

    [Fact]
    public async Task ToastStack_WhenAllActiveToastsHaveActions_ExceedsTheCapWithoutEviction()
    {
        await using var provider = CreateProvider();
        var toastService = provider.GetRequiredService<ToastService>();
        var delayCalls = 0;
        using var viewModel = new ToastHostViewModel(
            toastService,
            provider.GetRequiredService<LocalizationService>(),
            new LocalDiagnostics(),
            InvokeSerially,
            (_, _) =>
            {
                delayCalls++;
                return Task.CompletedTask;
            });

        for (var index = 1; index <= 5; index++)
        {
            toastService.Show(new ToastOptions
            {
                Message = $"m{index}",
                PrimaryAction = new ToastAction(
                    "Retry",
                    _ => Task.FromResult(ToastActionResult.Success()))
            });
        }

        await WaitUntilAsync(() =>
            viewModel.ActiveToasts.Count == 5
            && viewModel.ActiveToasts.Any(toast => toast.Message == "m5"));

        // 带动作的通知不自动淘汰（ADR-070）：入口不能因为栈满而消失。
        Assert.Equal(5, viewModel.ActiveToasts.Count);
        Assert.All(viewModel.ActiveToasts, toast => Assert.True(toast.HasActions));
        Assert.Equal(0, delayCalls);
    }

    [Fact]
    public async Task ToastStack_BeyondTheCap_EvictsTheEarliestWithoutActions_SkippingActionToasts()
    {
        await using var provider = CreateProvider();
        var toastService = provider.GetRequiredService<ToastService>();
        var delays = new ControlledDelay();
        using var viewModel = new ToastHostViewModel(
            toastService,
            provider.GetRequiredService<LocalizationService>(),
            new LocalDiagnostics(),
            InvokeSerially,
            delays.WaitAsync);

        toastService.Show(new ToastOptions { Message = "a1", Duration = ToastDuration.Brief });
        toastService.Show(new ToastOptions
        {
            Message = "b1",
            PrimaryAction = new ToastAction(
                "Retry",
                _ => Task.FromResult(ToastActionResult.Success()))
        });
        toastService.Show(new ToastOptions { Message = "a2", Duration = ToastDuration.Brief });
        toastService.Show(new ToastOptions
        {
            Message = "b2",
            PrimaryAction = new ToastAction(
                "Retry",
                _ => Task.FromResult(ToastActionResult.Success()))
        });
        await WaitUntilAsync(() => viewModel.ActiveToasts.Count == 4);
        toastService.Show(new ToastOptions { Message = "fresh", Duration = ToastDuration.Brief });

        await WaitUntilAsync(() =>
            viewModel.ActiveToasts.Count == 4
            && viewModel.ActiveToasts.Any(toast => toast.Message == "fresh"));

        var messages = viewModel.ActiveToasts.Select(toast => toast.Message).ToArray();
        Assert.DoesNotContain("a1", messages);
        Assert.Contains("b1", messages);
        Assert.Contains("a2", messages);
        Assert.Contains("b2", messages);
        Assert.Equal(new[] { "fresh", "b2", "a2", "b1" }, messages);
    }

    [Fact]
    public async Task Dispose_WhileExitDelayIsPending_CancelsObservedCommandTask()
    {
        await using var provider = CreateProvider();
        var settings = provider.GetRequiredService<SettingsViewModel>();
        settings.Editor.ApplySnapshot(new LauncherSettings
        {
        });
        var toastService = provider.GetRequiredService<ToastService>();
        var displayDelay = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var exitDelayStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var viewModel = new ToastHostViewModel(
            toastService,
            provider.GetRequiredService<LocalizationService>(),
            new LocalDiagnostics(),
            InvokeSerially,
            async (delay, cancellationToken) =>
            {
                if (delay != AnimationTimings.ExitAnimationDuration)
                {
                    await displayDelay.Task.WaitAsync(cancellationToken);
                    return;
                }

                exitDelayStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            });
        viewModel.ApplyMotionPreference(reduceMotion: false);
        toastService.Show("dispose");
        await WaitUntilAsync(() => viewModel.ActiveToasts.Count == 1);
        var toast = viewModel.ActiveToasts[0];

        var dismissTask = viewModel.DismissToastCommand.ExecuteAsync(toast.Id);
        await exitDelayStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        viewModel.Dispose();

        await dismissTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(dismissTask.IsCompletedSuccessfully);
        Assert.Empty(viewModel.ActiveToasts);
    }

    [Fact]
    public async Task DismissToastCommand_WhenExitDelayHasUnrelatedCancellation_PropagatesCancellation()
    {
        await using var provider = CreateProvider();
        var settings = provider.GetRequiredService<SettingsViewModel>();
        settings.Editor.ApplySnapshot(new LauncherSettings
        {
        });
        var toastService = provider.GetRequiredService<ToastService>();
        var displayDelay = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var unrelatedCts = new CancellationTokenSource();
        unrelatedCts.Cancel();
        using var viewModel = new ToastHostViewModel(
            toastService,
            provider.GetRequiredService<LocalizationService>(),
            new LocalDiagnostics(),
            InvokeSerially,
            (delay, cancellationToken) =>
                delay == AnimationTimings.ExitAnimationDuration
                    ? Task.FromCanceled(unrelatedCts.Token)
                    : displayDelay.Task.WaitAsync(cancellationToken));
        viewModel.ApplyMotionPreference(reduceMotion: false);
        toastService.Show("unrelated-cancellation");
        await WaitUntilAsync(() => viewModel.ActiveToasts.Count == 1);
        var toast = viewModel.ActiveToasts[0];

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => viewModel.DismissToastCommand.ExecuteAsync(toast.Id));

        Assert.True(toast.IsExiting);
        Assert.Contains(toast, viewModel.ActiveToasts);
        displayDelay.TrySetResult();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DismissToastCommand_WhenNonLifetimeCancellationRacesWithDispose_PropagatesCancellation(
        bool useDefaultCancellationToken)
    {
        await using var provider = CreateProvider();
        var settings = provider.GetRequiredService<SettingsViewModel>();
        settings.Editor.ApplySnapshot(new LauncherSettings
        {
        });
        var toastService = provider.GetRequiredService<ToastService>();
        var displayDelay = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var exitDelayStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseUnrelatedCancellation = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var unrelatedCts = new CancellationTokenSource();
        unrelatedCts.Cancel();
        var unrelatedCancellationToken = useDefaultCancellationToken
            ? CancellationToken.None
            : unrelatedCts.Token;
        using var viewModel = new ToastHostViewModel(
            toastService,
            provider.GetRequiredService<LocalizationService>(),
            new LocalDiagnostics(),
            InvokeSerially,
            async (delay, cancellationToken) =>
            {
                if (delay != AnimationTimings.ExitAnimationDuration)
                {
                    await displayDelay.Task.WaitAsync(cancellationToken);
                    return;
                }

                exitDelayStarted.TrySetResult();
                await releaseUnrelatedCancellation.Task;
                throw new OperationCanceledException(unrelatedCancellationToken);
            });
        viewModel.ApplyMotionPreference(reduceMotion: false);
        toastService.Show("racing-cancellation");
        await WaitUntilAsync(() => viewModel.ActiveToasts.Count == 1);
        var toast = viewModel.ActiveToasts[0];

        var dismissTask = viewModel.DismissToastCommand.ExecuteAsync(toast.Id);
        await exitDelayStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        viewModel.Dispose();
        releaseUnrelatedCancellation.TrySetResult();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => dismissTask);

        Assert.Equal(unrelatedCancellationToken, exception.CancellationToken);
        Assert.True(toast.IsExiting);
        Assert.Contains(toast, viewModel.ActiveToasts);
    }

    [Fact]
    public async Task ToastPointerOver_WhileDisplayCountdownIsPending_SuspendsItUntilThePointerLeaves()
    {
        await using var provider = CreateProvider();
        var toastService = provider.GetRequiredService<ToastService>();
        var delays = new ControlledDelay();
        using var viewModel = new ToastHostViewModel(
            toastService,
            provider.GetRequiredService<LocalizationService>(),
            new LocalDiagnostics(),
            InvokeSerially,
            delays.WaitAsync);

        toastService.Show(new ToastOptions { Message = "saved", Duration = ToastDuration.Brief });
        await WaitUntilAsync(() => viewModel.ActiveToasts.Count == 1);
        var toast = viewModel.ActiveToasts.Single();

        viewModel.SetToastInteractionActive(toast.Id, isActive: true);
        await WaitUntilAsync(() => delays.CancellationCount == 1);

        Assert.True(viewModel.IsCountdownSuspended(toast.Id));
        Assert.Equal(1, delays.RequestCount);
        Assert.Contains(toast, viewModel.ActiveToasts);

        viewModel.SetToastInteractionActive(toast.Id, isActive: false);
        await WaitUntilAsync(() => delays.RequestCount == 2);

        Assert.False(viewModel.IsCountdownSuspended(toast.Id));
        Assert.Equal(ToastDurations.Resolve(ToastDuration.Brief), delays.RequestedDurations[1]);
        Assert.Contains(toast, viewModel.ActiveToasts);

        delays.ReleaseAll();
        await WaitUntilAsync(() => viewModel.ActiveToasts.Count == 0);
    }

    [Fact]
    public async Task ToastPointerOver_AfterEachPointerLeave_RestartsTheFullDisplayDuration()
    {
        await using var provider = CreateProvider();
        var toastService = provider.GetRequiredService<ToastService>();
        var delays = new ControlledDelay();
        using var viewModel = new ToastHostViewModel(
            toastService,
            provider.GetRequiredService<LocalizationService>(),
            new LocalDiagnostics(),
            InvokeSerially,
            delays.WaitAsync);

        toastService.Show(new ToastOptions { Message = "saved", Duration = ToastDuration.Brief });
        await WaitUntilAsync(() => viewModel.ActiveToasts.Count == 1);
        var toast = viewModel.ActiveToasts.Single();

        for (var visit = 0; visit < 2; visit++)
        {
            viewModel.SetToastInteractionActive(toast.Id, isActive: true);
            await WaitUntilAsync(() => delays.CancellationCount == visit + 1);
            viewModel.SetToastInteractionActive(toast.Id, isActive: false);
            await WaitUntilAsync(() => delays.RequestCount == visit + 2);
        }

        Assert.Equal(
            new[]
            {
                ToastDurations.Resolve(ToastDuration.Brief),
                ToastDurations.Resolve(ToastDuration.Brief),
                ToastDurations.Resolve(ToastDuration.Brief)
            },
            delays.RequestedDurations);

        delays.ReleaseAll();
        await WaitUntilAsync(() => viewModel.ActiveToasts.Count == 0);
    }

    [Fact]
    public async Task DismissToast_WhilePointerRestsOnIt_EndsTheSuspendedCountdown()
    {
        await using var provider = CreateProvider();
        var toastService = provider.GetRequiredService<ToastService>();
        var delays = new ControlledDelay();
        using var viewModel = new ToastHostViewModel(
            toastService,
            provider.GetRequiredService<LocalizationService>(),
            new LocalDiagnostics(),
            InvokeSerially,
            delays.WaitAsync);

        toastService.Show(new ToastOptions { Message = "saved", Duration = ToastDuration.Brief });
        await WaitUntilAsync(() => viewModel.ActiveToasts.Count == 1);
        var toast = viewModel.ActiveToasts.Single();
        viewModel.SetToastInteractionActive(toast.Id, isActive: true);
        await WaitUntilAsync(() => delays.CancellationCount == 1);

        await viewModel.DismissToastCommand.ExecuteAsync(toast.Id);

        Assert.Empty(viewModel.ActiveToasts);
        Assert.False(viewModel.IsCountdownSuspended(toast.Id));

        // 负向断言给出可观察窗口：消失的 Toast 的计数不会因指针移开而被重新计一次。
        viewModel.SetToastInteractionActive(toast.Id, isActive: false);
        await TestWait.HoldsForAsync(
            TimeSpan.FromMilliseconds(250),
            () => delays.RequestCount == 1,
            "The disappeared toast's countdown was re-counted");
    }

    [Fact]
    public async Task DismissToast_WhileCountdownIsParkedOnThePointer_WakesTheParkedWait()
    {
        await using var provider = CreateProvider();
        var toastService = provider.GetRequiredService<ToastService>();
        var delays = new ControlledDelay();
        using var viewModel = new ToastHostViewModel(
            toastService,
            provider.GetRequiredService<LocalizationService>(),
            new LocalDiagnostics(),
            InvokeSerially,
            delays.WaitAsync);

        toastService.Show(new ToastOptions { Message = "saved", Duration = ToastDuration.Brief });
        await WaitUntilAsync(() => viewModel.ActiveToasts.Count == 1);
        var toast = viewModel.ActiveToasts.Single();
        viewModel.SetToastInteractionActive(toast.Id, isActive: true);
        // 先确认倒计时已经真的停在「等指针移开」上，再拆除：否则拆除落在重新评估之前，
        // 唤醒与「记录已移除」两条路都到得了终点，守卫咬不住缺失的唤醒。
        await WaitUntilAsync(() => viewModel.IsCountdownAwaitingResume(toast.Id));
        var parked = viewModel.PendingCountdownTask(toast.Id);

        Assert.NotNull(parked);

        await viewModel.DismissToastCommand.ExecuteAsync(toast.Id);

        // 拆除必须唤醒挂起的等待；否则这个任务会一直阻塞到宿主 Dispose 才结束，
        // 而那发生在本断言之后——超时即红（AUD-TEST-012）。
        await parked!.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Empty(viewModel.ActiveToasts);
    }

    [Fact]
    public async Task DismissToast_WhileDisplayDelayIsInFlight_CancelsTheInFlightWait()
    {
        await using var provider = CreateProvider();
        var toastService = provider.GetRequiredService<ToastService>();
        var delays = new ControlledDelay();
        using var viewModel = new ToastHostViewModel(
            toastService,
            provider.GetRequiredService<LocalizationService>(),
            new LocalDiagnostics(),
            InvokeSerially,
            delays.WaitAsync);

        toastService.Show(new ToastOptions { Message = "saved", Duration = ToastDuration.Brief });
        await WaitUntilAsync(() => viewModel.ActiveToasts.Count == 1);
        var toast = viewModel.ActiveToasts.Single();
        await WaitUntilAsync(() => delays.RequestCount == 1);

        await viewModel.DismissToastCommand.ExecuteAsync(toast.Id);

        // 拆除要打断在途的显示等待；不打断的话这条等待会一直挂到宿主 Dispose（AUD-TEST-012）。
        await WaitUntilAsync(() => delays.CancellationCount == 1);
        Assert.Equal(1, delays.RequestCount);
    }

    [Fact]
    public async Task ToastPointerOver_OnToastWithoutDisplayCountdown_IsIgnored()
    {
        await using var provider = CreateProvider();
        var toastService = provider.GetRequiredService<ToastService>();
        var delayCalls = 0;
        using var viewModel = new ToastHostViewModel(
            toastService,
            provider.GetRequiredService<LocalizationService>(),
            new LocalDiagnostics(),
            InvokeSerially,
            (_, _) =>
            {
                delayCalls++;
                return Task.CompletedTask;
            });

        toastService.Show(CreateActionOptions());
        await WaitUntilAsync(() => viewModel.ActiveToasts.Count == 1);
        var toast = viewModel.ActiveToasts.Single();

        viewModel.SetToastInteractionActive(toast.Id, isActive: true);
        viewModel.SetToastInteractionActive("missing-toast", isActive: true);

        Assert.False(viewModel.IsCountdownSuspended(toast.Id));
        Assert.Equal(0, delayCalls);
        Assert.Contains(toast, viewModel.ActiveToasts);
    }

    [Fact]
    public async Task Dispose_UnsubscribesFromToastService()
    {
        await using var provider = CreateProvider();
        var settings = provider.GetRequiredService<SettingsViewModel>();
        var toastService = provider.GetRequiredService<ToastService>();
        var delays = new ControlledDelay();
        using var viewModel = new ToastHostViewModel(
            toastService,
            provider.GetRequiredService<LocalizationService>(),
            new LocalDiagnostics(),
            InvokeSerially,
            delays.WaitAsync);
        viewModel.Dispose();

        toastService.Show("after-dispose");
        // 负向断言给出可观察窗口：若仍订阅，Show 会同步登记 duration delay（RequestCount=1）
        // 并进入 ActiveToasts，观察窗内即时失败；持续 250ms 无出现即证明已退订。
        await TestWait.HoldsForAsync(
            TimeSpan.FromMilliseconds(250),
            () => viewModel.ActiveToasts.Count == 0 && delays.RequestCount == 0,
            "A disposed toast host still registered the shown toast");

        Assert.Empty(viewModel.ActiveToasts);
        Assert.Equal(0, delays.RequestCount);
    }

    [Fact]
    public async Task Notifications_ResolveThroughTheNarrowInterfaceToTheSameInstance()
    {
        await using var provider = CreateProvider();

        var narrow = provider.GetRequiredService<INotificationService>();

        Assert.Same(provider.GetRequiredService<ToastService>(), narrow);
    }

    private ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddLauncherServices();
        services.AddSingleton(_ => new UnifiedLogger(Path.Combine(tempDir, "logs")));
        return services.BuildServiceProvider();
    }

    private static ToastOptions CreateActionOptions(
        Func<CancellationToken, Task<ToastActionResult>>? primary = null,
        Func<CancellationToken, Task<ToastActionResult>>? secondary = null,
        ToastDuration duration = ToastDuration.Brief) =>
        new()
        {
            Title = "Action",
            Message = "Choose",
            Duration = duration,
            PrimaryAction = new ToastAction(
                "Primary",
                primary ?? (_ => Task.FromResult(ToastActionResult.Success()))),
            SecondaryAction = new ToastAction(
                "Secondary",
                secondary ?? (_ => Task.FromResult(ToastActionResult.Success())))
        };

    private Task InvokeSerially(Action action)
    {
        lock (invokeGate)
        {
            action();
        }

        return Task.CompletedTask;
    }

    /// <summary>等提示条状态落定；机制与超时语义在 <see cref="TestWait"/>。</summary>
    private static Task WaitUntilAsync(Func<bool> condition) =>
        TestWait.UntilAsync(condition, TimeSpan.FromSeconds(2), "Toast state did not settle.");

    private sealed class ControlledDelay
    {
        private readonly ConcurrentQueue<TaskCompletionSource> requests = new();
        private readonly ConcurrentQueue<TimeSpan> requestedDurations = new();
        private readonly SemaphoreSlim requestAvailable = new(0);
        private int requestCount;
        private int cancellationCount;

        public int RequestCount => Volatile.Read(ref requestCount);

        /// <summary>Gets the durations requested so far, in request order.</summary>
        public IReadOnlyList<TimeSpan> RequestedDurations => requestedDurations.ToArray();

        /// <summary>Gets how many requests were cancelled while still pending.</summary>
        public int CancellationCount => Volatile.Read(ref cancellationCount);

        public async Task WaitAsync(TimeSpan duration, CancellationToken cancellationToken)
        {
            requestedDurations.Enqueue(duration);
            var request = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            requests.Enqueue(request);
            Interlocked.Increment(ref requestCount);
            requestAvailable.Release();
            try
            {
                await request.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Interlocked.Increment(ref cancellationCount);
                throw;
            }
        }

        public async Task ReleaseNextAsync()
        {
            await requestAvailable.WaitAsync();
            if (!requests.TryDequeue(out var request))
            {
                throw new InvalidOperationException("A recorded delay request could not be dequeued.");
            }

            request.TrySetResult();
        }

        /// <summary>Completes every request recorded so far, including ones already cancelled.</summary>
        public void ReleaseAll()
        {
            while (requests.TryDequeue(out var request))
            {
                request.TrySetResult();
            }
        }
    }

    public void Dispose()
    {
        tempDir.Dispose();
    }
}
