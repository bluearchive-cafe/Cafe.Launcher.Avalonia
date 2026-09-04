using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Testing;

namespace Cafe.Launcher.Avalonia.Tests;

public partial class MainWindowViewModelTests
{
    [Fact]
    public async Task InitializeAsync_WithReducedMotion_AppliesMotionPreference()
    {
        var snapshot = CreateSnapshot();
        snapshot.Settings.MotionMode = MotionModes.Reduced;
        using var viewModel = await CreateViewModelAsync(
            new CountingCoreService(snapshot),
            windowsAnimationSettingsProvider: new WindowsAnimationSettingsProvider(() => (true, true)));

        await viewModel.InitializeAsync();

        Assert.True(viewModel.IsMotionReduced);
        Assert.False(viewModel.IsMotionEnabled);
    }

    [Fact]
    public async Task SaveSettingsAsync_WithFullMotion_AppliesMotionPreferenceImmediately()
    {
        var snapshot = CreateSnapshot();
        snapshot.Settings.MotionMode = MotionModes.Reduced;
        using var viewModel = await CreateViewModelAsync(
            new CountingCoreService(snapshot),
            gameOperationsBackend: new StubGameOperationExecutor { IsDownloadRunning = true },
            windowsAnimationSettingsProvider: new WindowsAnimationSettingsProvider(() => (true, false)));
        await viewModel.InitializeAsync();
        viewModel.Settings.Editor.Current.MotionMode = MotionModes.Full;

        await SaveSettingsAsync(viewModel);

        Assert.False(viewModel.IsMotionReduced);
        Assert.True(viewModel.IsMotionEnabled);
    }

    [Fact]
    public async Task InitializeAsync_WithReducedMotionAndBanners_DoesNotStartBannerCarousel()
    {
        var snapshot = CreateSnapshot();
        snapshot.Settings.MotionMode = MotionModes.Reduced;
        snapshot.Remote.OperationsResource = new OperationsResourceResponse
        {
            OperationsResourceOpen = true,
            BannerLoop = true,
            OperationsBannerList = [new(), new()]
        };
        using var viewModel = await CreateViewModelAsync(
            new CountingCoreService(snapshot),
            windowsAnimationSettingsProvider: new WindowsAnimationSettingsProvider(() => (true, true)));

        await viewModel.InitializeAsync();

        Assert.True(viewModel.IsMotionReduced);
        Assert.False(viewModel.RemoteContent.IsCarouselTimerRunning);
    }

    [Fact]
    public async Task SaveSettingsAsync_WithReducedMotionAndBanners_StopsBannerCarouselImmediately()
    {
        var snapshot = CreateSnapshot();
        snapshot.Settings.MotionMode = MotionModes.Full;
        snapshot.Remote.OperationsResource = new OperationsResourceResponse
        {
            OperationsResourceOpen = true,
            BannerLoop = true,
            OperationsBannerList = [new(), new()]
        };
        using var viewModel = await CreateViewModelAsync(
            new CountingCoreService(snapshot),
            gameOperationsBackend: new StubGameOperationExecutor { IsDownloadRunning = true },
            windowsAnimationSettingsProvider: new WindowsAnimationSettingsProvider(() => (true, true)));
        await viewModel.InitializeAsync();
        Assert.True(viewModel.RemoteContent.IsCarouselTimerRunning);

        viewModel.Settings.Editor.Current.MotionMode = MotionModes.Reduced;
        await SaveSettingsAsync(viewModel);

        Assert.True(viewModel.IsMotionReduced);
        Assert.False(viewModel.RemoteContent.IsCarouselTimerRunning);
    }

    [Fact]
    public async Task RefreshSystemMotionPreference_SystemMode_ReevaluatesEffectiveMotion()
    {
        var animationsEnabled = true;
        var snapshot = CreateSnapshot();
        snapshot.Settings.MotionMode = MotionModes.System;
        using var viewModel = await CreateViewModelAsync(
            new CountingCoreService(snapshot),
            windowsAnimationSettingsProvider: new WindowsAnimationSettingsProvider(
                () => (true, animationsEnabled)));
        await viewModel.InitializeAsync();
        Assert.False(viewModel.IsMotionReduced);
        animationsEnabled = false;

        viewModel.RefreshSystemMotionPreference();

        Assert.True(viewModel.IsMotionReduced);
    }

    [Fact]
    public async Task RefreshSystemMotionPreference_UnchangedSystemValue_RetainsChildStateAndReadsProvider()
    {
        var providerReadCount = 0;
        var snapshot = CreateSnapshot();
        snapshot.Settings.MotionMode = MotionModes.System;
        using var viewModel = await CreateViewModelAsync(
            new CountingCoreService(snapshot),
            windowsAnimationSettingsProvider: new WindowsAnimationSettingsProvider(
                () =>
                {
                    providerReadCount++;
                    return (true, true);
                }));
        await viewModel.InitializeAsync();
        var carouselTransition = viewModel.RemoteContent.CarouselTransition;
        var readsBeforeRefresh = providerReadCount;

        viewModel.RefreshSystemMotionPreference();

        Assert.Same(carouselTransition, viewModel.RemoteContent.CarouselTransition);
        Assert.Equal(readsBeforeRefresh + 1, providerReadCount);
    }

