using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Services;
using Cafe.Launcher.Core.Services.Diagnostics;
using Cafe.Launcher.Core.Services.GameRuntime;
using Cafe.Launcher.UI.Constants;
using Cafe.Launcher.UI.Helpers;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.UI.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog.Events;

namespace Cafe.Launcher.UI.Features.Settings;

internal partial class SettingsViewModel : ObservableObject, IDisposable, IModalContentViewModel, ILanguageAwarePresentation
{
    private readonly ILauncherSettingsService settingsService;
    private readonly ISavedSettingsWriter savedSettingsWriter;
    private readonly LocalizationService localizer;
    private readonly ToastService toastService;
    private readonly ILauncherUpdateService launcherUpdateService;
    private readonly ILauncherUpdatePresenter updatePresenter;
    private readonly DialogsViewModel dialogs;
    private readonly SettingsEditor editor;
    private readonly UnifiedLogger unifiedLogger;
    private readonly IGameInstallationPath gameInstallationPath;
    private readonly IErrorHandlingService errorHandling;
    private readonly IFilePickerService filePickerService;
    private readonly LatestRefresh appearancePreviewRefresh = new();
    private bool disposed;

    /// <summary>
    /// 保存前等待在途外观预览落定的总预算。预览链路含远端图下载（受 HttpClient
    /// 默认超时约束）属有界等待，但仍设上限防止未来加入无超时操作后保存被无限挂起；
    /// 超时后按当前状态继续保存。测试可调小。
    /// </summary>
    internal static TimeSpan AppearancePreviewSettleTimeout = TimeSpan.FromMinutes(2);

    // Coordination seams — set by parent after construction.
    public Func<LauncherSettings, string?, CancellationToken, Task>? PreviewAppearanceAsync { get; set; }

    /// <summary>Applies the effects that follow a persisted settings snapshot (shell-owned).</summary>
    public ISavedSettingsFollowThrough? SavedSettingsFollowThrough { get; set; }

    /// <summary>
    /// The settings state editor. XAML binds to <c>Editor.Current.*</c> for
    /// setting values, and to ViewModel properties for option collections and UI state.
    /// </summary>
    public SettingsEditor Editor => editor;
    public SettingsOptionsViewModel Options { get; }
    public SettingsAppearanceViewModel Appearance { get; }

    /// <summary>游戏分类的运行时可用性流程；状态探测、构建发现与运行器路径选择都在其内部收口。</summary>
    public SettingsGameRuntimeViewModel GameRuntime { get; }

