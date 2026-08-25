using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Features.Diagnostics;
using Cafe.Launcher.Avalonia.Features.Shell;
using Cafe.Launcher.Avalonia.Features.GameOperations;
using Cafe.Launcher.Avalonia.Features.ResourcePanel;
using Cafe.Launcher.Avalonia.Features.Settings;
using Cafe.Launcher.Avalonia.Features.SetupWizard;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Auth;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.ViewModels;
using Avalonia.Media;
using System.Net;
using System.Net.Http;
using System.Text;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class MainWindowViewModelTests : IDisposable
{
    [Fact]
    public async Task Dispose_UnsubscribesSettingsEditorNotifications()
    {
        var coreService = new CountingCoreService(CreateSnapshot());
        var viewModel = await CreateViewModelAsync(coreService);
        viewModel.Settings.Editor.ApplySnapshot(new LauncherSettings());
        var notificationCount = 0;
        viewModel.Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.IsSettingsDirty))
            {
                notificationCount++;
            }
        };

        viewModel.Dispose();
        viewModel.Settings.Editor.Current.Language = LauncherLanguages.Japanese;

        Assert.Equal(0, notificationCount);
    }

    [Fact]
    public async Task Dispose_UnhooksLifecycleCoordinationDelegates()
    {
        var viewModel = await CreateViewModelAsync(new CountingCoreService(CreateSnapshot()));

        Assert.NotNull(viewModel.Settings.Appearance.GetBackgroundBitmap);
        Assert.NotNull(viewModel.Settings.PreviewAppearanceAsync);
        Assert.NotNull(viewModel.Settings.ApplyLanguageAndTheme);
        Assert.NotNull(viewModel.RemoteContent.OpenExternalUrlRequested);
        Assert.NotNull(viewModel.Dialogs.SetupWizard.PickGameFolderAsync);

        viewModel.Dispose();

        Assert.Null(viewModel.Settings.Appearance.GetBackgroundBitmap);
        Assert.Null(viewModel.Settings.PreviewAppearanceAsync);
        Assert.Null(viewModel.Settings.ApplyLanguageAndTheme);
        Assert.Null(viewModel.RemoteContent.OpenExternalUrlRequested);
        Assert.Null(viewModel.Dialogs.SetupWizard.PickGameFolderAsync);
    }

    static MainWindowViewModelTests()
    {
        TestLocalizationHelper.Initialize();
    }

    private readonly string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private readonly ProxySettingsService proxySettings = new();
    private readonly HttpClientFactory httpClientFactory;
    private readonly LauncherApiClient apiClient = new(
        new HttpClientHandler(),
        new AuthorizationHeaderFactory(),
        new PatchUrlGroupService());
    private readonly ImageCacheService imageCacheService;

    public MainWindowViewModelTests()
    {
        Directory.CreateDirectory(tempDir);
        httpClientFactory = new HttpClientFactory(proxySettings);
        imageCacheService = new ImageCacheService(
            httpClientFactory,
            new Crc64Service(),
            RemoteHttpUrlValidator.CreateForTesting());
    }

    [Fact]
    public async Task Constructor_DoesNotLoadLauncherState()
    {
        var coreService = new CountingCoreService(CreateSnapshot());
        using var viewModel = await CreateViewModelAsync(coreService);

        Assert.Equal(0, coreService.LoadCount);
    }

    [Fact]
    public async Task SetupWizardLanguage_WhenChanged_AppliesLanguageImmediately()
    {
        var coreService = new CountingCoreService(CreateSnapshot());
        using var viewModel = await CreateViewModelAsync(coreService);
        viewModel.Dialogs.ShowSetupWizard();

        viewModel.Dialogs.SetupWizard.Language = LauncherLanguages.Japanese;

        Assert.Equal("言語", viewModel.Shell.I18n["setupWizardLanguage"]);
        Assert.Equal("言語", viewModel.Dialogs.SetupWizard.Steps[0].Title);
    }

    [Fact]
    public async Task SetupWizardLanguage_WhenWizardIsHidden_DoesNotPreviewLanguage()
    {
        var coreService = new CountingCoreService(CreateSnapshot());
        using var viewModel = await CreateViewModelAsync(coreService);
        var originalTitle = viewModel.Shell.I18n["setupWizardLanguage"];

        viewModel.Dialogs.SetupWizard.Language = LauncherLanguages.Japanese;

        Assert.Equal(originalTitle, viewModel.Shell.I18n["setupWizardLanguage"]);
    }

    [Fact]
    public async Task InitializeAsync_WhenCalledTwice_LoadsLauncherStateOnce()
    {
        var coreService = new CountingCoreService(CreateSnapshot());
        using var viewModel = await CreateViewModelAsync(coreService);

        await viewModel.InitializeAsync();
        await viewModel.InitializeAsync();

        Assert.Equal(1, coreService.LoadCount);
    }

    [Fact]
    public async Task HandleOperationsRefreshRequestedAsync_ConsumesSkipPersistedResumeOnce()
    {
        var coreService = new CountingCoreService(CreateSnapshot());
        var backend = new CountingGameOperationsBackend();
        using var viewModel = await CreateViewModelAsync(coreService, gameOperationsBackend: backend);

        await viewModel.HandleOperationsRefreshRequestedAsync(GameOperationsRefreshMode.Normal);
        Assert.Equal(1, backend.ResumeInvocationCount);

        await viewModel.HandleOperationsRefreshRequestedAsync(GameOperationsRefreshMode.SkipPersistedResume);
        Assert.Equal(1, backend.ResumeInvocationCount);

        await viewModel.HandleOperationsRefreshRequestedAsync(GameOperationsRefreshMode.Normal);
        Assert.Equal(2, backend.ResumeInvocationCount);
        Assert.Equal(3, coreService.LoadCount);
    }

    [Fact]
    public async Task ShellSetLoading_UsesPureLoadingValuesForStatusDetails()
    {
        var coreService = new CountingCoreService(CreateSnapshot());
        using var viewModel = await CreateViewModelAsync(coreService);

        viewModel.Shell.SetLoading();

        Assert.Equal(viewModel.Shell.ExecutableNameText, viewModel.Shell.LaunchCheckValueText);
        Assert.DoesNotContain(':', viewModel.Shell.ExecutableNameText);
        Assert.DoesNotContain('：', viewModel.Shell.ExecutableNameText);
    }

    [Fact]
    public async Task ShellSetLaunchCheckResult_UpdatesPureStatusDetailValue()
    {
        var coreService = new CountingCoreService(CreateSnapshot());
        using var viewModel = await CreateViewModelAsync(coreService);

        viewModel.Shell.SetLaunchCheckResult("manifest verified");

        Assert.Equal("manifest verified", viewModel.Shell.LaunchCheckValueText);
        Assert.DoesNotContain("Launch check:", viewModel.Shell.LaunchCheckValueText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("启动校验：", viewModel.Shell.LaunchCheckValueText, StringComparison.Ordinal);
        Assert.DoesNotContain("起動チェック：", viewModel.Shell.LaunchCheckValueText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InitializeAsync_WhenSnapshotLoads_PopulatesPureStatusDetailValues()
    {
        var snapshot = CreateSnapshot();
        var coreService = new CountingCoreService(snapshot);
        using var viewModel = await CreateViewModelAsync(coreService);

        await viewModel.InitializeAsync();

        Assert.Equal("BlueArchive.exe", viewModel.Shell.ExecutableNameText);
        Assert.Equal(
            viewModel.Settings.Options.ResolveLaunchCheckDisplayName(snapshot.Settings.LaunchCheckMode),
            viewModel.Shell.LaunchCheckValueText);
        Assert.DoesNotContain("Executable", viewModel.Shell.ExecutableNameText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("启动程序", viewModel.Shell.ExecutableNameText, StringComparison.Ordinal);
        Assert.DoesNotContain("実行ファイル", viewModel.Shell.ExecutableNameText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InitializeAsync_WhenNewsAndNoticesExist_AddsBothToNewsCategories()
    {
        var snapshot = CreateSnapshot();
        snapshot.Remote.OperationsResource = CreateOperationsResource();
        var coreService = new CountingCoreService(snapshot);
        using var viewModel = await CreateViewModelAsync(coreService);

        await viewModel.InitializeAsync();

        var items = viewModel.RemoteContent.NewsCategories.SelectMany(category => category.Items);
        Assert.Contains(items, item => item.Title == "news title");
        Assert.Contains(items, item => item.Title == "notice title");
    }

    [Fact]
    public async Task InitializeAsync_WhenShowRemoteContentCardIsFalse_HidesRemoteContentCard()
    {
        var snapshot = CreateSnapshot();
        snapshot.Settings.ShowRemoteContentCard = false;
        snapshot.Remote.OperationsResource = CreateOperationsResource();
        var coreService = new CountingCoreService(snapshot);
        using var viewModel = await CreateViewModelAsync(coreService);

        await viewModel.InitializeAsync();

        var items = viewModel.RemoteContent.NewsCategories.SelectMany(category => category.Items);
        Assert.Contains(items, item => item.Title == "news title");
        Assert.Contains(items, item => item.Title == "notice title");
        Assert.True(viewModel.RemoteContent.HasNewsItems);
        Assert.False(viewModel.RemoteContent.HasRemoteContent);
        Assert.False(viewModel.RemoteContent.IsPanelVisible);
    }

    [Fact]
    public async Task RefreshAsync_WhileCoreLoadIsPending_ShowsRemoteContentLoadingState()
    {
        var snapshot = CreateSnapshot();
        snapshot.Settings.ShowRemoteContentCard = true;
        snapshot.Remote.OperationsResource = CreateOperationsResource();
        var coreService = new BlockingSecondLoadCoreService(snapshot);
        using var viewModel = await CreateViewModelAsync(coreService);
        await viewModel.InitializeAsync();

        var refreshTask = viewModel.RefreshCommand.ExecuteAsync(null);
        await coreService.SecondLoadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        try
        {
            Assert.True(viewModel.IsBusy);
            Assert.True(viewModel.RemoteContent.IsLoading);
            Assert.True(viewModel.RemoteContent.IsPanelVisible);
        }
        finally
        {
            coreService.ReleaseSecondLoad.TrySetResult();
            await refreshTask;
        }

        Assert.False(viewModel.IsBusy);
        Assert.False(viewModel.RemoteContent.IsLoading);
        Assert.True(viewModel.RemoteContent.IsPanelVisible);
    }

    [Fact]
    public async Task PrepareForShutdownAsync_WhileRefreshIsPending_CancelsAndDrainsRefresh()
    {
        var coreService = new BlockingSecondLoadCoreService(CreateSnapshot());
        using var viewModel = await CreateViewModelAsync(coreService);
        await viewModel.InitializeAsync();

        Task refreshTask = viewModel.RefreshCommand.ExecuteAsync(null);
        await coreService.SecondLoadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await viewModel.PrepareForShutdownAsync().WaitAsync(TimeSpan.FromSeconds(2));
        await refreshTask.WaitAsync(TimeSpan.FromSeconds(2));
        await viewModel.HandleOperationsRefreshRequestedAsync(
            GameOperationsRefreshMode.SkipPersistedResume);

        Assert.Equal(2, coreService.LoadCount);
        Assert.False(viewModel.IsBusy);
        Assert.False(viewModel.RemoteContent.IsLoading);
    }

    [Fact]
    public async Task RefreshAsync_WhenRequestsOverlap_SerializesLoadsAndKeepsNewestSnapshot()
    {
        var initial = CreateSnapshot();
        initial.RuntimeState = LauncherRuntimeState.NotInstalled;
        var older = CreateSnapshot();
        older.RuntimeState = LauncherRuntimeState.Corrupted;
        var newest = CreateSnapshot();
        newest.RuntimeState = LauncherRuntimeState.Ready;
        var coreService = new SequencedBlockingCoreService(initial, older, newest);
        using var viewModel = await CreateViewModelAsync(coreService);
        await viewModel.InitializeAsync();

        var olderRefresh = viewModel.HandleOperationsRefreshRequestedAsync(
            GameOperationsRefreshMode.SkipPersistedResume);
        await coreService.SecondLoadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var newestRefresh = viewModel.HandleOperationsRefreshRequestedAsync(
            GameOperationsRefreshMode.SkipPersistedResume);

        coreService.ReleaseSecondLoad.TrySetResult();
        await Task.WhenAll(olderRefresh, newestRefresh).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, coreService.MaximumConcurrency);
        Assert.Equal(3, coreService.LoadCount);
        Assert.True(viewModel.Operations.IsControlPanelVisible);
        Assert.False(viewModel.Operations.IsInstallPanelVisible);
    }

    [Fact]
    public async Task SaveSettingsAsync_WhenRemoteContentVisibilityChanges_AppliesBeforeRefreshCompletes()
    {
        var snapshot = CreateSnapshot();
        snapshot.Settings.ShowRemoteContentCard = true;
        snapshot.Remote.OperationsResource = CreateOperationsResource();
        var coreService = new BlockingSecondLoadCoreService(snapshot);
        using var viewModel = await CreateViewModelAsync(coreService);
        await viewModel.InitializeAsync();
        Assert.True(viewModel.RemoteContent.HasRemoteContent);
        viewModel.Settings.Editor.Current.ShowRemoteContentCard = false;

        var saveTask = SaveSettingsAsync(viewModel);
        await coreService.SecondLoadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        try
        {
            Assert.False(viewModel.RemoteContent.HasRemoteContent);
        }
        finally
        {
            coreService.ReleaseSecondLoad.TrySetResult();
            await saveTask;
        }
    }

    [Fact]
    public async Task InitializeAsync_WhenCoreLoadFails_DoesNotShowNetworkLoadedToast()
    {
        var coreService = new ThrowingCoreService();
        var successToasts = new List<string>();
        var toastService = new ToastService();
        using var viewModel = await CreateViewModelAsync(coreService, toastService: toastService);
        toastService.ToastRaised += notification =>
        {
            if (notification.Severity == ToastSeverity.Success)
            {
                successToasts.Add(notification.Message);
            }
        };

        await viewModel.InitializeAsync();

        Assert.DoesNotContain(viewModel.Shell.I18n["statusNetworkLoaded"], successToasts);
        Assert.False(viewModel.RemoteContent.IsLoading);
    }

    [Fact]
    public async Task InitializeAsync_WhenInstallationStateIsCorrupted_OffersRepairInsteadOfLaunch()
    {
        var snapshot = CreateSnapshot();
        snapshot.RuntimeState = LauncherRuntimeState.Corrupted;
        var coreService = new CountingCoreService(snapshot);
        using var viewModel = await CreateViewModelAsync(coreService);

        await viewModel.InitializeAsync();
        await viewModel.Operations.InstallOrUpdateCommand.ExecuteAsync(null);

        Assert.Equal(viewModel.Shell.I18n["repair"], viewModel.Operations.InstallButtonText);
        Assert.True(viewModel.Operations.IsInstallPanelVisible);
        Assert.False(viewModel.Operations.IsControlPanelVisible);
        Assert.True(viewModel.Dialogs.IsRepairConfirmVisible);
    }

    [Fact]
    public async Task ConfirmRepairCommand_WhenShellIsWired_InvokesRepairOnce()
    {
        var snapshot = CreateSnapshot();
        snapshot.RuntimeState = LauncherRuntimeState.Corrupted;
        var backend = new CountingGameOperationsBackend
        {
            RepairResult = new GameOperationResult
            {
                Success = true,
                Message = "repaired"
            }
        };
        using var viewModel = await CreateViewModelAsync(
            new CountingCoreService(snapshot),
            gameOperationsBackend: backend);
        await viewModel.InitializeAsync();
        await viewModel.Operations.InstallOrUpdateCommand.ExecuteAsync(null);

        await viewModel.Dialogs.ConfirmRepairCommand.ExecuteAsync(null);

        Assert.Equal(1, backend.RepairInvocationCount);
    }

    [Fact]
    public async Task ConfirmUninstallCommand_WhenShellIsWired_InvokesUninstallOnce()
    {
        var snapshot = CreateSnapshot();
        snapshot.RuntimeState = LauncherRuntimeState.Ready;
        var backend = new CountingGameOperationsBackend
        {
            ValidateUninstallResult = new GameOperationResult { Success = true },
            UninstallResult = new GameOperationResult { Success = true, Message = "uninstalled" }
        };
        using var viewModel = await CreateViewModelAsync(
            new CountingCoreService(snapshot),
            gameOperationsBackend: backend);
        await viewModel.InitializeAsync();
        await viewModel.Operations.RequestUninstallCommand.ExecuteAsync(null);

        await viewModel.Dialogs.ConfirmUninstallCommand.ExecuteAsync(null);

        Assert.Equal(1, backend.UninstallInvocationCount);
    }

    [Fact]
    public async Task ConfirmStopCommand_WhenShellIsWired_InvokesStopOnce()
    {
        var backend = new CountingGameOperationsBackend(isDownloadRunning: true);
        using var viewModel = await CreateViewModelAsync(
            new CountingCoreService(CreateSnapshot()),
            gameOperationsBackend: backend);
        viewModel.Dialogs.ShowStopConfirm();

        viewModel.Dialogs.ConfirmStopCommand.Execute(null);

        Assert.Equal(1, backend.StopInvocationCount);
    }

    [Fact]
    public async Task InstallOrUpdateAsync_WhenRemoteStateIsUnavailable_ReloadsState()
    {
        var snapshot = CreateSnapshot();
        snapshot.RuntimeState = LauncherRuntimeState.RemoteUnavailable;
        var coreService = new CountingCoreService(snapshot);
        using var viewModel = await CreateViewModelAsync(coreService);
        await viewModel.InitializeAsync();

        await viewModel.Operations.InstallOrUpdateCommand.ExecuteAsync(null);

        Assert.Equal(2, coreService.LoadCount);
        Assert.Equal(viewModel.Shell.I18n["refresh"], viewModel.Operations.InstallButtonText);
    }

    [Fact]
    public async Task InitializeAsync_WhenSocialChannelIsPixiv_UsesPaletteIcon()
    {
        var snapshot = CreateSnapshot();
        snapshot.Remote.SocialMediaResource = new SocialMediaResourceResponse
        {
            SocialMediaResourceOpen = true,
            SocialMediaResourceList =
            [
                new SocialMediaResourceItem
                {
                    SocialMediaChannel = "pixiv",
                    JumpUrl = "https://example.invalid/pixiv"
                }
            ]
        };
        var coreService = new CountingCoreService(snapshot);
        using var viewModel = await CreateViewModelAsync(coreService);

        await viewModel.InitializeAsync();

        var item = Assert.Single(viewModel.RemoteContent.SocialMediaItems);
        Assert.Equal("Palette", item.SocialIconKind);
    }

    [Fact]
    public void RemoteContentItem_ImageStateTransitionsSeparateLoadingAndFailure()
    {
        var item = new RemoteContentItem();

        Assert.True(item.IsImageLoading);
        Assert.False(item.IsImageLoadFailed);

        item.MarkImageLoadFailed();

        Assert.False(item.IsImageLoading);
        Assert.True(item.IsImageLoadFailed);

        item.MarkImageLoading();

        Assert.True(item.IsImageLoading);
        Assert.False(item.IsImageLoadFailed);

        item.MarkImageLoaded();

        Assert.False(item.IsImageLoading);
        Assert.False(item.IsImageLoadFailed);
    }

    [Fact]
    public async Task ResolveRandomBackgroundImage_WhenFolderHasSupportedImage_ReturnsImageFromFolder()
    {
        var folderPath = Path.Combine(tempDir, "wallpapers");
        Directory.CreateDirectory(folderPath);
        var imagePath = Path.Combine(folderPath, "wallpaper.PNG");
        await WriteTestPngAsync(imagePath);

        var resolved = BackgroundViewModel.ResolveRandomBackgroundImage(folderPath);

        Assert.Equal(imagePath, resolved);
    }

    [Fact]
    public async Task ResolveRandomBackgroundImage_WhenOnlySubfolderHasImage_ReturnsNull()
    {
        var folderPath = Path.Combine(tempDir, "wallpapers");
        var nestedFolderPath = Path.Combine(folderPath, "nested");
        Directory.CreateDirectory(folderPath);
        Directory.CreateDirectory(nestedFolderPath);
        await WriteTestPngAsync(Path.Combine(nestedFolderPath, "wallpaper.png"));

        var resolved = BackgroundViewModel.ResolveRandomBackgroundImage(folderPath);

        Assert.Null(resolved);
    }

    [Fact]
    public void ResolveRandomBackgroundImage_WhenFolderHasNoSupportedImage_ReturnsNull()
    {
        var folderPath = Path.Combine(tempDir, "empty-wallpapers");
        Directory.CreateDirectory(folderPath);

        var resolved = BackgroundViewModel.ResolveRandomBackgroundImage(folderPath);

        Assert.Null(resolved);
    }

    [Fact]
    public async Task ApplyProgress_WhenProgressCannotPause_HidesPauseResume()
    {
        var coreService = new CountingCoreService(CreateSnapshot());
        using var viewModel = await CreateViewModelAsync(coreService);

        ApplyProgress(viewModel, new GameOperationProgress
        {
            OperationKind = GameOperationKind.Uninstall,
            Stage = GameOperationStage.Uninstalling,
            Progress = 50,
            CanPause = false
        });

        Assert.False(viewModel.Operations.CanPauseOperation);
    }

    [Fact]
    public async Task ApplyProgress_WhenProgressCanPause_ShowsPauseResume()
    {
        var coreService = new CountingCoreService(CreateSnapshot());
        using var viewModel = await CreateViewModelAsync(coreService);

        ApplyProgress(viewModel, new GameOperationProgress
        {
            OperationKind = GameOperationKind.Download,
            Stage = GameOperationStage.Downloading,
            Progress = 50,
            CanPause = true
        });

        Assert.True(viewModel.Operations.CanPauseOperation);
    }

    [Fact]
    public async Task SaveSettingsAsync_WhenPatchUrlGroupChangesForInstalledGame_ShowsRepairPrompt()
    {
        var snapshot = CreateSnapshot();
        snapshot.RuntimeState = LauncherRuntimeState.Ready;
        snapshot.Settings.PatchUrlGroup = PatchUrlGroups.Official;
        snapshot.LocalGame = new LocalInstallationState
        {
            Kind = LocalInstallationStateKind.Valid,
            GamePath = snapshot.LocalGame.GamePath,
            GameConfig = new GameLauncherConfig
            {
                Name = "BlueArchive",
                Version = "1.0.0"
            }
        };
        var settingsPath = Path.Combine(tempDir, Guid.NewGuid().ToString("N"), "settings.json");
        var settingsService = new LauncherSettingsService(settingsPath);
        await settingsService.SaveAsync(new LauncherSettings
        {
            GamePath = snapshot.Settings.GamePath,
            PatchUrlGroup = PatchUrlGroups.Official
        });
        var coreService = new CountingCoreService(snapshot);
        using var viewModel = await CreateViewModelAsync(coreService, settingsService);
        await viewModel.InitializeAsync();

        viewModel.Settings.Editor.Current.PatchUrlGroup = PatchUrlGroups.Cafe;
        await SaveSettingsAsync(viewModel);

        Assert.True(viewModel.Dialogs.IsRepairConfirmVisible);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.Dialogs.RepairConfirmText));
    }

    [Fact]
    public async Task SaveSettingsAsync_WhenPatchUrlGroupChangesDuringUpdateAvailable_ShowsRepairPrompt()
    {
        var snapshot = CreateSnapshot();
        snapshot.RuntimeState = LauncherRuntimeState.UpdateAvailable;
        snapshot.Settings.PatchUrlGroup = PatchUrlGroups.Official;
        snapshot.LocalGame = new LocalInstallationState
        {
            Kind = LocalInstallationStateKind.Valid,
            GamePath = snapshot.LocalGame.GamePath,
            GameConfig = new GameLauncherConfig
            {
                Name = "BlueArchive",
                Version = "1.0.0"
            }
        };
        var settingsPath = Path.Combine(tempDir, Guid.NewGuid().ToString("N"), "settings.json");
        var settingsService = new LauncherSettingsService(settingsPath);
        await settingsService.SaveAsync(new LauncherSettings
        {
            GamePath = snapshot.Settings.GamePath,
            PatchUrlGroup = PatchUrlGroups.Official
        });
        using var viewModel = await CreateViewModelAsync(
            new CountingCoreService(snapshot),
            settingsService);
        await viewModel.InitializeAsync();

        viewModel.Settings.Editor.Current.PatchUrlGroup = PatchUrlGroups.Cafe;
        await SaveSettingsAsync(viewModel);

        Assert.True(viewModel.Dialogs.IsRepairConfirmVisible);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.Dialogs.RepairConfirmText));
    }

    [Fact]
    public async Task ChooseGamePathAsync_UpdatesEditorWithoutPersistingUntilSave()
    {
        var pickedPath = Path.Combine(tempDir, "installed-game");
        Directory.CreateDirectory(pickedPath);
        // GameInstallationPath.NormalizeGamePath appends YostarGames/BlueArchive_JP
        var expectedPath = Path.Combine(pickedPath, "YostarGames", "BlueArchive_JP");
        var settingsPath = Path.Combine(tempDir, Guid.NewGuid().ToString("N"), "settings.json");
        var settingsService = new LauncherSettingsService(settingsPath);
        await settingsService.SaveAsync(new LauncherSettings());
        var snapshot = CreateSnapshot();
        snapshot.Settings.GamePath = "";
        snapshot.LocalGame = new LocalInstallationState();
        var coreService = new CountingCoreService(snapshot);
        using var viewModel = await CreateViewModelAsync(coreService, settingsService);
        await viewModel.InitializeAsync();
        viewModel.WindowChrome.IsSettingsVisible = true;
        viewModel.Settings.PickGameFolderAsync = _ => Task.FromResult<string?>(pickedPath);

        await viewModel.Settings.ChooseGamePathCommand.ExecuteAsync(null);

        Assert.True(viewModel.Settings.IsSettingsDirty);
        Assert.Equal(expectedPath, viewModel.Settings.Editor.Current.GamePath);
        Assert.Equal("", (await settingsService.ReadAsync()).GamePath);

        await SaveSettingsAsync(viewModel);

        Assert.False(viewModel.Settings.IsSettingsDirty);
        Assert.True(viewModel.WindowChrome.IsSettingsVisible);
        Assert.Equal(expectedPath, (await settingsService.ReadAsync()).GamePath);
    }

    [Fact]
    public async Task SelectInstalledGameAsync_PersistsOnlyGamePathAndRefreshesShell()
    {
        var originalPath = Path.Combine(tempDir, "original", "YostarGames", "BlueArchive_JP");
        var selectedRoot = Path.Combine(tempDir, "selected");
        Directory.CreateDirectory(selectedRoot);
        var expectedPath = Path.Combine(selectedRoot, "YostarGames", "BlueArchive_JP");
        var settingsPath = Path.Combine(tempDir, Guid.NewGuid().ToString("N"), "settings.json");
        var settingsService = new LauncherSettingsService(settingsPath);
        await settingsService.SaveAsync(new LauncherSettings
        {
            GamePath = originalPath,
            ThemeMode = ThemeModes.Light
        });
        var snapshot = CreateSnapshot();
        snapshot.Settings.GamePath = originalPath;
        snapshot.Settings.ThemeMode = ThemeModes.Light;
        snapshot.LocalGame = CopyLocalGameWithPath(snapshot.LocalGame, originalPath);
        var coreService = new SettingsBackedCoreService(settingsService, snapshot);
        using var viewModel = await CreateViewModelAsync(coreService, settingsService);
        await viewModel.InitializeAsync();
        viewModel.Settings.Editor.Current.ThemeMode = ThemeModes.Dark;
        viewModel.Settings.PickGameFolderAsync = _ => Task.FromResult<string?>(selectedRoot);

        await viewModel.Settings.SelectInstalledGameCommand.ExecuteAsync(null);

        var persisted = await settingsService.ReadAsync();
        Assert.Equal(expectedPath, persisted.GamePath);
        Assert.Equal(ThemeModes.Light, persisted.ThemeMode);
        Assert.Equal(expectedPath, viewModel.Shell.PathText);
        Assert.Equal(expectedPath, viewModel.Settings.Editor.Current.GamePath);
    }

    [Fact]
    public async Task ChooseBackgroundImageAsync_UpdatesEditorWithoutPersistingUntilSave()
    {
        var pickedPath = Path.Combine(tempDir, "background.png");
        await File.WriteAllBytesAsync(pickedPath, []);
        var settingsPath = Path.Combine(tempDir, Guid.NewGuid().ToString("N"), "settings.json");
        var settingsService = new LauncherSettingsService(settingsPath);
        await settingsService.SaveAsync(new LauncherSettings());
        var coreService = new CountingCoreService(CreateSnapshot());
        using var viewModel = await CreateViewModelAsync(coreService, settingsService);
        await viewModel.InitializeAsync();
        viewModel.Settings.PickBackgroundImageAsync = () => Task.FromResult<string?>(pickedPath);

        await viewModel.Settings.ChooseBackgroundImageCommand.ExecuteAsync(null);

        var persistedBeforeSave = await settingsService.ReadAsync();
        Assert.True(viewModel.Settings.IsSettingsDirty);
        Assert.Equal(pickedPath, viewModel.Settings.Editor.Current.CustomBackgroundPath);
        Assert.Equal(BackgroundSources.Custom, viewModel.Settings.Editor.Current.BackgroundSource);
        Assert.Equal("", persistedBeforeSave.CustomBackgroundPath);
        Assert.Equal(BackgroundSources.Bundled, persistedBeforeSave.BackgroundSource);

        await SaveSettingsAsync(viewModel);

        var persistedAfterSave = await settingsService.ReadAsync();
        Assert.False(viewModel.Settings.IsSettingsDirty);
        Assert.Equal(pickedPath, persistedAfterSave.CustomBackgroundPath);
        Assert.Equal(BackgroundSources.Custom, persistedAfterSave.BackgroundSource);
    }

    [Fact]
    public async Task AppearancePreview_WhenSettingChangesAgain_CancelsPreviousPreview()
    {
        var coreService = new CountingCoreService(CreateSnapshot());
        using var viewModel = await CreateViewModelAsync(coreService);
        var firstPreviewStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var firstPreviewCanceled = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        string? appliedPath = null;
        viewModel.Settings.PreviewAppearanceAsync = async (settings, propertyName, cancellationToken) =>
        {
            if (propertyName != nameof(LauncherSettings.CustomBackgroundPath))
            {
                return;
            }

            if (settings.CustomBackgroundPath == "first")
            {
                firstPreviewStarted.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    firstPreviewCanceled.TrySetResult();
                    throw;
                }
            }

            appliedPath = settings.CustomBackgroundPath;
        };

        viewModel.Settings.Editor.Current.CustomBackgroundPath = "first";
        await firstPreviewStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        viewModel.Settings.Editor.Current.CustomBackgroundPath = "second";
        await viewModel.Settings.PendingAppearancePreview;

        await firstPreviewCanceled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("second", appliedPath);
    }

    [Fact]
    public async Task BackgroundFolderAndClearCommands_DoNotPersistUntilSave()
    {
        var savedPath = Path.Combine(tempDir, "saved-background.png");
        var pickedFolder = Path.Combine(tempDir, "backgrounds");
        Directory.CreateDirectory(pickedFolder);
        var settingsPath = Path.Combine(tempDir, Guid.NewGuid().ToString("N"), "settings.json");
        var settingsService = new LauncherSettingsService(settingsPath);
        var persistedSettings = new LauncherSettings
        {
            BackgroundSource = BackgroundSources.Custom,
            CustomBackgroundPath = savedPath
        };
        await settingsService.SaveAsync(persistedSettings);
        var coreService = new CountingCoreService(CreateSnapshot());
        using var viewModel = await CreateViewModelAsync(coreService, settingsService);
        await viewModel.InitializeAsync();
        viewModel.Settings.Editor.ApplySnapshot(persistedSettings);
        viewModel.Settings.Appearance.Load(persistedSettings);
        viewModel.Settings.PickBackgroundFolderAsync = () => Task.FromResult<string?>(pickedFolder);

        await viewModel.Settings.ChooseBackgroundFolderCommand.ExecuteAsync(null);

        Assert.True(viewModel.Settings.IsSettingsDirty);
        Assert.Equal(pickedFolder, viewModel.Settings.Editor.Current.CustomBackgroundPath);
        Assert.Equal(savedPath, (await settingsService.ReadAsync()).CustomBackgroundPath);

        viewModel.Settings.ClearBackgroundCommand.Execute(null);

        Assert.Equal("", viewModel.Settings.Editor.Current.CustomBackgroundPath);
        Assert.Equal(BackgroundSources.Bundled, viewModel.Settings.Editor.Current.BackgroundSource);
        Assert.Equal(savedPath, (await settingsService.ReadAsync()).CustomBackgroundPath);

        await SaveSettingsAsync(viewModel);

        var saved = await settingsService.ReadAsync();
        Assert.Equal("", saved.CustomBackgroundPath);
        Assert.Equal(BackgroundSources.Bundled, saved.BackgroundSource);
    }

    [Fact]
    public async Task BackgroundPresentationSettings_ArePreviewedBeforeSave()
    {
        var coreService = new CountingCoreService(CreateSnapshot());
        using var viewModel = await CreateViewModelAsync(coreService);
        viewModel.Settings.Editor.ApplySnapshot(new LauncherSettings());

        viewModel.Settings.Editor.Current.BackgroundFit = BackgroundFits.Uniform;
        viewModel.Settings.Appearance.SelectedBackgroundFillColor =
            Color.FromArgb(0xFF, 0x12, 0x34, 0x56);
        await viewModel.Settings.PendingAppearancePreview;

        Assert.Equal(Stretch.Uniform, viewModel.Background.BackgroundStretch);
        var fill = Assert.IsType<SolidColorBrush>(viewModel.Background.BackgroundFillBrush);
        Assert.Equal(Color.FromArgb(0xFF, 0x12, 0x34, 0x56), fill.Color);
        Assert.True(viewModel.Settings.IsSettingsDirty);
    }

    [Fact]
    public async Task DiscardSettingsChangesAsync_RestoresSnapshotAndClosesSettings()
    {
        var snapshot = CreateSnapshot();
        snapshot.Settings.ThemeMode = ThemeModes.Light;
        var coreService = new CountingCoreService(snapshot);
        using var viewModel = await CreateViewModelAsync(coreService);
        await viewModel.InitializeAsync();
        viewModel.Settings.Editor.ApplySnapshot(snapshot.Settings);
        viewModel.Settings.Appearance.Load(snapshot.Settings);
        viewModel.WindowChrome.IsSettingsVisible = true;
        LauncherSettings? lastPreview = null;
        viewModel.Settings.PreviewAppearanceAsync = (settings, _, _) =>
        {
            lastPreview = settings;
            return Task.CompletedTask;
        };
        viewModel.Settings.Editor.Current.ThemeMode = ThemeModes.Dark;
        viewModel.WindowChrome.ShowSettingsCommand.Execute(null);

        Assert.True(viewModel.Settings.IsUnsavedChangesVisible);

        await viewModel.WindowChrome.DiscardSettingsChangesCommand.ExecuteAsync(null);

        Assert.False(viewModel.WindowChrome.IsSettingsVisible);
        Assert.False(viewModel.Settings.IsUnsavedChangesVisible);
        Assert.False(viewModel.Settings.IsSettingsDirty);
        Assert.Equal(ThemeModes.Light, viewModel.Settings.Editor.Current.ThemeMode);
        Assert.Equal(ThemeModes.Light, Assert.IsType<LauncherSettings>(lastPreview).ThemeMode);
    }

    [Fact]
    public async Task ShowSettingsCommand_WhenNoChanges_ClosesWithoutConfirmation()
    {
        var coreService = new CountingCoreService(CreateSnapshot());
        using var viewModel = await CreateViewModelAsync(coreService);
        viewModel.Settings.Editor.ApplySnapshot(new LauncherSettings());
        viewModel.WindowChrome.IsSettingsVisible = true;

        viewModel.WindowChrome.ShowSettingsCommand.Execute(null);

        Assert.False(viewModel.WindowChrome.IsSettingsVisible);
        Assert.False(viewModel.Settings.IsUnsavedChangesVisible);
    }

    [Fact]
    public async Task SaveSettingsCommand_IsEnabledOnlyWhenSettingsAreDirty()
    {
        var coreService = new CountingCoreService(CreateSnapshot());
        using var viewModel = await CreateViewModelAsync(coreService);
        viewModel.Settings.Editor.ApplySnapshot(new LauncherSettings());

        Assert.False(viewModel.Settings.CanSaveSettings);
        Assert.False(viewModel.Settings.SaveSettingsCommand.CanExecute(null));

        viewModel.Settings.Editor.Current.Language = LauncherLanguages.Japanese;

        Assert.True(viewModel.Settings.CanSaveSettings);
        Assert.True(viewModel.Settings.SaveSettingsCommand.CanExecute(null));
    }

    [Fact]
    public async Task SaveSettingsAsync_WhenPersistenceFails_KeepsDirtyState()
    {
        var settingsService = new LauncherSettingsService(tempDir);
        var localizer = new LocalizationService();
        var toastService = new ToastService();
        var editor = new SettingsEditor();
        var appearance = new SettingsAppearanceViewModel(editor);
        var dialogs = new DialogsViewModel(
            localizer,
            new NoticeStateService(Path.Combine(tempDir, "save-failure-notices.json")),
            new SetupWizardViewModel(localizer, new GameInstallationPath(), new LocalInstallationStateStore(), new LocalDiagnostics()));
        using var testLogger = new UnifiedLogger(tempDir);
        using var settings = new SettingsViewModel(
            settingsService,
            localizer,
            toastService,
            new LauncherUpdateService(new LauncherUpdateHandler()),
            dialogs,
            testLogger,
            new GameInstallationPath(),
            new SettingsOptionsViewModel(localizer, new DiskSpaceService()),
            appearance,
            new ErrorHandlingService(localizer, new LocalDiagnostics(testLogger), toastService));
        ToastNotification? errorToast = null;
        toastService.ToastRaised += notification =>
        {
            if (notification.Severity == ToastSeverity.Error)
            {
                errorToast = notification;
            }
        };
        editor.ApplySnapshot(new LauncherSettings());
        editor.Current.Language = LauncherLanguages.Japanese;

        await settings.SaveSettingsCommand.ExecuteAsync(null);

        Assert.True(settings.IsSettingsDirty);
        Assert.True(settings.CanSaveSettings);
        Assert.NotNull(errorToast);
    }

    [Fact]
    public async Task SelectedThemeColorMode_WhenSettingsVisible_MarksSettingsDirty()
    {
        var coreService = new CountingCoreService(CreateSnapshot());
        using var viewModel = await CreateViewModelAsync(coreService);
        viewModel.WindowChrome.IsSettingsVisible = true;
        viewModel.Settings.Editor.ApplySnapshot(viewModel.Settings.Editor.Current);

        viewModel.Settings.Editor.Current.ThemeColorMode = ThemeColorModes.System;

        Assert.True(viewModel.Settings.IsSettingsDirty);
    }

    [Fact]
    public async Task SaveSettingsAsync_WhenCustomThemeColorSelected_PersistsColor()
    {
        var settingsPath = Path.Combine(tempDir, Guid.NewGuid().ToString("N"), "settings.json");
        var settingsService = new LauncherSettingsService(settingsPath);
        await settingsService.SaveAsync(new LauncherSettings());
        var coreService = new CountingCoreService(CreateSnapshot());
        using var viewModel = await CreateViewModelAsync(coreService, settingsService);

        viewModel.Settings.Editor.Current.ThemeColorMode = ThemeColorModes.Custom;
        viewModel.Settings.Appearance.SelectedCustomThemeColor =
            Color.FromArgb(0xFF, 0x33, 0x66, 0x99);
        await SaveSettingsAsync(viewModel);

        var settings = await settingsService.ReadAsync();
        Assert.Equal(ThemeColorModes.Custom, settings.ThemeColorMode);
        Assert.Equal("#FF336699", settings.CustomThemeColor);
    }

    [Fact]
    public void ExtractPaletteFromBgraBuffer_WhenBackgroundHasMultipleColors_ReturnsAtMostFiveColors()
    {
        var buffer = CreateStripedBgraBuffer(
            12,
            6,
            [
                Color.FromRgb(0xD8, 0x20, 0x38),
                Color.FromRgb(0x20, 0x90, 0x40),
                Color.FromRgb(0x30, 0x50, 0xD8),
                Color.FromRgb(0xE0, 0xA0, 0x20),
                Color.FromRgb(0x90, 0x30, 0xB8),
                Color.FromRgb(0x20, 0xB8, 0xD8)
            ]);

        var palette = ThemeColorExtractionService.ExtractPaletteFromBgraBuffer(buffer, 12, 6, 12 * 4);

        Assert.InRange(palette.Count, 1, 5);
    }

    [Fact]
    public void ExtractPaletteFromBgraBuffer_WhenSaturatedAndGrayExist_PrioritizesSaturatedColor()
    {
        var buffer = CreateStripedBgraBuffer(
            8,
            4,
            [
                Color.FromRgb(0x80, 0x80, 0x80),
                Color.FromRgb(0xD8, 0x20, 0x38)
            ]);

        var palette = ThemeColorExtractionService.ExtractPaletteFromBgraBuffer(buffer, 8, 4, 8 * 4);

        Assert.NotEmpty(palette);
        Assert.True(palette[0].R > palette[0].G);
        Assert.True(palette[0].R > palette[0].B);
    }

    [Fact]
    public void ExtractPaletteFromBgraBuffer_WhenBackgroundHasNoUsableColor_ReturnsEmpty()
    {
        var buffer = CreateSolidBgraBuffer(0x80, 0x80, 0x80, 8, 8, 0xFF);

        var palette = ThemeColorExtractionService.ExtractPaletteFromBgraBuffer(buffer, 8, 8, 8 * 4);

        Assert.Empty(palette);
    }

    [Fact]
    public void ExtractPaletteFromBgraBuffer_WhenBackgroundIsTransparent_ReturnsEmpty()
    {
        var buffer = CreateSolidBgraBuffer(0xD8, 0x20, 0x38, 8, 8, 0x00);

        var palette = ThemeColorExtractionService.ExtractPaletteFromBgraBuffer(buffer, 8, 8, 8 * 4);

        Assert.Empty(palette);
    }

    [Fact]
    public void NormalizeAccentColorForUi_WhenColorIsPaleAndLowSaturation_IncreasesContrast()
    {
        var source = Color.FromRgb(0xC9, 0xCD, 0xD8);

        var normalized = SettingsAppearanceViewModel.NormalizeAccentColorForUi(source);

        Assert.NotEqual(source, normalized);
        Assert.True(GetPerceivedSaturation(normalized) >= 0.22d);
        Assert.True(GetRelativeLuminance(normalized) < GetRelativeLuminance(source));
    }

    [Fact]
    public void NormalizeAccentColorForUi_WhenColorAlreadyHasStrongContrast_KeepsOriginalColor()
    {
        var source = Color.FromRgb(0x20, 0x50, 0xD8);

        var normalized = SettingsAppearanceViewModel.NormalizeAccentColorForUi(source);

        Assert.Equal(source, normalized);
    }

    [Fact]
    public async Task SelectedThemeColorPaletteIndex_WhenSettingsVisible_MarksSettingsDirtyAndUpdatesSelection()
    {
        var coreService = new CountingCoreService(CreateSnapshot());
        using var viewModel = await CreateViewModelAsync(coreService);
        viewModel.Settings.Appearance.ThemeColorPaletteItems.Add(new ThemeColorPaletteItem
        {
            Index = 0,
            ColorHex = "#FFD82038",
            Brush = new SolidColorBrush(Color.FromRgb(0xD8, 0x20, 0x38))
        });
        viewModel.Settings.Appearance.ThemeColorPaletteItems.Add(new ThemeColorPaletteItem
        {
            Index = 1,
            ColorHex = "#FF2050D8",
            Brush = new SolidColorBrush(Color.FromRgb(0x20, 0x50, 0xD8))
        });
        viewModel.Settings.Editor.Current.ThemeColorMode = ThemeColorModes.Wallpaper;
        viewModel.WindowChrome.IsSettingsVisible = true;
        viewModel.Settings.Editor.ApplySnapshot(viewModel.Settings.Editor.Current);

        viewModel.Settings.Appearance.SelectedThemeColorPaletteIndex = 1;

        Assert.True(viewModel.Settings.IsSettingsDirty);
        Assert.False(viewModel.Settings.Appearance.ThemeColorPaletteItems[0].IsSelected);
        Assert.True(viewModel.Settings.Appearance.ThemeColorPaletteItems[1].IsSelected);
        var preview = Assert.IsType<SolidColorBrush>(
            viewModel.Settings.Appearance.ThemeColorPreviewBrush);
        Assert.Equal(Color.FromArgb(0xFF, 0x20, 0x50, 0xD8), preview.Color);
    }

    [Fact]
    public async Task SaveSettingsAsync_WhenWallpaperPaletteSelected_PersistsPaletteAndIndex()
    {
        var settingsPath = Path.Combine(tempDir, Guid.NewGuid().ToString("N"), "settings.json");
        var settingsService = new LauncherSettingsService(settingsPath);
        await settingsService.SaveAsync(new LauncherSettings());
        var coreService = new CountingCoreService(CreateSnapshot());
        using var viewModel = await CreateViewModelAsync(coreService, settingsService);
        viewModel.Settings.Editor.Current.ThemeColorMode = ThemeColorModes.Wallpaper;
        viewModel.Settings.Appearance.ThemeColorPaletteItems.Add(new ThemeColorPaletteItem
        {
            Index = 0,
            ColorHex = "#FFD82038",
            Brush = new SolidColorBrush(Color.FromRgb(0xD8, 0x20, 0x38))
        });
        viewModel.Settings.Appearance.ThemeColorPaletteItems.Add(new ThemeColorPaletteItem
        {
            Index = 1,
            ColorHex = "#FF2050D8",
            Brush = new SolidColorBrush(Color.FromRgb(0x20, 0x50, 0xD8))
        });
        viewModel.Settings.Appearance.SelectedThemeColorPaletteIndex = 1;

        await SaveSettingsAsync(viewModel);

        var settings = await settingsService.ReadAsync();
        Assert.Equal(ThemeColorModes.Wallpaper, settings.ThemeColorMode);
        Assert.Equal(["#FFD82038", "#FF2050D8"], settings.ThemeColorPalette);
        Assert.Equal(1, settings.SelectedThemeColorPaletteIndex);
    }

    [Fact]
    public async Task ShowSettingsAsync_WhenWallpaperPaletteWasRefreshed_KeepsCurrentPalette()
    {
        var snapshot = CreateSnapshot();
        snapshot.Settings.ThemeColorMode = ThemeColorModes.Wallpaper;
        snapshot.Settings.ThemeColorPalette = ["#FF2050D8"];
        var coreService = new CountingCoreService(snapshot);
        using var viewModel = await CreateViewModelAsync(coreService);

        await viewModel.InitializeAsync();
        viewModel.Settings.Appearance.ThemeColorPaletteItems.Clear();
        viewModel.Settings.Appearance.ThemeColorPaletteItems.Add(new ThemeColorPaletteItem
        {
            Index = 0,
            ColorHex = "#FFD82038",
            Brush = new SolidColorBrush(Color.FromRgb(0xD8, 0x20, 0x38)),
            IsSelected = true
        });
        viewModel.Settings.Appearance.SelectedThemeColorPaletteIndex = 0;
        viewModel.Settings.Editor.ApplySnapshot(viewModel.Settings.Editor.Current);
        Assert.Equal(
            "#FFD82038",
            Assert.Single(viewModel.Settings.Appearance.ThemeColorPaletteItems).ColorHex);

        viewModel.WindowChrome.ShowSettingsCommand.Execute(null);

        Assert.True(viewModel.WindowChrome.IsSettingsVisible);
        Assert.Equal(
            "#FFD82038",
            Assert.Single(viewModel.Settings.Appearance.ThemeColorPaletteItems).ColorHex);
        Assert.False(viewModel.Settings.IsSettingsDirty);
    }

    [Fact]
    public async Task OpenResourcePanelAsync_WhenCookieUidExists_LoadsStatusAndConfig()
    {
        var cookiePath = Path.Combine(tempDir, "Library");
        await WriteResourcePanelCookieLibraryAsync(cookiePath, "UIDTESTA");
        var settingsService = new LauncherSettingsService(Path.Combine(tempDir, Guid.NewGuid().ToString("N"), "settings.json"));
        var uidService = new ResourcePanelUidService(new BestHttpCookieLibraryService(), settingsService, cookiePath);
        var handler = new ResourcePanelHandler();
        using var apiClient = new ResourcePanelApiClient(handler);
        var coreService = new CountingCoreService(CreateSnapshot());
        using var viewModel = await CreateViewModelAsync(coreService, settingsService, uidService, apiClient);
        viewModel.ResourcePanel.ApplySettings(new LauncherSettings { PatchUrlGroup = PatchUrlGroups.Cafe });

        await viewModel.ResourcePanel.OpenResourcePanelCommand.ExecuteAsync(null);

        Assert.True(viewModel.ResourcePanel.IsResourcePanelVisible);
        Assert.False(viewModel.ResourcePanel.IsResourcePanelUidMissing);
        Assert.Equal("UIDTESTA", viewModel.ResourcePanel.ResourcePanelUid);
        Assert.Equal(1, handler.StatusListCount);
        Assert.Equal(1, handler.ConfigGetCount);
        var text = viewModel.ResourcePanel.ResourcePanelItems.First(item => item.Code == ResourcePanelResourceCodes.Text);
        var voice = viewModel.ResourcePanel.ResourcePanelItems.First(item => item.Code == ResourcePanelResourceCodes.Voice);
        Assert.Equal(viewModel.Shell.I18n["resourcePanelReady"], text.StatusText);
        Assert.True(text.IsEnabled);
        Assert.Equal(viewModel.Shell.I18n["resourcePanelWaiting"], voice.StatusText);
        Assert.False(voice.IsEnabled);
    }

    [Fact]
    public async Task OpenResourcePanelAsync_WhenSourceIsNotCafe_ShowsConfirmBeforeOpening()
    {
        var cookiePath = Path.Combine(tempDir, "Library");
        await WriteResourcePanelCookieLibraryAsync(cookiePath, "UIDTESTA");
        var settingsService = new LauncherSettingsService(Path.Combine(tempDir, Guid.NewGuid().ToString("N"), "settings.json"));
        var uidService = new ResourcePanelUidService(new BestHttpCookieLibraryService(), settingsService, cookiePath);
        var handler = new ResourcePanelHandler();
        using var apiClient = new ResourcePanelApiClient(handler);
        using var viewModel = await CreateViewModelAsync(
            new CountingCoreService(CreateSnapshot()),
            settingsService,
            uidService,
            apiClient);
        viewModel.ResourcePanel.ApplySettings(new LauncherSettings { PatchUrlGroup = PatchUrlGroups.Official });

        await viewModel.ResourcePanel.OpenResourcePanelCommand.ExecuteAsync(null);

        Assert.True(viewModel.Dialogs.IsResourcePanelSourceConfirmVisible);
        Assert.False(viewModel.ResourcePanel.IsResourcePanelVisible);
        Assert.Equal(0, handler.StatusListCount);
        Assert.Equal(0, handler.ConfigGetCount);
    }

    [Fact]
    public async Task ConfirmResourcePanelSourceSwitch_WhenUidExists_SwitchesToCafeAndOpensPanel()
    {
        var cookiePath = Path.Combine(tempDir, "Library");
        await WriteResourcePanelCookieLibraryAsync(cookiePath, "UIDTESTA");
        var settingsPath = Path.Combine(tempDir, Guid.NewGuid().ToString("N"), "settings.json");
        var settingsService = new LauncherSettingsService(settingsPath);
        await settingsService.SaveAsync(new LauncherSettings
        {
            PatchUrlGroup = PatchUrlGroups.Official
        });
        var uidService = new ResourcePanelUidService(new BestHttpCookieLibraryService(), settingsService, cookiePath);
        var handler = new ResourcePanelHandler();
        using var apiClient = new ResourcePanelApiClient(handler);
        var snapshot = CreateSnapshot();
        snapshot.Settings.PatchUrlGroup = PatchUrlGroups.Cafe;
        using var viewModel = await CreateViewModelAsync(
            new CountingCoreService(snapshot),
            settingsService,
            uidService,
            apiClient);
        viewModel.ResourcePanel.ApplySettings(new LauncherSettings { PatchUrlGroup = PatchUrlGroups.Official });
        await viewModel.ResourcePanel.OpenResourcePanelCommand.ExecuteAsync(null);

        viewModel.Dialogs.ConfirmResourcePanelSourceSwitchCommand.Execute(null);
        await WaitForConditionAsync(() =>
            viewModel.ResourcePanel.IsResourcePanelVisible
            && handler.StatusListCount == 1
            && handler.ConfigGetCount == 1);

        Assert.False(viewModel.Dialogs.IsResourcePanelSourceConfirmVisible);
        Assert.True(viewModel.ResourcePanel.IsResourcePanelVisible);
        Assert.Equal(PatchUrlGroups.Cafe, viewModel.Settings.Editor.Current.PatchUrlGroup);
        Assert.Equal(PatchUrlGroups.Cafe, (await settingsService.ReadAsync()).PatchUrlGroup);
        Assert.Equal(1, handler.ConfigGetCount);
    }

    [Fact]
    public async Task ResourcePanelApplySettings_UsesCafeSourceAndSystemProxyWhenOpeningPanel()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var proxyEndpoint = (IPEndPoint)listener.LocalEndpoint;
        var proxySettings = new ProxySettingsService(() => new SystemProxySettings(
            $"http://127.0.0.1:{proxyEndpoint.Port}",
            []));
        using var clientFactory = new HttpClientFactory(proxySettings);
        using var apiClient = new ResourcePanelApiClient(clientFactory);
        var settingsService = new LauncherSettingsService(
            Path.Combine(tempDir, Guid.NewGuid().ToString("N"), "settings.json"));
        await settingsService.SaveAsync(new LauncherSettings { ResourcePanelUid = "UIDTESTA" });
        var uidService = new ResourcePanelUidService(
            new BestHttpCookieLibraryService(),
            settingsService,
            Path.Combine(tempDir, "missing"));
        using var viewModel = await CreateViewModelAsync(
            new CountingCoreService(CreateSnapshot()),
            settingsService,
            uidService,
            apiClient);
        viewModel.ResourcePanel.ApplySettings(new LauncherSettings
        {
            ProxyMode = ProxyModes.System,
            PatchUrlGroup = PatchUrlGroups.Cafe
        });
        var proxyConnection = listener.AcceptTcpClientAsync();

        var openTask = viewModel.ResourcePanel.OpenResourcePanelCommand.ExecuteAsync(null);
        using var acceptedClient = await proxyConnection.WaitAsync(TimeSpan.FromSeconds(5));
        acceptedClient.Close();
        listener.Stop();
        await openTask;

        Assert.True(viewModel.ResourcePanel.IsResourcePanelVisible);
        Assert.False(viewModel.Dialogs.IsResourcePanelSourceConfirmVisible);
    }

    [Fact]
    public async Task SaveResourcePanelAsync_SendsCnForEnabledAndJpForDisabled()
    {
        var settingsService = new LauncherSettingsService(Path.Combine(tempDir, Guid.NewGuid().ToString("N"), "settings.json"));
        await settingsService.SaveAsync(new LauncherSettings { ResourcePanelUid = "UIDTESTA" });
        var uidService = new ResourcePanelUidService(
            new BestHttpCookieLibraryService(),
            settingsService,
            Path.Combine(tempDir, "missing"));
        var handler = new ResourcePanelHandler();
        using var apiClient = new ResourcePanelApiClient(handler);
        var coreService = new CountingCoreService(CreateSnapshot());
        using var viewModel = await CreateViewModelAsync(coreService, settingsService, uidService, apiClient);
        viewModel.ResourcePanel.ApplySettings(new LauncherSettings { PatchUrlGroup = PatchUrlGroups.Cafe });
        await viewModel.ResourcePanel.OpenResourcePanelCommand.ExecuteAsync(null);
        viewModel.ResourcePanel.ResourcePanelItems.First(item => item.Code == ResourcePanelResourceCodes.Text).IsEnabled = true;
        viewModel.ResourcePanel.ResourcePanelItems.First(item => item.Code == ResourcePanelResourceCodes.Voice).IsEnabled = false;
        viewModel.ResourcePanel.ResourcePanelItems.First(item => item.Code == ResourcePanelResourceCodes.Media).IsEnabled = true;

        await viewModel.ResourcePanel.SaveResourcePanelCommand.ExecuteAsync(null);

        Assert.Equal("GET", handler.LastRequestMethod);
        Assert.Equal("/config/set?uid=UIDTESTA&text=cn&voice=jp&media=cn", handler.LastRequestPathAndQuery);
        Assert.Null(handler.LastRequestBody);
        Assert.Equal(1, handler.ConfigSetCount);
    }

    [Fact]
    public async Task OpenResourcePanelAsync_WhenUidMissing_ShowsManualInputAndSkipsApiCalls()
    {
        var settingsService = new LauncherSettingsService(Path.Combine(tempDir, Guid.NewGuid().ToString("N"), "settings.json"));
        var uidService = new ResourcePanelUidService(
            new BestHttpCookieLibraryService(),
            settingsService,
            Path.Combine(tempDir, "missing"));
        var handler = new ResourcePanelHandler();
        using var apiClient = new ResourcePanelApiClient(handler);
        var coreService = new CountingCoreService(CreateSnapshot());
        using var viewModel = await CreateViewModelAsync(coreService, settingsService, uidService, apiClient);
        viewModel.ResourcePanel.ApplySettings(new LauncherSettings { PatchUrlGroup = PatchUrlGroups.Cafe });

        await viewModel.ResourcePanel.OpenResourcePanelCommand.ExecuteAsync(null);

        Assert.True(viewModel.ResourcePanel.IsResourcePanelVisible);
        Assert.True(viewModel.ResourcePanel.IsResourcePanelUidMissing);
        Assert.Equal("", viewModel.ResourcePanel.ResourcePanelUid);
        Assert.Equal(0, handler.StatusListCount);
        Assert.Equal(0, handler.ConfigGetCount);
        Assert.Equal(0, handler.ConfigSetCount);
    }

    [Fact]
    public async Task SaveManualResourcePanelUidAsync_WhenUidIsBlank_ShowsValidationMessage()
    {
        var settingsService = new LauncherSettingsService(Path.Combine(tempDir, Guid.NewGuid().ToString("N"), "settings.json"));
        var uidService = new ResourcePanelUidService(
            new BestHttpCookieLibraryService(),
            settingsService,
            Path.Combine(tempDir, "missing"));
        var handler = new ResourcePanelHandler();
        using var apiClient = new ResourcePanelApiClient(handler);
        using var viewModel = await CreateViewModelAsync(
            new CountingCoreService(CreateSnapshot()),
            settingsService,
            uidService,
            apiClient);
        viewModel.ResourcePanel.ApplySettings(new LauncherSettings { PatchUrlGroup = PatchUrlGroups.Cafe });
        viewModel.ResourcePanel.ManualResourcePanelUid = "   ";

        await viewModel.ResourcePanel.SaveManualResourcePanelUidCommand.ExecuteAsync(null);

        Assert.Equal(viewModel.Shell.I18n["resourcePanelUidEmpty"], viewModel.ResourcePanel.ResourcePanelMessage);
        Assert.Equal(0, handler.StatusListCount);
        Assert.Equal(0, handler.ConfigGetCount);
        Assert.Equal(0, handler.ConfigSetCount);
    }

    [Fact]
    public async Task TryHandleEscape_ForEveryModalKind_ClosesOnlyTopModal()
    {
        using var viewModel = await CreateViewModelAsync(new CountingCoreService(CreateSnapshot()));

        viewModel.WindowChrome.IsSettingsVisible = true;
        viewModel.Dialogs.ShowRepairConfirm("repair");
        Assert.Equal(ModalKind.RepairConfirmation, viewModel.ModalHost.Top?.Kind);
        Assert.True(viewModel.TryHandleEscape());
        Assert.False(viewModel.Dialogs.IsRepairConfirmVisible);
        Assert.True(viewModel.WindowChrome.IsSettingsVisible);
        Assert.True(viewModel.TryHandleEscape());
        Assert.False(viewModel.WindowChrome.IsSettingsVisible);

        viewModel.Settings.IsUnsavedChangesVisible = true;
        Assert.True(viewModel.TryHandleEscape());
        Assert.False(viewModel.Settings.IsUnsavedChangesVisible);

        viewModel.Dialogs.ShowResourcePanelSourceConfirm("source");
        Assert.True(viewModel.TryHandleEscape());
        Assert.False(viewModel.Dialogs.IsResourcePanelSourceConfirmVisible);

        viewModel.Dialogs.ShowUninstallConfirm("uninstall");
        Assert.True(viewModel.TryHandleEscape());
        Assert.False(viewModel.Dialogs.IsUninstallConfirmVisible);

        viewModel.Dialogs.ShowStopConfirm();
        Assert.True(viewModel.TryHandleEscape());
        Assert.False(viewModel.Dialogs.IsStopConfirmVisible);

        viewModel.Dialogs.ShowDownloadRunningCloseConfirm();
        Assert.True(viewModel.TryHandleEscape());
        Assert.False(viewModel.Dialogs.IsDownloadRunningCloseConfirmVisible);

        viewModel.Dialogs.IsNoticeDialogVisible = true;
        Assert.True(viewModel.TryHandleEscape());
        Assert.False(viewModel.Dialogs.IsNoticeDialogVisible);

        viewModel.Dialogs.ShowUpdateAvailable("1.0.0", []);
        Assert.True(viewModel.TryHandleEscape());
        Assert.False(viewModel.Dialogs.IsUpdateAvailableVisible);

        viewModel.LogViewer.OpenCommand.Execute(null);
        Assert.True(viewModel.TryHandleEscape());
        Assert.False(viewModel.LogViewer.IsVisible);

        viewModel.ResourcePanel.IsResourcePanelVisible = true;
        Assert.True(viewModel.TryHandleEscape());
        Assert.False(viewModel.ResourcePanel.IsResourcePanelVisible);

        Assert.False(viewModel.TryHandleEscape());
    }

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
            gameOperationsBackend: new CountingGameOperationsBackend(isDownloadRunning: true),
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
            gameOperationsBackend: new CountingGameOperationsBackend(isDownloadRunning: true),
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
            Assert.IsType<global::Avalonia.Animation.PageSlide>(
                viewModel.RemoteContent.CarouselTransition).Duration);
        Assert.Empty(viewModel.Toasts.ActiveToasts);
        Assert.False(toast.IsExiting);
        Assert.Equal(0, exitDelayCalls);
        displayDelay.TrySetResult();
    }

    [Fact]
    public async Task InitializeAsync_WhenStartupUpdateCheckEnabledAndUpdateAvailable_ShowsToast()
    {
        var snapshot = CreateSnapshot();
        snapshot.Settings.EnableStartupUpdateCheck = true;
        snapshot.Settings.UpdateChannel = UpdateChannels.Stable;
        var coreService = new CountingCoreService(snapshot);
        var toasts = new List<string>();
        var toastService = new ToastService();
        toastService.ToastRaised += notification => toasts.Add(notification.Message);
        var releaseJson = """
            [
                {
                    "version": "99.0.0",
                    "files": [
                        {
                            "name": "installer.exe",
                            "url": "https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia_Release/releases/download/v99.0.0/installer.exe",
                            "size": 123456
                        }
                    ],
                    "releaseDate": "2026-01-01"
                }
            ]
            """;
        var updateSvc = new LauncherUpdateService(
            new LauncherUpdateHandler(releaseJson),
            currentVersionOverride: "1.0.0");
        using var viewModel = await CreateViewModelAsync(
            coreService,
            toastService: toastService,
            launcherUpdateService: updateSvc);

        await viewModel.InitializeAsync();
        await viewModel.PendingStartupUpdateCheck.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Contains(toasts, t => t.Contains("99.0.0"));
    }

    [Fact]
    public async Task InitializeAsync_WhenStartupUpdateCheckDisabled_DoesNotShowToast()
    {
        var snapshot = CreateSnapshot();
        snapshot.Settings.EnableStartupUpdateCheck = false;
        snapshot.Settings.UpdateChannel = UpdateChannels.Stable;
        var coreService = new CountingCoreService(snapshot);
        var toasts = new List<string>();
        var toastService = new ToastService();
        toastService.ToastRaised += notification => toasts.Add(notification.Message);
        using var viewModel = await CreateViewModelAsync(
            coreService,
            toastService: toastService);

        await viewModel.InitializeAsync();
        await viewModel.PendingStartupUpdateCheck.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.DoesNotContain(toasts, t => t.Contains("available"));
    }

    [Fact]
    public async Task InitializeAsync_WhenStartupUpdateCheckEnabledButNoUpdate_DoesNotShowToast()
    {
        var snapshot = CreateSnapshot();
        snapshot.Settings.EnableStartupUpdateCheck = true;
        snapshot.Settings.UpdateChannel = UpdateChannels.Stable;
        var coreService = new CountingCoreService(snapshot);
        var toasts = new List<string>();
        var toastService = new ToastService();
        toastService.ToastRaised += notification => toasts.Add(notification.Message);
        // LauncherUpdateHandler returns 404 by default (no releases found = no update)
        using var viewModel = await CreateViewModelAsync(
            coreService,
            toastService: toastService);

        await viewModel.InitializeAsync();
        await viewModel.PendingStartupUpdateCheck.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.DoesNotContain(toasts, t => t.Contains("available"));
    }

    [Fact]
    public async Task InstallFailureViewLogAction_OpensLogViewerUntilMainWindowIsDisposed()
    {
        var toastService = new ToastService();
        ToastNotification? raised = null;
        toastService.ToastRaised += toast => raised = toast;
        var backend = new CountingGameOperationsBackend
        {
            InstallResult = new GameOperationResult
            {
                Success = false,
                Message = "offline"
            }
        };
        var viewModel = await CreateViewModelAsync(
            new CountingCoreService(CreateSnapshot()),
            toastService: toastService,
            gameOperationsBackend: backend);
        viewModel.Shell.IsBusy = false;
        viewModel.Operations.ApplySnapshot(new LauncherStatusSnapshot
        {
            RuntimeState = LauncherRuntimeState.NotInstalled
        });
        await viewModel.Operations.InstallOrUpdateCommand.ExecuteAsync(null);

        await raised!.SecondaryAction!.ExecuteAsync(CancellationToken.None);

        Assert.True(viewModel.LogViewer.IsVisible);
        viewModel.LogViewer.CloseCommand.Execute(null);
        viewModel.Dispose();
        await raised.SecondaryAction.ExecuteAsync(CancellationToken.None);
        Assert.False(viewModel.LogViewer.IsVisible);
    }

    private async Task<MainWindowViewModel> CreateViewModelAsync(
        ILauncherCoreService coreService,
        LauncherSettingsService? settingsService = null,
        ResourcePanelUidService? resourcePanelUidService = null,
        ResourcePanelApiClient? resourcePanelApiClient = null,
        ToastService? toastService = null,
        LauncherUpdateService? launcherUpdateService = null,
        CountingGameOperationsBackend? gameOperationsBackend = null,
        WindowsAnimationSettingsProvider? windowsAnimationSettingsProvider = null,
        Func<TimeSpan, CancellationToken, Task>? toastDelayAsync = null)
    {
        settingsService ??= new LauncherSettingsService(
            Path.Combine(tempDir, Guid.NewGuid().ToString("N"), "settings.json"));
        var localInstallationStateStore = new LocalInstallationStateStore();
        var diagnostics = new LocalDiagnostics();
        var localizationService = new LocalizationService();
        var remoteManifestService = new RemoteManifestService(apiClient);
        var diagnosticsVal = new LocalDiagnostics();
        var fileDownloadService = new FileDownloadService(
            new Crc64Service(),
            diagnosticsVal,
            RemoteHttpUrlValidator.CreateForTesting());
        var manifestValidationService = new ManifestValidationService(apiClient, remoteManifestService, localizationService);
        var gameLaunchService = new GameLaunchService(
            manifestValidationService,
            new ClickCodeService(),
            localizationService);
        var gameDownloadService = new GameDownloadService(
            apiClient,
            remoteManifestService,
            fileDownloadService,
            localInstallationStateStore,
            settingsService,
            new HttpClientFactory(new ProxySettingsService()),
            new Crc64Service(),
            new DiskSpaceService(),
            diagnostics,
            localizationService,
            new GameInstallationPath(),
            Path.Combine(tempDir, Guid.NewGuid().ToString("N"), "download_state.json"));
        resourcePanelUidService ??= new ResourcePanelUidService(
            new BestHttpCookieLibraryService(),
            settingsService,
            Path.Combine(tempDir, "missing-resource-panel-cookie"));
        resourcePanelApiClient ??= new ResourcePanelApiClient(new ResourcePanelHandler());

        toastService ??= new ToastService();
        var diskSpaceService = new DiskSpaceService();
        var launcherUpdateSvc = launcherUpdateService ?? new LauncherUpdateService(new LauncherUpdateHandler());
        var settingsEditor = new SettingsEditor();
        var settingsOptions = new SettingsOptionsViewModel(localizationService, diskSpaceService);
        var settingsAppearance = new SettingsAppearanceViewModel(settingsEditor);
        var shellViewModel = new ShellViewModel(localizationService);
        var errorHandling = new ErrorHandlingService(localizationService, diagnostics, toastService);
        var noticeStateService = new NoticeStateService(Path.Combine(tempDir, Guid.NewGuid().ToString("N"), "shown_notices.json"));
        var dialogsViewModel = new DialogsViewModel(localizationService, noticeStateService, new SetupWizardViewModel(localizationService, new GameInstallationPath(), new LocalInstallationStateStore(), new LocalDiagnostics()));
        using var settingsLogger = new UnifiedLogger(Path.Combine(tempDir, Guid.NewGuid().ToString("N")));
        var settingsViewModel = new SettingsViewModel(
            settingsService, localizationService, toastService,
            launcherUpdateSvc, dialogsViewModel,
            settingsLogger,
            new GameInstallationPath(),
            settingsOptions, settingsAppearance, errorHandling);
        var resourcePanelService = new ResourcePanelService(
            resourcePanelUidService, resourcePanelApiClient, diagnostics);
        var resourcePanelViewModel = new ResourcePanelViewModel(
            resourcePanelService, localizationService, toastService, errorHandling);
        var gameUninstallService = new GameUninstallService(
            localInstallationStateStore,
            diagnostics,
            localizationService,
            new GameInstallationPath());

        var remoteContentViewModel = new RemoteContentViewModel(localizationService, imageCacheService);
        var backgroundViewModel = new BackgroundViewModel(imageCacheService, diagnostics, settingsViewModel);
        var gameOperationsViewModel = gameOperationsBackend is null
            ? new GameOperationsViewModel(
                gameLaunchService,
                gameDownloadService,
                gameUninstallService,
                localizationService,
                toastService,
                diagnostics,
                shellViewModel,
                dialogsViewModel,
                errorHandling)
            : new GameOperationsViewModel(
                gameOperationsBackend,
                gameOperationsBackend,
                gameOperationsBackend,
                localizationService,
                toastService,
                diagnostics,
                shellViewModel,
                dialogsViewModel,
                _ => Task.CompletedTask,
                errorHandling);
        var toastHostViewModel = toastDelayAsync is null
            ? new ToastHostViewModel(toastService, localizationService, diagnostics)
            : new ToastHostViewModel(
                toastService,
                localizationService,
                diagnostics,
            action =>
                {
                    action();
                    return Task.CompletedTask;
                },
                toastDelayAsync);
        var debugViewModel = new DebugViewModel(toastService, new UnifiedLogger(Path.Combine(tempDir, Guid.NewGuid().ToString("N"))), errorHandling, settingsService, gameOperationsViewModel, shellViewModel);
        var windowChromeViewModel = new WindowChromeViewModel(
            settingsViewModel, remoteContentViewModel, dialogsViewModel, gameOperationsViewModel,
            debugViewModel);

        using var testLogger = new UnifiedLogger(tempDir);
        return new MainWindowViewModel(
            coreService,
            settingsService,
            localizationService,
            toastService,
            launcherUpdateSvc,
            diagnostics,
            shellViewModel,
            backgroundViewModel,
            remoteContentViewModel,
            dialogsViewModel,
            gameOperationsViewModel,
            toastHostViewModel,
            windowChromeViewModel,
            settingsViewModel,
            resourcePanelViewModel,
            errorHandling,
            new LogViewerDialogViewModel(testLogger, null, null, null, null, null),
            debugViewModel,
            new ModalHostViewModel(),
            windowsAnimationSettingsProvider ?? new WindowsAnimationSettingsProvider());
    }

    private LauncherStatusSnapshot CreateSnapshot()
    {
        var gamePath = Path.Combine(tempDir, "YostarGames", "BlueArchive_JP");
        return new LauncherStatusSnapshot
        {
            Settings = new LauncherSettings
            {
                GamePath = gamePath
            },
            LocalGame = new LocalInstallationState
            {
                GamePath = gamePath
            },
            Remote = new LauncherRemoteState
            {
                GameConfig = new GameConfigResponse
                {
                    GameLatestVersion = "1.0.0",
                    GameStartExeName = "BlueArchive"
                }
            },
            CheckedAt = DateTimeOffset.Now
        };
    }

    private static OperationsResourceResponse CreateOperationsResource()
    {
        return new OperationsResourceResponse
        {
            OperationsResourceOpen = true,
            NewsList = new NewsListEnvelope
            {
                Code = 0,
                Data = new NewsListData
                {
                    News =
                    [
                        new NewsTypeItem
                        {
                            TypeLabel = "news",
                            Rows =
                            [
                                new NewsRowItem
                                {
                                    Title = "news title",
                                    PublishTime = 0,
                                    Link = "https://example.invalid/news"
                                }
                            ]
                        }
                    ]
                }
            },
            NoticeList =
            [
                new NoticeTypeItem
                {
                    NoticeType = "notice",
                    NoticeDetailList =
                    [
                        new NoticeDetailItem
                        {
                            NoticeTitle = "notice title",
                            NoticeTime = "2026-06-12",
                            JumpUrl = "https://example.invalid/notice"
                        }
                    ]
                }
            ]
        };
    }

    private static Task WriteTestPngAsync(string path)
    {
        var bytes = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII=");
        return File.WriteAllBytesAsync(path, bytes);
    }

    private static async Task WriteResourcePanelCookieLibraryAsync(string path, string uid)
    {
        await using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(1);
        writer.Write(1);
        writer.Write(1);
        writer.Write("uid");
        writer.Write(uid);
        writer.Write(DateTime.UtcNow.ToBinary());
        writer.Write(DateTime.UtcNow.ToBinary());
        writer.Write(DateTime.FromBinary(0).ToBinary());
        writer.Write(2147483647L);
        writer.Write(false);
        writer.Write("bluearchive.cafe");
        writer.Write("/");
        writer.Write(false);
        writer.Write(false);
        writer.Flush();
    }

    private static void ApplyProgress(MainWindowViewModel viewModel, GameOperationProgress progress)
    {
        viewModel.Operations.ApplyProgress(progress);
    }

    private static byte[] CreateSolidBgraBuffer(byte r, byte g, byte b, int width, int height, byte alpha)
    {
        var rowBytes = width * 4;
        var buffer = new byte[rowBytes * height];
        for (var y = 0; y < height; y++)
        {
            var rowOffset = y * rowBytes;
            for (var x = 0; x < width; x++)
            {
                var offset = rowOffset + (x * 4);
                buffer[offset] = b;
                buffer[offset + 1] = g;
                buffer[offset + 2] = r;
                buffer[offset + 3] = alpha;
            }
        }

        return buffer;
    }

    private static LocalInstallationState CopyLocalGameWithPath(
        LocalInstallationState source,
        string gamePath)
    {
        return new LocalInstallationState
        {
            Kind = source.Kind,
            GamePath = gamePath,
            ConfigPath = source.ConfigPath,
            ManifestPath = source.ManifestPath,
            GameConfig = source.GameConfig,
            Manifest = source.Manifest,
            Error = source.Error
        };
    }

    private static byte[] CreateStripedBgraBuffer(int width, int height, IReadOnlyList<Color> colors)
    {
        var rowBytes = width * 4;
        var buffer = new byte[rowBytes * height];
        for (var y = 0; y < height; y++)
        {
            var rowOffset = y * rowBytes;
            for (var x = 0; x < width; x++)
            {
                var color = colors[Math.Min(colors.Count - 1, x * colors.Count / width)];
                var offset = rowOffset + (x * 4);
                buffer[offset] = color.B;
                buffer[offset + 1] = color.G;
                buffer[offset + 2] = color.R;
                buffer[offset + 3] = color.A;
            }
        }

        return buffer;
    }

    private static async Task SaveSettingsAsync(MainWindowViewModel viewModel)
    {
        await viewModel.Settings.SaveSettingsCommand.ExecuteAsync(null);
    }

    private static async Task WaitForConditionAsync(Func<bool> condition)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
        {
            cts.Token.ThrowIfCancellationRequested();
            await Task.Delay(10, cts.Token);
        }
    }

    public void Dispose()
    {
        imageCacheService.Dispose();
        apiClient.Dispose();
        httpClientFactory.Dispose();
        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private sealed class CountingCoreService : ILauncherCoreService
    {
        private readonly LauncherStatusSnapshot snapshot;

        public CountingCoreService(LauncherStatusSnapshot snapshot)
        {
            this.snapshot = snapshot;
        }

        public int LoadCount { get; private set; }

        public Task<LauncherStatusSnapshot> LoadAsync(CancellationToken cancellationToken = default)
        {
            LoadCount++;
            return Task.FromResult(snapshot);
        }
    }

    private sealed class SettingsBackedCoreService : ILauncherCoreService
    {
        private readonly LauncherSettingsService settingsService;
        private readonly LauncherStatusSnapshot snapshot;

        public SettingsBackedCoreService(
            LauncherSettingsService settingsService,
            LauncherStatusSnapshot snapshot)
        {
            this.settingsService = settingsService;
            this.snapshot = snapshot;
        }

        public async Task<LauncherStatusSnapshot> LoadAsync(CancellationToken cancellationToken = default)
        {
            var settings = await settingsService.ReadAsync(cancellationToken);
            snapshot.Settings = settings;
            snapshot.LocalGame = CopyLocalGameWithPath(snapshot.LocalGame, settings.GamePath);
            return snapshot;
        }
    }

    private sealed class CountingGameOperationsBackend :
        IGameLaunchWorkflow,
        IGameInstallationWorkflow,
        IGameUninstallWorkflow
    {
        private readonly bool isDownloadRunning;

        public CountingGameOperationsBackend(bool isDownloadRunning = false)
        {
            this.isDownloadRunning = isDownloadRunning;
        }

        public int ResumeInvocationCount { get; private set; }
        public int RepairInvocationCount { get; private set; }
        public int UninstallInvocationCount { get; private set; }
        public int StopInvocationCount { get; private set; }
        public GameOperationResult InstallResult { get; set; } = new();
        public GameOperationResult RepairResult { get; set; } = new();
        public GameOperationResult ValidateUninstallResult { get; set; } = new();
        public GameOperationResult UninstallResult { get; set; } = new();
        public bool IsDownloadRunning => isDownloadRunning;
        public bool IsRunning => IsDownloadRunning;
        public bool IsPaused => false;
        public event Action? IsRunningChanged { add { } remove { } }

        public Task<GameLaunchResult> StartGameAsync(LauncherStatusSnapshot snapshot) =>
            throw new NotSupportedException();

        public Task<GameOperationResult> InstallOrUpdateAsync(
            LauncherStatusSnapshot snapshot,
            Action<GameOperationProgress> progress,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(InstallResult);

        public Task<GameOperationResult> RepairAsync(
            LauncherStatusSnapshot snapshot,
            Action<GameOperationProgress> progress)
        {
            RepairInvocationCount++;
            return Task.FromResult(RepairResult);
        }

        public Task<GameOperationResult> ValidateUninstallAsync(string gamePath) =>
            Task.FromResult(ValidateUninstallResult);

        public Task<GameOperationResult> UninstallAsync(
            LauncherStatusSnapshot snapshot,
            Action<GameOperationProgress> progress)
        {
            UninstallInvocationCount++;
            return Task.FromResult(UninstallResult);
        }

        public Task<GameOperationResult?> ResumePersistedAsync(
            LauncherStatusSnapshot snapshot,
            Action<GameOperationProgress> progress,
            CancellationToken cancellationToken)
        {
            ResumeInvocationCount++;
            return Task.FromResult<GameOperationResult?>(null);
        }

        public void Stop(bool clearPersistedState)
        {
            StopInvocationCount++;
        }

        public void Pause()
        {
        }

        public void Resume()
        {
        }
    }

    private sealed class ThrowingCoreService : ILauncherCoreService
    {
        public Task<LauncherStatusSnapshot> LoadAsync(CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("load failed");
        }
    }

    private sealed class BlockingSecondLoadCoreService : ILauncherCoreService
    {
        private readonly LauncherStatusSnapshot snapshot;
        private int loadCount;

        public BlockingSecondLoadCoreService(LauncherStatusSnapshot snapshot)
        {
            this.snapshot = snapshot;
        }

        public TaskCompletionSource SecondLoadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseSecondLoad { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int LoadCount => Volatile.Read(ref loadCount);

        public async Task<LauncherStatusSnapshot> LoadAsync(CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref loadCount) == 2)
            {
                SecondLoadStarted.TrySetResult();
                await ReleaseSecondLoad.Task.WaitAsync(cancellationToken);
            }

            return snapshot;
        }
    }

    private sealed class SequencedBlockingCoreService(
        LauncherStatusSnapshot initial,
        LauncherStatusSnapshot older,
        LauncherStatusSnapshot newest) : ILauncherCoreService
    {
        private int loadCount;
        private int currentConcurrency;
        private int maximumConcurrency;

        public int LoadCount => Volatile.Read(ref loadCount);
        public int MaximumConcurrency => Volatile.Read(ref maximumConcurrency);

        public TaskCompletionSource SecondLoadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseSecondLoad { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<LauncherStatusSnapshot> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            var invocation = Interlocked.Increment(ref loadCount);
            var concurrency = Interlocked.Increment(ref currentConcurrency);
            UpdateMaximum(concurrency);
            try
            {
                if (invocation == 2)
                {
                    SecondLoadStarted.TrySetResult();
                    await ReleaseSecondLoad.Task.WaitAsync(cancellationToken);
                }

                return invocation switch
                {
                    1 => initial,
                    2 => older,
                    _ => newest
                };
            }
            finally
            {
                Interlocked.Decrement(ref currentConcurrency);
            }
        }

        private void UpdateMaximum(int value)
        {
            while (true)
            {
                var current = Volatile.Read(ref maximumConcurrency);
                if (current >= value
                    || Interlocked.CompareExchange(ref maximumConcurrency, value, current) == current)
                {
                    return;
                }
            }
        }
    }

    private sealed class ResourcePanelHandler : HttpMessageHandler
    {
        public int StatusListCount { get; private set; }
        public int ConfigGetCount { get; private set; }
        public int ConfigSetCount { get; private set; }
        public string LastRequestMethod { get; private set; } = "";
        public string LastRequestPathAndQuery { get; private set; } = "";
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestMethod = request.Method.Method;
            LastRequestPathAndQuery = request.RequestUri?.PathAndQuery ?? "";
            LastRequestBody = request.Content is not null ? await request.Content.ReadAsStringAsync(cancellationToken) : null;
            var path = request.RequestUri?.AbsolutePath ?? "";
            var json = "{}";
            if (path == "/status/list")
            {
                StatusListCount++;
                json = """
                {
                  "text": {
                    "official": { "version": "1.0.0" },
                    "localized": { "version": "1.0.0" }
                  },
                  "voice": {
                    "official": { "version": "2.0.0" },
                    "localized": { "version": "2.1.0" }
                  },
                  "media": {
                    "official": { "version": "3.0.0" },
                    "localized": { "version": "3.0.0" }
                  }
                }
                """;
            }
            else if (path == "/config/get")
            {
                ConfigGetCount++;
                json = """
                {
                  "text": "cn",
                  "voice": "jp",
                  "media": "cn"
                }
                """;
            }
            else if (request.RequestUri?.PathAndQuery == "/config/set?uid=UIDTESTA&text=cn&voice=jp&media=cn")
            {
                ConfigSetCount++;
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class LauncherUpdateHandler : HttpMessageHandler
    {
        private readonly string? responseJson;

        public LauncherUpdateHandler(string? responseJson = null)
        {
            this.responseJson = responseJson;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (responseJson is not null)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private static double GetPerceivedSaturation(Color color)
    {
        var max = Math.Max(color.R, Math.Max(color.G, color.B));
        var min = Math.Min(color.R, Math.Min(color.G, color.B));
        return max == 0 ? 0 : 1d - (min / (double)max);
    }

    private static double GetRelativeLuminance(Color color)
    {
        static double ToLinear(byte channel)
        {
            var value = channel / 255d;
            return value <= 0.04045
                ? value / 12.92
                : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * ToLinear(color.R))
            + (0.7152 * ToLinear(color.G))
            + (0.0722 * ToLinear(color.B));
    }
}
