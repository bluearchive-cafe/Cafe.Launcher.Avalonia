using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Composition;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Features.Diagnostics;
using Cafe.Launcher.Avalonia.Features.GameOperations;
using Cafe.Launcher.Avalonia.Features.Settings;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class WindowChromeViewModelTests : IDisposable
{
    private readonly string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    static WindowChromeViewModelTests()
    {
        TestLocalizationHelper.Initialize();
    }

    [Fact]
    public void LegacyWindowDelegateProperties_AreRemoved()
    {
        var propertyNames = typeof(WindowChromeViewModel)
            .GetProperties(System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic)
            .Select(property => property.Name);

        Assert.DoesNotContain("GetSnapshot", propertyNames);
        Assert.DoesNotContain("MinimizeWindow", propertyNames);
        Assert.DoesNotContain("CloseWindow", propertyNames);
        Assert.DoesNotContain("RestoreWindow", propertyNames);
    }

    [Theory]
    [InlineData(PatchUrlGroups.Official, LauncherConstants.OfficialGameWebsiteUrl)]
    [InlineData(PatchUrlGroups.Cafe, LauncherConstants.CafeWebsiteUrl)]
    public void ResolveOfficialSiteUrl_UsesCurrentDownloadSource(
        string patchUrlGroup,
        string expectedUrl)
    {
        Assert.Equal(expectedUrl, WindowChromeViewModel.ResolveOfficialSiteUrl(patchUrlGroup));
    }

    [Fact]
    public void ShowSettingsCommand_OpensSettingsAndLoadsSnapshot()
    {
        using var context = CreateContext();
        context.Settings.ApplyLauncherSettings(
            new LauncherSettings { Language = LauncherLanguages.Japanese });

        context.ViewModel.ShowSettingsCommand.Execute(null);

        Assert.True(context.ViewModel.IsSettingsVisible);
        Assert.Equal(
            LauncherLanguages.Japanese,
            context.Settings.Editor.Current.Language);
    }

    [Fact]
    public void ShowSettingsCommand_WhenDirty_ShowsUnsavedChangesInsteadOfClosing()
    {
        using var context = CreateContext();
        context.Settings.ApplyLauncherSettings(new LauncherSettings());
        context.ViewModel.ShowSettingsCommand.Execute(null);
        context.Settings.Editor.Current.Language = LauncherLanguages.Japanese;

        context.ViewModel.ShowSettingsCommand.Execute(null);

        Assert.True(context.ViewModel.IsSettingsVisible);
        Assert.True(context.Settings.IsUnsavedChangesVisible);
    }

    [Fact]
    public async Task DiscardSettingsChangesCommand_DiscardsAndClosesSettings()
    {
        using var context = CreateContext();
        context.Settings.ApplyLauncherSettings(new LauncherSettings());
        context.ViewModel.ShowSettingsCommand.Execute(null);
        context.Settings.Editor.Current.Language = LauncherLanguages.Japanese;

        await context.ViewModel.DiscardSettingsChangesCommand.ExecuteAsync(null);

        Assert.False(context.ViewModel.IsSettingsVisible);
        Assert.False(context.Settings.IsSettingsDirty);
    }

    [Fact]
    public void MinimizeAndRestoreCommands_ControlCarouselAndWindowDelegates()
    {
        using var context = CreateContext();
        var minimized = false;
        var restored = false;
        context.RemoteContent.Apply(
            new LauncherRemoteState
            {
                OperationsResource = new OperationsResourceResponse
                {
                    OperationsResourceOpen = true,
                    BannerLoop = true,
                    OperationsBannerList =
                    [
                        new OperationsBannerItem(),
                        new OperationsBannerItem()
                    ]
                }
            },
            new LauncherSettings(),
            CancellationToken.None);
        context.ViewModel.MinimizeRequested += () => minimized = true;
        context.ViewModel.RestoreRequested += () => restored = true;

        context.ViewModel.MinimizeCommand.Execute(null);

        Assert.True(minimized);
        Assert.False(context.RemoteContent.IsCarouselTimerRunning);

        context.ViewModel.ExecuteRestoreWindowCommand.Execute(null);

        Assert.True(restored);
        Assert.True(context.RemoteContent.IsCarouselTimerRunning);
    }

    [Fact]
    public void CloseCommand_WhenDownloadIsRunning_ShowsConfirmation()
    {
        using var context = CreateContext();
        context.Backend.IsDownloadRunning = true;
        var closed = false;
        context.ViewModel.CloseRequested += () => closed = true;

        context.ViewModel.CloseCommand.Execute(null);

        Assert.True(context.Dialogs.IsDownloadRunningCloseConfirmVisible);
        Assert.False(closed);
    }

    [Fact]
    public void CloseCommand_WhenNoDownloadIsRunning_ClosesWindow()
    {
        using var context = CreateContext();
        var closed = false;
        context.ViewModel.CloseRequested += () => closed = true;

        context.ViewModel.CloseCommand.Execute(null);

        Assert.True(closed);
    }

    [Fact]
    public void CloseAfterStoppingDownload_ClearsPersistedStateAndClosesWindow()
    {
        using var context = CreateContext();
        var closed = false;
        context.ViewModel.CloseRequested += () => closed = true;

        context.ViewModel.CloseAfterStoppingDownload();

        Assert.True(context.Backend.LastClearPersistedState);
        Assert.True(closed);
    }

    [Fact]
    public void ShowSettingsCommand_WhenSettingsAreSaving_DoesNothing()
    {
        using var context = CreateContext();
        context.Settings.IsSaving = true;

        context.ViewModel.ShowSettingsCommand.Execute(null);

        Assert.False(context.ViewModel.IsSettingsVisible);
    }

    [Fact]
    public void KeepEditingSettingsCommand_HidesUnsavedChangesPrompt()
    {
        using var context = CreateContext();
        context.Settings.IsUnsavedChangesVisible = true;

        context.ViewModel.KeepEditingSettingsCommand.Execute(null);

        Assert.False(context.Settings.IsUnsavedChangesVisible);
    }

    [Fact]
    public void ExternalActionCommands_ForwardExactTargets()
    {
        using var context = CreateContext();
        var openedUrls = new List<string?>();
        string? openedDirectory = null;
        var viewModel = new WindowChromeViewModel(
            context.Settings,
            context.RemoteContent,
            context.Dialogs,
            context.Operations,
            context.Debug,
            openedUrls.Add,
            path => openedDirectory = path);
        context.Settings.Editor.ApplySnapshot(new LauncherSettings
        {
            PatchUrlGroup = PatchUrlGroups.Cafe
        });

        viewModel.OpenOfficialSiteCommand.Execute(null);
        viewModel.OpenAboutOfficialSiteCommand.Execute(null);
        viewModel.OpenHelpDocsCommand.Execute(null);
        viewModel.OpenGitHubRepositoryCommand.Execute(null);
        viewModel.OpenGitHubReleaseRepositoryCommand.Execute(null);
        viewModel.OpenPrivacyPolicyCommand.Execute(null);
        viewModel.OpenDefaultBackgroundArtworkCommand.Execute(null);
        viewModel.OpenExternalUrl("mailto:support@example.invalid");
        viewModel.OpenDataDirectoryCommand.Execute(null);

        Assert.Equal(LauncherConstants.CafeWebsiteUrl, openedUrls[0]);
        Assert.Equal(LauncherConstants.CafeWebsiteUrl, openedUrls[1]);
        Assert.Equal(LauncherConstants.HelpDocsUrl, openedUrls[2]);
        Assert.Equal(LauncherConstants.GitHubRepositoryUrl, openedUrls[3]);
        Assert.Equal(LauncherConstants.GitHubReleaseRepositoryUrl, openedUrls[4]);
        Assert.Equal(LauncherConstants.PrivacyPolicyUrl, openedUrls[5]);
        Assert.Equal(LauncherConstants.DefaultBackgroundArtworkUrl, openedUrls[6]);
        Assert.Equal("mailto:support@example.invalid", openedUrls[7]);
        Assert.Equal(LauncherUserDataDirectory.Root, openedDirectory);
    }

    [Fact]
    public void ExecuteRestoreWindow_WhenMotionReduced_DoesNotStartCarousel()
    {
        using var context = CreateContext();
        context.RemoteContent.Apply(
            new LauncherRemoteState
            {
                OperationsResource = new OperationsResourceResponse
                {
                    OperationsResourceOpen = true,
                    BannerLoop = true,
                    OperationsBannerList = [new(), new()]
                }
            },
            new LauncherSettings(),
            CancellationToken.None);
        context.RemoteContent.ApplyMotionPreference(true);

        context.ViewModel.ExecuteRestoreWindowCommand.Execute(null);

        Assert.False(context.RemoteContent.IsCarouselTimerRunning);
    }

    private TestContext CreateContext()
    {
        Directory.CreateDirectory(tempDir);
        var services = new ServiceCollection();
        var logger = new UnifiedLogger(Path.Combine(tempDir, "logs"));
        services.AddLauncherServices(logger);
        var provider = services.BuildServiceProvider();
        var settings = provider.GetRequiredService<SettingsViewModel>();
        var remoteContent = provider.GetRequiredService<RemoteContentViewModel>();
        var dialogs = provider.GetRequiredService<DialogsViewModel>();
        var backend = new TestBackend();
        var operations = new GameOperationsViewModel(
            backend,
            new TestGameShortcutService(),
            provider.GetRequiredService<LocalizationService>(),
            provider.GetRequiredService<ToastService>(),
            provider.GetRequiredService<LocalDiagnostics>(),
            provider.GetRequiredService<ShellViewModel>(),
            dialogs,
            provider.GetRequiredService<IErrorHandlingService>(),
            _ => Task.CompletedTask);
        var debug = new DebugViewModel(
            provider.GetRequiredService<ToastService>(),
            logger,
            provider.GetRequiredService<IErrorHandlingService>(),
            provider.GetRequiredService<LauncherSettingsService>(),
            operations,
            provider.GetRequiredService<ShellViewModel>(),
            new StubFilePickerService());
        var viewModel = new WindowChromeViewModel(
            settings,
            remoteContent,
            dialogs,
            operations,
            debug);
        return new TestContext(
            viewModel,
            debug,
            settings,
            remoteContent,
            dialogs,
            operations,
            backend,
            provider,
            logger);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private sealed record TestContext(
        WindowChromeViewModel ViewModel,
        DebugViewModel Debug,
        SettingsViewModel Settings,
        RemoteContentViewModel RemoteContent,
        DialogsViewModel Dialogs,
        GameOperationsViewModel Operations,
        TestBackend Backend,
        ServiceProvider Provider,
        UnifiedLogger Logger) : IDisposable
    {
        public void Dispose()
        {
            Provider.Dispose();
            Logger.Dispose();
        }
    }

    private sealed class TestBackend : IGameOperationExecutor
    {
        public bool IsDownloadRunning { get; set; }
        public bool IsPaused { get; private set; }
        public event Action? IsRunningChanged { add { } remove { } }
        public bool LastClearPersistedState { get; private set; }

        public Task<GameLaunchResult> LaunchAsync(
            LauncherStatusSnapshot snapshot,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new GameLaunchResult());

        public Task<GameOperationResult> InstallOrUpdateAsync(
            LauncherStatusSnapshot snapshot,
            Action<GameOperationProgress> progress,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new GameOperationResult());

        public Task<GameOperationResult> RepairAsync(
            LauncherStatusSnapshot snapshot,
            Action<GameOperationProgress> progress) =>
            Task.FromResult(new GameOperationResult());

        public Task<GameOperationResult> ValidateUninstallAsync(string gamePath) =>
            Task.FromResult(new GameOperationResult());

        public Task<GameOperationResult> UninstallAsync(
            LauncherStatusSnapshot snapshot,
            Action<GameOperationProgress> progress) =>
            Task.FromResult(new GameOperationResult());

        public Task<GameOperationResult?> ResumePersistedAsync(
            LauncherStatusSnapshot snapshot,
            Action<GameOperationProgress> progress,
            CancellationToken cancellationToken) =>
            Task.FromResult<GameOperationResult?>(null);

        public void Stop(bool clearPersistedState)
        {
            LastClearPersistedState = clearPersistedState;
            IsDownloadRunning = false;
        }

        public void Pause() => IsPaused = true;

        public void Resume() => IsPaused = false;
    }
}