    public SettingsViewModel(
        ILauncherSettingsService settingsService,
        ISavedSettingsWriter savedSettingsWriter,
        LocalizationService localizer,
        ToastService toastService,
        ILauncherUpdateService launcherUpdateService,
        ILauncherUpdatePresenter updatePresenter,
        DialogsViewModel dialogs,
        UnifiedLogger unifiedLogger,
        IGameInstallationPath gameInstallationPath,
        SettingsOptionsViewModel options,
        SettingsAppearanceViewModel appearance,
        IErrorHandlingService errorHandling,
        IGameRuntime gameRuntime,
        IFilePickerService filePickerService,
        IProtonBuildDiscovery protonBuildDiscovery)
    {
        this.settingsService = settingsService;
        this.savedSettingsWriter = savedSettingsWriter;
        this.localizer = localizer;
        this.toastService = toastService;
        this.launcherUpdateService = launcherUpdateService;
        this.updatePresenter = updatePresenter;
        this.dialogs = dialogs;
        this.unifiedLogger = unifiedLogger;
        this.gameInstallationPath = gameInstallationPath;
        editor = appearance.Editor;
        Options = options;
        Appearance = appearance;
        this.errorHandling = errorHandling;
        this.filePickerService = filePickerService;
        GameRuntime = new SettingsGameRuntimeViewModel(
            editor,
            options,
            localizer,
            gameRuntime,
            filePickerService,
            protonBuildDiscovery);
        editor.PropertyChanged += OnEditorPropertyChanged;
        editor.CurrentPropertyChanged += OnCurrentSettingChanged;
    }

    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsEditor.IsDirty))
        {
            OnPropertyChanged(nameof(IsSettingsDirty));
            OnPropertyChanged(nameof(CanSaveSettings));
            OnPropertyChanged(nameof(SaveStatusText));
            SaveSettingsCommand.NotifyCanExecuteChanged();
        }

        if (e.PropertyName == nameof(SettingsEditor.Current))
        {
            // Current 实例会被整体替换（保存后草稿换成归一化值），展示值须随之重算。
            OnPropertyChanged(nameof(GamePathDisplay));
            OnPropertyChanged(nameof(HasGamePathChanges));
        }
    }

    private void OnCurrentSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LauncherSettings.GamePath))
        {
            OnPropertyChanged(nameof(GamePathDisplay));
            OnPropertyChanged(nameof(HasGamePathChanges));
        }

        if (!IsAppearanceSetting(e.PropertyName))
        {
            return;
        }

        RequestAppearancePreview(e.PropertyName);
    }

    // ── Settings UI state ────────────────────────────────────────────────

    public bool IsSettingsDirty => editor.IsDirty;

    public bool CanSaveSettings => IsSettingsDirty && !IsSaving;

    /// <summary>Localized action label; saving is visible even while the button is disabled.</summary>
    public string SaveButtonText => localizer.T(IsSaving ? LocalizationKeys.SettingsSaving : LocalizationKeys.Save);

    /// <summary>One status for the shared draft across all categories, with saving taking precedence.</summary>
    public string SaveStatusText => IsSaving
        ? localizer.T(LocalizationKeys.SettingsSaving)
        : IsSettingsDirty ? localizer.T(LocalizationKeys.UnsavedChanges) : string.Empty;

    /// <summary>
    /// 游戏路径行的展示值：长路径做段感知中间省略（见 <see cref="PathMiddleEllipsis"/>），
    /// 保住盘符与末段目录名；草稿真值仍是 <c>Editor.Current.GamePath</c>。
    /// </summary>
    public string GamePathDisplay => PathMiddleEllipsis.MiddleEllipsize(editor.Current.GamePath);

    /// <summary>Maintenance always targets the applied installation, so it waits for path edits to settle.</summary>
    public bool HasGamePathChanges => !string.Equals(editor.Current.GamePath,
        editor.GetSavedSnapshot().GamePath, StringComparison.Ordinal);

    [ObservableProperty]
    private bool isUnsavedChangesVisible;

    [ObservableProperty]
    private bool isSaving;

    private string selectedCategory = SettingsCategoryCodes.General;

    public string SelectedCategory
    {
        get => selectedCategory;
        set
        {
            if (!SetProperty(ref selectedCategory, SettingsCategoryCodes.Normalize(value)))
            {
                return;
            }

            OnPropertyChanged(nameof(IsGeneralCategorySelected));
            OnPropertyChanged(nameof(IsGameCategorySelected));
            OnPropertyChanged(nameof(IsDownloadNetworkCategorySelected));
            OnPropertyChanged(nameof(IsAppearanceCategorySelected));
            OnPropertyChanged(nameof(IsAdvancedCategorySelected));
            OnPropertyChanged(nameof(IsAboutCategorySelected));
            if (selectedCategory == SettingsCategoryCodes.Game)
            {
                GameRuntime.RefreshStatus();
            }
        }
    }

    public bool IsGeneralCategorySelected => SelectedCategory == SettingsCategoryCodes.General;
    public bool IsGameCategorySelected => SelectedCategory == SettingsCategoryCodes.Game;
    public bool IsDownloadNetworkCategorySelected => SelectedCategory == SettingsCategoryCodes.DownloadNetwork;
    public bool IsAppearanceCategorySelected => SelectedCategory == SettingsCategoryCodes.Appearance;
    public bool IsAdvancedCategorySelected => SelectedCategory == SettingsCategoryCodes.Advanced;
    public bool IsAboutCategorySelected => SelectedCategory == SettingsCategoryCodes.About;

    internal Task PendingAppearancePreview => appearancePreviewRefresh.Pending;

    // ── Public API for parent VM ──────────────────────────────────────────

    /// <summary>Called by parent when settings panel opens or discards changes.</summary>
    public void LoadFromSnapshot(LauncherSettings settings)
    {
        Appearance.ResetSectionState();
        var currentWallpaperPalette = settings.ThemeColorMode == ThemeColorModes.Wallpaper
            ? Appearance.GetThemeColorPaletteHexes()
            : [];
        var currentWallpaperPaletteIndex = Appearance.SelectedThemeColorPaletteIndex;

        editor.ApplySnapshot(settings);
        Appearance.Load(settings);

        if (editor.Current.ThemeColorMode == ThemeColorModes.Wallpaper)
        {
            if (currentWallpaperPalette.Count > 0)
            {
                var snapshot = editor.GetSnapshot();
                snapshot.ThemeColorPalette = currentWallpaperPalette;
                snapshot.SelectedThemeColorPaletteIndex = currentWallpaperPaletteIndex;
                Appearance.Load(snapshot);
            }
            else
            {
                // 后台取色；色板就绪前外观页暂显示空色板，就绪后自动填充。
                _ = Appearance.RefreshThemeColorPaletteFromCurrentBackgroundAsync(markDirty: false);
            }
        }
        else
        {
            // Preview the wallpaper source even before it is selected, without editing the draft.
            _ = Appearance.RefreshThemeColorPaletteFromCurrentBackgroundAsync(markDirty: false);
        }

        GameRuntime.RefreshStatus();
    }

    /// <inheritdoc cref="ILanguageAwarePresentation.RefreshLocalizedText"/>
    public void RefreshLocalizedText() => RefreshOptionDisplayNames();

    /// <summary>Called by parent ApplyLanguage to refresh display names.</summary>
    public void RefreshOptionDisplayNames()
    {
        Options.RefreshDisplayNames();
        OnPropertyChanged(nameof(SaveButtonText));
        OnPropertyChanged(nameof(SaveStatusText));
        GameRuntime.RefreshLocalizedText();
    }

    /// <summary>Loads a persisted settings snapshot into the active edit session.</summary>
    public void ApplyLauncherSettings(LauncherSettings settings)
    {
        editor.ApplySnapshot(settings);
        var snapshot = editor.GetSnapshot();
        Appearance.Load(snapshot);
        ApplyLogLevel(snapshot.LogLevel);
    }

    // ── Commands ──────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        var savedSettings = editor.GetSavedSnapshot();
        var result = await launcherUpdateService.CheckForUpdateAsync(
            savedSettings.UpdateChannel);

        await updatePresenter.PresentAsync(result, LauncherUpdateTrigger.Manual);
    }

    /// <summary>Opens the shared launcher-settings reset confirmation (shell performs the reset).</summary>
    [RelayCommand]
    private void RequestResetSettings() => dialogs.SettingsResetConfirm.Show();

    [RelayCommand(CanExecute = nameof(CanSaveSettings))]
    private async Task SaveSettingsAsync()
    {
        LauncherSettings settings;
        IsSaving = true;
        try
        {
            // 先让当前设置快照对应的壁纸预览完成；若此处先取消，新的壁纸可能尚未
            // 落入 BackgroundViewModel，随后取色会错误地读取上一张壁纸。
            await WaitForAppearancePreviewToSettleAsync();
            CancelAppearancePreview();

            // 提交前确保色板对应当前壁纸（等待与补提取的判据在 Appearance 侧收拢）。
            await Appearance.EnsureThemePaletteReadyForSaveAsync();

            editor.Commit(s =>
            {
                s.ThemeColorPalette = Appearance.GetThemeColorPaletteHexes();
                s.SelectedThemeColorPaletteIndex = Appearance.SelectedThemeColorPaletteIndex;
            });

            // 落盘值而非草稿对象：归一化会改写草稿里的表示（大小写、trim、色板去重），
            // 后续的跟随动作与编辑器都必须以真正落盘的那个值为准。
            settings = await savedSettingsWriter.SaveDraftAsync();
        }
        catch (Exception exception)
        {
            await errorHandling.HandleErrorAsync("Settings save failed.", exception,
                new ErrorHandlingOptions { ToastMessage = localizer.F(LocalizationKeys.SettingsSaveFailed, exception.Message) });
            return;
        }
        finally
        {
            // 保存态以落盘为界（AUD-SET-003）：之后的派生效果可能包含整段壳刷新，
            // 不能继续把页脚留在「正在保存」并锁住导航、关闭与编辑。
            IsSaving = false;
        }

        await ApplySavedSettingsAsync(settings);
    }

    /// <summary>
    /// 落盘后的成功反馈与派生效果（ADR-052）。它不属于保存态：值已经写进磁盘，
    /// 这里的失败不能再报「保存失败」。
    /// </summary>
    private async Task ApplySavedSettingsAsync(LauncherSettings settings)
    {
        var followThrough = SavedSettingsFollowThrough;
        var handoff = followThrough?.Apply(settings);

        toastService.ShowSuccess(localizer.T(LocalizationKeys.SettingsSaved));
        GameRuntime.RefreshStatus();

        if (followThrough is null || handoff is null)
        {
            return;
        }

        try
        {
            await followThrough.CompleteAsync(handoff);
        }
        catch (Exception exception)
        {
            // 只留诊断：值已落盘，再弹「保存失败」会与已显示的成功提示自相矛盾。
            await errorHandling.HandleErrorAsync(
                "Settings follow-through failed.",
                exception,
                new ErrorHandlingOptions { ShowToast = false });
        }
    }

    [RelayCommand]
    private async Task ChooseGamePathAsync()
    {
        var pickedPath = await filePickerService.PickFolderAsync(
            localizer.T(LocalizationKeys.ChooseInstallFolder),
            editor.Current.GamePath);
        if (string.IsNullOrWhiteSpace(pickedPath))
        {
            return;
        }

        // Normalise: append YostarGames/BlueArchive_JP subdirectory if missing,
        // matching the original Yostar launcher behaviour.
        editor.Current.GamePath = gameInstallationPath.NormalizeGamePath(pickedPath);
    }

    /// <summary>Chooses an installation draft without changing saved settings.</summary>
    public async Task<string?> PickInstallationDraftAsync(string startPath)
    {
        var picked = await filePickerService.PickFolderAsync(localizer.T(LocalizationKeys.ChooseInstallFolder), startPath);
        return string.IsNullOrWhiteSpace(picked) ? null : gameInstallationPath.NormalizeGamePath(picked);
    }

    /// <summary>Commits an explicit install/identify action through the saved-settings follow-through.</summary>
    public async Task<bool> CommitInstallationPathAsync(string path)
    {
        try
        {
            var saved = await savedSettingsWriter.UpdateAsync(settings => settings.GamePath = gameInstallationPath.NormalizeGamePath(path));
            if (SavedSettingsFollowThrough is { } followThrough)
                await followThrough.CompleteAsync(followThrough.Apply(saved));
            return true;
        }
        catch (Exception exception)
        {
            await errorHandling.HandleErrorAsync("Settings game path update failed.", exception,
                new ErrorHandlingOptions { ToastMessage = localizer.F(LocalizationKeys.GamePathUpdateFailed, exception.Message) });
            return false;
        }
    }

    [RelayCommand]
    private async Task ChangePersistedGamePathAsync()
    {
        var settings = await settingsService.ReadAsync();
        await PickAndPersistGamePathAsync(settings.GamePath);
    }

    [RelayCommand]
    private async Task SelectInstalledGameAsync()
    {
        var startPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        await PickAndPersistGamePathAsync(startPath);
    }

    private async Task PickAndPersistGamePathAsync(string startPath)
    {
        var pickedPath = await filePickerService.PickFolderAsync(
            localizer.T(LocalizationKeys.ChooseInstallFolder),
            startPath);
        if (string.IsNullOrWhiteSpace(pickedPath))
        {
            return;
        }

        // Normalise: append YostarGames/BlueArchive_JP if missing.
        pickedPath = gameInstallationPath.NormalizeGamePath(pickedPath);

        try
        {
            var savedSettings = await savedSettingsWriter.UpdateAsync(
                settings => settings.GamePath = pickedPath);
            var followThrough = SavedSettingsFollowThrough;
            var handoff = followThrough?.Apply(savedSettings);

            toastService.ShowSuccess(localizer.T(LocalizationKeys.GamePathUpdated));

            if (followThrough is not null && handoff is not null)
            {
                await followThrough.CompleteAsync(handoff);
            }
        }
        catch (Exception exception)
        {
            await errorHandling.HandleErrorAsync("Settings game path update failed.", exception,
                new ErrorHandlingOptions { ToastMessage = localizer.F(LocalizationKeys.GamePathUpdateFailed, exception.Message) });
        }
    }

    [RelayCommand]
    private async Task ChooseBackgroundFileAsync()
    {
        var pickedPath = await filePickerService.PickBackgroundFileAsync(
            localizer.T(LocalizationKeys.ChooseBackgroundFileTitle),
            localizer.T(LocalizationKeys.BackgroundFileTypes));
        if (string.IsNullOrWhiteSpace(pickedPath))
            return;

        editor.Current.CustomBackgroundPath = pickedPath;
        editor.Current.BackgroundSource = BackgroundSources.Custom;
    }

    [RelayCommand]
    private async Task ChooseBackgroundFolderAsync()
    {
        var pickedPath = await filePickerService.PickFolderAsync(
            localizer.T(LocalizationKeys.ChooseBackgroundFolderTitle));
        if (string.IsNullOrWhiteSpace(pickedPath))
            return;

        editor.Current.CustomBackgroundPath = pickedPath;
        editor.Current.BackgroundSource = BackgroundSources.Custom;
    }

    [RelayCommand]
    private void ClearBackground()
    {
        editor.Current.CustomBackgroundPath = "";
        editor.Current.BackgroundSource = BackgroundSources.Bundled;
    }

    public async Task DiscardChangesAsync()
    {
        IsUnsavedChangesVisible = false;
        CancelAppearancePreview();
        editor.Discard();
        GameRuntime.RefreshStatus();
        Appearance.Load(editor.Current);
        appearancePreviewRefresh.Run(null, token => PreviewCurrentAppearanceAsync(null, token));
        await appearancePreviewRefresh.Pending;
    }

    public void KeepEditing()
    {
        IsUnsavedChangesVisible = false;
    }

    private void RequestAppearancePreview(string? propertyName)
    {
        appearancePreviewRefresh.Run(null, token => PreviewCurrentAppearanceAsync(propertyName, token));
    }

    private void CancelAppearancePreview() => appearancePreviewRefresh.Cancel();

    private Task WaitForAppearancePreviewToSettleAsync() =>
        TaskSettler.WaitAsync(() => appearancePreviewRefresh.Pending, AppearancePreviewSettleTimeout);

    private async Task PreviewCurrentAppearanceAsync(
        string? propertyName,
        CancellationToken cancellationToken)
    {
        if (PreviewAppearanceAsync is null)
        {
            return;
        }

        try
        {
            await PreviewAppearanceAsync(editor.GetSnapshot(), propertyName, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await errorHandling.HandleErrorAsync("Settings appearance preview failed.", exception,
                new ErrorHandlingOptions { ToastMessage = localizer.F(LocalizationKeys.AppearancePreviewFailed, exception.Message) });
        }
    }

    private static bool IsAppearanceSetting(string? propertyName) =>
        propertyName is nameof(LauncherSettings.ThemeMode)
            or nameof(LauncherSettings.ThemeColorMode)
            or nameof(LauncherSettings.ThemeColorExtractionAlgorithm)
            or nameof(LauncherSettings.ThemeColorVariant)
            or nameof(LauncherSettings.NeutralColorStrategy)
            or nameof(LauncherSettings.CustomThemeColor)
            or nameof(LauncherSettings.ThemeColorPalette)
            or nameof(LauncherSettings.SelectedThemeColorPaletteIndex)
            or nameof(LauncherSettings.BackgroundSource)
            or nameof(LauncherSettings.CustomBackgroundPath)
            or nameof(LauncherSettings.BackgroundFit)
            or nameof(LauncherSettings.BackgroundFillColor)
            or nameof(LauncherSettings.BackgroundVideoVolume)
            or nameof(LauncherSettings.BackgroundVideoMuted);

    partial void OnIsSavingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanSaveSettings));
        OnPropertyChanged(nameof(SaveButtonText));
        OnPropertyChanged(nameof(SaveStatusText));
        SaveSettingsCommand.NotifyCanExecuteChanged();
    }

    internal void ApplyLogLevel(string logLevelCode)
    {
        try
        {
            var level = logLevelCode switch
            {
                LogLevels.Verbose => LogEventLevel.Verbose,
                LogLevels.Debug => LogEventLevel.Debug,
                LogLevels.Warning => LogEventLevel.Warning,
                LogLevels.Error => LogEventLevel.Error,
                LogLevels.Fatal => LogEventLevel.Fatal,
                _ => LogEventLevel.Information
            };
            unifiedLogger.SetMinimumLevel(level);
        }
        catch (Exception ex)
        {
            // 豁免：日志级别应用是 best-effort，绝不能反噬设置流程。
            System.Diagnostics.Debug.WriteLine(
                $"Settings: failed to apply log level: {ex.Message}");
            // Best-effort — log level application must never disrupt settings flow.
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        CancelAppearancePreview();
        GameRuntime.Dispose();
        editor.PropertyChanged -= OnEditorPropertyChanged;
        editor.CurrentPropertyChanged -= OnCurrentSettingChanged;
        Appearance.Dispose();
    }
}