    [Theory]
    [InlineData(MotionModes.Full)]
    [InlineData(MotionModes.Reduced)]
    public async Task RefreshSystemMotionPreference_ExplicitMode_NeverReadsProvider(string motionMode)
    {
        var providerReadCount = 0;
        var snapshot = CreateSnapshot();
        snapshot.Settings.MotionMode = motionMode;
        using var settingsService = new LauncherSettingsService(
            Path.Combine(tempDir, Guid.NewGuid().ToString("N"), "settings.json"));
        await settingsService.SaveAsync(snapshot.Settings);
        using var viewModel = await CreateViewModelAsync(
            new CountingCoreService(snapshot),
            settingsService: settingsService,
            windowsAnimationSettingsProvider: new WindowsAnimationSettingsProvider(
                () =>
                {
                    providerReadCount++;
                    return (true, false);
                }));

        await viewModel.InitializeAsync();
        viewModel.RefreshSystemMotionPreference();

        Assert.Equal(0, providerReadCount);
    }

    [Fact]
    public async Task RefreshSystemMotionPreference_BeforeSettingsSnapshotInitialized_DoesNotReadProvider()
    {
        var providerReadCount = 0;
        var snapshot = CreateSnapshot();
        snapshot.Settings.MotionMode = MotionModes.Full;
        using var settingsService = new LauncherSettingsService(
            Path.Combine(tempDir, Guid.NewGuid().ToString("N"), "settings.json"));
        await settingsService.SaveAsync(snapshot.Settings);
        using var viewModel = await CreateViewModelAsync(
            new CountingCoreService(snapshot),
            settingsService: settingsService,
            windowsAnimationSettingsProvider: new WindowsAnimationSettingsProvider(
                () =>
                {
                    providerReadCount++;
                    return (true, true);
                }));

        viewModel.RefreshSystemMotionPreference();

        Assert.Equal(0, providerReadCount);
        Assert.True(viewModel.IsMotionReduced);

        await viewModel.InitializeAsync();

        Assert.Equal(0, providerReadCount);
        Assert.False(viewModel.IsMotionReduced);
    }

    [Fact]
    public async Task RefreshSystemMotionPreference_CoreLoadFails_UsesPersistedSystemSnapshot()
    {
        var animationsEnabled = true;
        var providerReadCount = 0;
        var persistedSettings = new LauncherSettings
        {
            GamePath = "persisted-game-path",
            MotionMode = MotionModes.System
        };
        using var settingsService = new LauncherSettingsService(
            Path.Combine(tempDir, Guid.NewGuid().ToString("N"), "settings.json"));
        await settingsService.SaveAsync(persistedSettings);
        using var viewModel = await CreateViewModelAsync(
            new ThrowingCoreService(),
            settingsService: settingsService,
            windowsAnimationSettingsProvider: new WindowsAnimationSettingsProvider(
                () =>
                {
                    providerReadCount++;
                    return (true, animationsEnabled);
                }));

        await viewModel.InitializeAsync();
        Assert.Equal(
            persistedSettings.GamePath,
            viewModel.Settings.Editor.GetSavedSnapshot().GamePath);
        Assert.False(viewModel.IsMotionReduced);
        var readsBeforeRefresh = providerReadCount;
        animationsEnabled = false;

        viewModel.RefreshSystemMotionPreference();

        Assert.Equal(readsBeforeRefresh + 1, providerReadCount);
        Assert.True(viewModel.IsMotionReduced);
    }

    [Fact]
    public async Task InitializeAsync_InitialReducedValue_SynchronizesChildMotionPreferences()
    {
        var snapshot = CreateSnapshot();
        snapshot.Settings.MotionMode = MotionModes.Reduced;
        using var settingsService = new LauncherSettingsService(
            Path.Combine(tempDir, Guid.NewGuid().ToString("N"), "settings.json"));
        await settingsService.SaveAsync(snapshot.Settings);
        var toastService = new ToastService();
        var displayDelay = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var exitDelayCalls = 0;
        using var viewModel = await CreateViewModelAsync(
            new CountingCoreService(snapshot),
            settingsService: settingsService,
            toastService: toastService,
            toastDelayAsync: (delay, cancellationToken) =>
            {
                if (delay == AnimationTimings.ExitAnimationDuration)
                {
                    Interlocked.Increment(ref exitDelayCalls);
                    return Task.CompletedTask;
                }

                return displayDelay.Task.WaitAsync(cancellationToken);
            });
        viewModel.RemoteContent.ApplyMotionPreference(reduceMotion: false);
        viewModel.Toasts.ApplyMotionPreference(reduceMotion: false);

        await viewModel.InitializeAsync();
        toastService.Show("reduced");
        var toast = Assert.Single(viewModel.Toasts.ActiveToasts);
        await viewModel.Toasts.DismissToastCommand.ExecuteAsync(toast.Id);

        Assert.True(viewModel.IsMotionReduced);
        Assert.Equal(
            TimeSpan.Zero,
            Assert.IsType<global::Cafe.Launcher.Avalonia.Helpers.BannerCarouselTransition>(
                viewModel.RemoteContent.CarouselTransition).Duration);
        Assert.Empty(viewModel.Toasts.ActiveToasts);
        Assert.False(toast.IsExiting);
        Assert.Equal(0, exitDelayCalls);
        displayDelay.TrySetResult();
    }
}
