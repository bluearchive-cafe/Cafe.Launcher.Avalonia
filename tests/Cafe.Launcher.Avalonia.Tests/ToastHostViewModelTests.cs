using System.Collections.Concurrent;
using Cafe.Launcher.Avalonia.Composition;
using Cafe.Launcher.Avalonia.Features.Settings;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Cafe.Launcher.Avalonia.Tests;

[Collection(nameof(LocalizationServiceTestIsolation))]
public sealed class ToastHostViewModelTests : IDisposable
{
    private readonly object invokeGate = new();
    private readonly string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    static ToastHostViewModelTests()
    {
        TestLocalizationHelper.Initialize();
    }

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

        toastService.Show(CreateActionOptions(durationMs: 0));
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
            DurationMs = 100
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

        toastService.Show(new ToastOptions { Message = "saved", DurationMs = 100 });
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
        }, durationMs: 0));
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
            ToastActionResult.Failure("Still offline", "Retry failed")), durationMs: 0));
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
            secondary: _ => throw new InvalidOperationException("secret detail"), durationMs: 0));
        await WaitUntilAsync(() => viewModel.ActiveToasts.Count == 1);
        var toast = viewModel.ActiveToasts[0];

        await viewModel.ExecuteSecondaryToastActionCommand.ExecuteAsync(toast.Id);

        Assert.Equal(localizer.T("toastActionFailedTitle"), toast.Title);
        Assert.Equal(localizer.T("toastActionFailedMessage"), toast.Message);
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
        await dismissTask;

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
        toastService.Show("overlap", durationMs: 1234);
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
        await dismissTask;
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
        await exitDelayStarted.Task;
        viewModel.Dispose();

        await dismissTask;

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
        await exitDelayStarted.Task;
        viewModel.Dispose();
        releaseUnrelatedCancellation.TrySetResult();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => dismissTask);

        Assert.Equal(unrelatedCancellationToken, exception.CancellationToken);
        Assert.True(toast.IsExiting);
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
        // 并进入 ActiveToasts，轮询中即时失败；持续 250ms 无出现即证明已退订。
        var observationEnd = DateTime.UtcNow.AddMilliseconds(250);
        while (DateTime.UtcNow < observationEnd)
        {
            Assert.Empty(viewModel.ActiveToasts);
            Assert.Equal(0, delays.RequestCount);
            await Task.Delay(10);
        }

        Assert.Empty(viewModel.ActiveToasts);
        Assert.Equal(0, delays.RequestCount);
    }

    private ServiceProvider CreateProvider()
    {
        Directory.CreateDirectory(tempDir);
        var services = new ServiceCollection();
        services.AddLauncherServices();
        services.AddSingleton(_ => new UnifiedLogger(Path.Combine(tempDir, "logs")));
        return services.BuildServiceProvider();
    }

    private static ToastOptions CreateActionOptions(
        Func<CancellationToken, Task<ToastActionResult>>? primary = null,
        Func<CancellationToken, Task<ToastActionResult>>? secondary = null,
        int durationMs = 4000) =>
        new()
        {
            Title = "Action",
            Message = "Choose",
            DurationMs = durationMs,
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

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeout = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (!condition())
        {
            if (DateTime.UtcNow >= timeout)
            {
                throw new TimeoutException("Condition was not reached.");
            }

            await Task.Delay(10);
        }
    }

    private sealed class ControlledDelay
    {
        private readonly ConcurrentQueue<TaskCompletionSource> requests = new();
        private readonly SemaphoreSlim requestAvailable = new(0);
        private int requestCount;

        public int RequestCount => Volatile.Read(ref requestCount);

        public Task WaitAsync(TimeSpan _, CancellationToken cancellationToken)
        {
            var request = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            requests.Enqueue(request);
            Interlocked.Increment(ref requestCount);
            requestAvailable.Release();
            return request.Task.WaitAsync(cancellationToken);
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
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
