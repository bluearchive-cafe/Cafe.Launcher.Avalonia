using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class ToastHostViewModelTests : IDisposable
{
    private readonly string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    static ToastHostViewModelTests()
    {
        TestLocalizationHelper.Initialize();
    }

    [Fact]
    public async Task ToastRaised_WhenNotificationsAreEnabled_AddsThenExpiresToast()
    {
        await using var provider = CreateProvider();
        var settings = provider.GetRequiredService<SettingsViewModel>();
        settings.Editor.ApplySnapshot(new LauncherSettings
        {
            ToastNotificationsEnabled = true
        });
        var toastService = provider.GetRequiredService<ToastService>();
        var delay = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var viewModel = new ToastHostViewModel(
            toastService,
            provider.GetRequiredService<LocalizationService>(),
            settings,
            InvokeImmediately,
            (_, cancellationToken) => delay.Task.WaitAsync(cancellationToken));

        toastService.ShowSuccess("saved");
        await WaitUntilAsync(() => viewModel.ActiveToasts.Count == 1);

        Assert.Equal(ToastSeverity.Success, viewModel.ActiveToasts[0].Severity);
        Assert.NotEmpty(viewModel.ActiveToasts[0].SeverityLabel);

        delay.TrySetResult();
        await WaitUntilAsync(() => viewModel.ActiveToasts.Count == 0);
    }

    [Fact]
    public async Task ToastRaised_WhenNotificationsAreDisabled_DoesNotAddToast()
    {
        await using var provider = CreateProvider();
        var settings = provider.GetRequiredService<SettingsViewModel>();
        settings.Editor.ApplySnapshot(new LauncherSettings
        {
            ToastNotificationsEnabled = false
        });
        var toastService = provider.GetRequiredService<ToastService>();
        using var viewModel = new ToastHostViewModel(
            toastService,
            provider.GetRequiredService<LocalizationService>(),
            settings,
            InvokeImmediately,
            static (_, _) => Task.CompletedTask);

        toastService.Show("hidden");
        await Task.Delay(20);

        Assert.Empty(viewModel.ActiveToasts);
    }

    [Fact]
    public async Task DismissToastCommand_WithFullMotion_MarksExitingBeforeRemovingToast()
    {
        await using var provider = CreateProvider();
        var settings = provider.GetRequiredService<SettingsViewModel>();
        settings.Editor.ApplySnapshot(new LauncherSettings
        {
            ToastNotificationsEnabled = true
        });
        var toastService = provider.GetRequiredService<ToastService>();
        var displayDelay = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var exitDelay = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var viewModel = new ToastHostViewModel(
            toastService,
            provider.GetRequiredService<LocalizationService>(),
            settings,
            InvokeImmediately,
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
            ToastNotificationsEnabled = true
        });
        var toastService = provider.GetRequiredService<ToastService>();
        var displayDelay = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var exitDelayCalls = 0;
        using var viewModel = new ToastHostViewModel(
            toastService,
            provider.GetRequiredService<LocalizationService>(),
            settings,
            InvokeImmediately,
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
            ToastNotificationsEnabled = true
        });
        var toastService = provider.GetRequiredService<ToastService>();
        var displayDelay = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var exitDelay = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var viewModel = new ToastHostViewModel(
            toastService,
            provider.GetRequiredService<LocalizationService>(),
            settings,
            InvokeImmediately,
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
            ToastNotificationsEnabled = true
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
            settings,
            InvokeImmediately,
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
        toastService.Show("overlap");
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
        var dismissTask = viewModel.DismissToastCommand.ExecuteAsync(toast.Id);
        await WaitUntilAsync(() => toast.IsExiting);

        Assert.Equal(1, exitDelayCalls);

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
            ToastNotificationsEnabled = true
        });
        var toastService = provider.GetRequiredService<ToastService>();
        var displayDelay = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var exitDelay = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var viewModel = new ToastHostViewModel(
            toastService,
            provider.GetRequiredService<LocalizationService>(),
            settings,
            InvokeImmediately,
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
    public async Task Dispose_WhileExitDelayIsPending_CancelsObservedCommandTask()
    {
        await using var provider = CreateProvider();
        var settings = provider.GetRequiredService<SettingsViewModel>();
        settings.Editor.ApplySnapshot(new LauncherSettings
        {
            ToastNotificationsEnabled = true
        });
        var toastService = provider.GetRequiredService<ToastService>();
        var displayDelay = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var exitDelayStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var viewModel = new ToastHostViewModel(
            toastService,
            provider.GetRequiredService<LocalizationService>(),
            settings,
            InvokeImmediately,
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
        Assert.Contains(toast, viewModel.ActiveToasts);
    }

    [Fact]
    public async Task DismissToastCommand_WhenExitDelayHasUnrelatedCancellation_PropagatesCancellation()
    {
        await using var provider = CreateProvider();
        var settings = provider.GetRequiredService<SettingsViewModel>();
        settings.Editor.ApplySnapshot(new LauncherSettings
        {
            ToastNotificationsEnabled = true
        });
        var toastService = provider.GetRequiredService<ToastService>();
        var displayDelay = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var unrelatedCts = new CancellationTokenSource();
        unrelatedCts.Cancel();
        using var viewModel = new ToastHostViewModel(
            toastService,
            provider.GetRequiredService<LocalizationService>(),
            settings,
            InvokeImmediately,
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
            ToastNotificationsEnabled = true
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
            settings,
            InvokeImmediately,
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
        var viewModel = new ToastHostViewModel(
            toastService,
            provider.GetRequiredService<LocalizationService>(),
            settings,
            InvokeImmediately,
            static (_, _) => Task.CompletedTask);
        viewModel.Dispose();

        toastService.Show("after-dispose");
        await Task.Delay(20);

        Assert.Empty(viewModel.ActiveToasts);
    }

    private ServiceProvider CreateProvider()
    {
        Directory.CreateDirectory(tempDir);
        var services = new ServiceCollection();
        services.AddLauncherServices();
        services.AddSingleton(_ => new UnifiedLogger(Path.Combine(tempDir, "logs")));
        return services.BuildServiceProvider();
    }

    private static Task InvokeImmediately(Action action)
    {
        action();
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

    public void Dispose()
    {
        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
