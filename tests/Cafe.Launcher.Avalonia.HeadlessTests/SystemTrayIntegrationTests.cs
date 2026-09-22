using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Features.GameOperations;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Testing;
using Cafe.Launcher.Avalonia.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Cafe.Launcher.Avalonia.HeadlessTests;

public sealed class SystemTrayIntegrationTests
{
    [AvaloniaFact]
    public void Menu_OperationsAndModalsChange_UpdatesAvailabilityAndRechecksClicks()
    {
        using var context = new TrayContext();
        Assert.False(context.Platform.Text.CanStartGame);
        context.Ready();
        Assert.True(context.Platform.Text.CanStartGame);

        context.ViewModel.Shell.IsBusy = true;
        Assert.False(context.Platform.Text.CanStartGame);
        context.Platform.StartGame?.Invoke();
        Assert.Equal(0, context.Executor.LaunchCallCount);
        context.ViewModel.Shell.IsBusy = false;
        Assert.True(context.Platform.Text.CanStartGame);

        context.Executor.IsDownloadRunning = true;
        Dispatcher.UIThread.RunJobs();
        Assert.False(context.Platform.Text.CanStartGame);
        context.Executor.IsDownloadRunning = false;
        Dispatcher.UIThread.RunJobs();
        context.Ready();
        Assert.True(context.Platform.Text.CanStartGame);

        context.ViewModel.ModalHost.Open(ModalKind.SetupWizard, context.ViewModel.Settings);
        Assert.False(context.Platform.Text.CanStartGame);
        Assert.False(context.Platform.Text.CanOpenSettings);
        context.Platform.OpenSettings?.Invoke();
        Assert.False(context.ViewModel.WindowChrome.IsSettingsVisible);
        context.ViewModel.ModalHost.Close(ModalKind.SetupWizard);
        Assert.True(context.Platform.Text.CanStartGame);
        Assert.True(context.Platform.Text.CanOpenSettings);
    }

    [AvaloniaFact]
    public void Settings_ClickedTwice_RestoresWindowAndKeepsDraftOpen()
    {
        using var context = new TrayContext();
        context.Ready();
        context.Window.Show();
        context.Tray.HideWindow();
        context.Platform.OpenSettings?.Invoke();
        Assert.True(context.Window.IsVisible);
        Assert.True(context.ViewModel.WindowChrome.IsSettingsVisible);
        Assert.False(context.Platform.Text.CanStartGame);

        context.ViewModel.Settings.Editor.Current.GamePath = context.Directory.Sub("custom-game");
        string draftPath = context.ViewModel.Settings.Editor.Current.GamePath;
        context.Tray.HideWindow();
        context.Platform.OpenSettings?.Invoke();
        Assert.True(context.Window.IsVisible);
        Assert.True(context.ViewModel.WindowChrome.IsSettingsVisible);
        Assert.Equal(draftPath, context.ViewModel.Settings.Editor.Current.GamePath);
        Assert.False(context.ViewModel.Settings.IsUnsavedChangesVisible);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Start_Clicked_UsesExistingLaunchFlowAndRestoresWindow(bool succeeds)
    {
        using var context = new TrayContext();
        context.Executor.LaunchResult = new GameLaunchResult { Success = succeeds };
        context.Ready();
        context.Window.Show();
        context.Tray.HideWindow();
        context.Platform.StartGame?.Invoke();
        Assert.True(context.Window.IsVisible);
        Assert.Equal(1, context.Executor.LaunchCallCount);
        if (context.ViewModel.Operations.StartGameCommand.ExecutionTask is { } execution)
        {
            await execution;
        }

        Assert.False(context.ViewModel.Shell.IsBusy);
        Assert.Equal(succeeds ? 1 : 0, context.Monitor.BeginSessionCallCount);
    }

    [AvaloniaFact]
    public async Task Menu_SessionAndLanguageChange_UpdatesTextAndIgnoresQueuedUpdatesAfterDispose()
    {
        using var context = new TrayContext();
        context.Ready();
        await Task.Run(() => context.Monitor.Transition(GameSessionState.Starting));
        Dispatcher.UIThread.RunJobs();
        Assert.False(context.Platform.Text.CanStartGame);
        Assert.Equal(context.Localizer.T(LocalizationKeys.GameSessionStarting), context.Platform.Text.StartGame);
        context.Monitor.Transition(GameSessionState.Running);
        Assert.False(context.Platform.Text.CanStartGame);
        Assert.Equal(context.Localizer.T(LocalizationKeys.GameSessionRunning), context.Platform.Text.StartGame);

        foreach (string language in new[] { LauncherLanguages.SimplifiedChinese, LauncherLanguages.TraditionalChinese,
            LauncherLanguages.Japanese, LauncherLanguages.English })
        {
            context.Localizer.SetLanguage(language);
            Assert.Equal(context.Localizer.T(LocalizationKeys.GameSessionRunning), context.Platform.Text.StartGame);
            Assert.Equal(context.Localizer.T(LocalizationKeys.Settings), context.Platform.Text.Settings);
        }

        context.Monitor.Transition(GameSessionState.Exited);
        Assert.True(context.Platform.Text.CanStartGame);
        Assert.Equal(context.Localizer.T(LocalizationKeys.StartGame), context.Platform.Text.StartGame);

        await Task.Run(() => context.Monitor.Transition(GameSessionState.Starting));
        context.Tray.Dispose();
        int count = context.Platform.UpdateCount;
        Dispatcher.UIThread.RunJobs();
        context.ViewModel.Shell.IsBusy = true;
        Assert.Equal(count, context.Platform.UpdateCount);
        context.Platform.StartGame?.Invoke();
        context.Platform.OpenSettings?.Invoke();
        Assert.Equal(0, context.Executor.LaunchCallCount);
        Assert.False(context.ViewModel.WindowChrome.IsSettingsVisible);
    }

    private sealed class TrayContext : IDisposable
    {
        private readonly ServiceProvider provider;
        public TestDirectory Directory { get; } = TestDirectory.Create(TestDirectoryCleanup.BestEffort);
        public StubGameOperationExecutor Executor { get; } = new();
        public FakeGameSessionMonitor Monitor { get; } = new();
        public MainWindowViewModel ViewModel { get; }
        public LocalizationService Localizer { get; }
        public Window Window { get; } = new();
        public TestTrayPlatform Platform { get; } = new();
        public SystemTrayService Tray { get; }

        public TrayContext()
        {
            provider = HeadlessTestHost.CreateServiceProvider(Directory, services =>
            {
                services.AddSingleton<IGameOperationExecutor>(Executor);
                services.AddSingleton<IGameSessionMonitor>(Monitor);
            });
            ViewModel = provider.GetRequiredService<MainWindowViewModel>();
            Localizer = provider.GetRequiredService<LocalizationService>();
            Tray = new SystemTrayService(Window, Localizer, Platform,
                actions: provider.GetRequiredService<ISystemTrayActions>());
            Assert.True(Tray.Initialize());
        }

        public void Ready()
        {
            ViewModel.Operations.ApplySnapshot(new LauncherStatusSnapshot
            {
                RuntimeState = LauncherRuntimeState.Ready
            });
            ViewModel.Shell.IsBusy = false;
        }

        public void Dispose()
        {
            Tray.Dispose();
            Window.Close();
            provider.Dispose();
            Directory.Dispose();
        }
    }
}
