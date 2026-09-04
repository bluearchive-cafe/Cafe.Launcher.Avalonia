using Avalonia.Media;
using Cafe.Launcher.Avalonia.Features.Settings;
using Cafe.Launcher.Avalonia.Features.SetupWizard;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.Services.GameRuntime;
using Cafe.Launcher.Avalonia.Testing;
using Cafe.Launcher.Avalonia.ViewModels;

namespace Cafe.Launcher.Avalonia.Tests;

public partial class MainWindowViewModelTests
{
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
    public async Task SaveSettingsAsync_WhenDownloadIsRunning_UpdatesCurrentSnapshotWithoutRefreshing()
    {
        var snapshot = CreateSnapshot();
        snapshot.Settings.PatchUrlGroup = PatchUrlGroups.Official;
        var settingsPath = Path.Combine(tempDir, Guid.NewGuid().ToString("N"), "settings.json");
        var settingsService = new LauncherSettingsService(settingsPath);
        await settingsService.SaveAsync(new LauncherSettings
        {
            GamePath = snapshot.Settings.GamePath,
            PatchUrlGroup = PatchUrlGroups.Official
        });
        var coreService = new CountingCoreService(snapshot);
        using var viewModel = await CreateViewModelAsync(
            coreService,
            settingsService,
            gameOperationsBackend: new StubGameOperationExecutor { IsDownloadRunning = true });
        await viewModel.InitializeAsync();

        viewModel.Settings.Editor.Current.PatchUrlGroup = PatchUrlGroups.Cafe;
        await SaveSettingsAsync(viewModel);

        Assert.Equal(1, coreService.LoadCount);
        Assert.Equal(PatchUrlGroups.Cafe, snapshot.Settings.PatchUrlGroup);
        Assert.False(viewModel.Dialogs.IsRepairConfirmVisible);
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
        var filePicker = new StubFilePickerService
        {
            FolderPicker = (_, _) => Task.FromResult<string?>(pickedPath)
        };
        using var viewModel = await CreateViewModelAsync(coreService, settingsService, filePickerService: filePicker);
        await viewModel.InitializeAsync();
        viewModel.WindowChrome.IsSettingsVisible = true;

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
        var filePicker = new StubFilePickerService
        {
            FolderPicker = (_, _) => Task.FromResult<string?>(selectedRoot)
        };
        using var viewModel = await CreateViewModelAsync(coreService, settingsService, filePickerService: filePicker);
        await viewModel.InitializeAsync();
        viewModel.Settings.Editor.Current.ThemeMode = ThemeModes.Dark;

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
        var filePicker = new StubFilePickerService
        {
            ImagePicker = _ => Task.FromResult<string?>(pickedPath)
        };
        using var viewModel = await CreateViewModelAsync(coreService, settingsService, filePickerService: filePicker);
        await viewModel.InitializeAsync();

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
        var filePicker = new StubFilePickerService
        {
            FolderPicker = (_, _) => Task.FromResult<string?>(pickedFolder)
        };
        using var viewModel = await CreateViewModelAsync(coreService, settingsService, filePickerService: filePicker);
        await viewModel.InitializeAsync();
        viewModel.Settings.Editor.ApplySnapshot(persistedSettings);
        viewModel.Settings.Appearance.Load(persistedSettings);

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
            new SetupWizardViewModel(localizer, new GameInstallationPath(), new LocalInstallationStateStore(), new LocalDiagnostics(), new StubFilePickerService()));
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
            new ErrorHandlingService(localizer, new LocalDiagnostics(testLogger), toastService),
            new GameRuntime([], new DefaultProcessLauncher(), new GameProcessTracker()), new StubFilePickerService());
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
}
