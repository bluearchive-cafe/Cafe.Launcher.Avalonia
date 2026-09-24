using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Features.Diagnostics;
using Cafe.Launcher.Avalonia.Features.GameOperations;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Features.ResourcePanel;
using Cafe.Launcher.Avalonia.Features.Settings;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.Services.Update;
using Cafe.Launcher.Avalonia.ViewModels;

namespace Cafe.Launcher.Avalonia.Features.Shell;

/// <summary>
/// Owns shell startup, refresh, settings-save, first-run wizard completion,
/// resource-panel switching, and every cross-feature subscription, and maps the
/// in-app self-update state machine (<see cref="ShellSelfUpdateCoordinator"/>) onto
/// dialogs, toasts, and shutdown.
/// The window (MainWindowViewModel) only presents shell state.
/// </summary>
public sealed class ShellLifecycle : IDisposable
{
    /// <summary>Raised when shell presentation state changes.</summary>
    public event Action? PresentationChanged;

    /// <summary>Raised after a saved status-detail setting changes the shell presentation mode.</summary>
    public event Action? StatusDetailModeChanged;

    /// <summary>Gets the modal host coordinated by this shell lifecycle.</summary>
    public ModalHostViewModel ModalHost { get; }

    private readonly ILauncherCoreService launcherCoreService;
    private readonly LauncherSettingsService settingsService;
    private readonly ISavedSettingsWriter savedSettingsWriter;
    private readonly LocalizationService localizer;
    private readonly ToastService toastService;
    private readonly LauncherUpdateService launcherUpdateService;
    private readonly LauncherSelfUpdateService launcherSelfUpdateService;
    private readonly IWindowsLauncherUpdateApplier launcherUpdateApplier;
    private readonly LocalDiagnostics diagnostics;
    private readonly IErrorHandlingService errorHandling;
    private readonly SystemAnimationSettingsProvider systemAnimationSettingsProvider;
    private readonly ShellViewModel shell;
    private readonly BackgroundViewModel background;
    private readonly RemoteContentViewModel remoteContent;
    private readonly DialogsViewModel dialogs;
    private readonly GameOperationsViewModel operations;
    private readonly ToastHostViewModel toasts;
    private readonly WindowChromeViewModel windowChrome;
    private readonly SettingsViewModel settings;
    private readonly ResourcePanelViewModel resourcePanel;
    private readonly LogViewerDialogViewModel logViewer;
    private readonly LogExportDialogViewModel logExport;
    private readonly DebugViewModel debug;

    /// <summary>
    /// 语言变化时的刷新名单（D10）：呈现族里实现 <see cref="ILanguageAwarePresentation"/>
    /// 的成员，顺序即迁移前 ApplyLanguage 的扇出顺序。向导不在族里，经它的宿主
    /// DialogsViewModel 一并刷新。
    /// </summary>
    private readonly IReadOnlyList<ILanguageAwarePresentation> languageAwarePresentations;
    private readonly bool ownsPresentationCollaborators;
    private readonly ShellRefreshCoordinator refreshCoordinator;
    private readonly IFilePickerService filePickerService;
    private readonly ShellStartup startup;
    private readonly ModalRegistrar modalRegistrar;

    /// <summary>
    /// 退订记录表：Wire 的每条接线经 <see cref="Attach"/> 配对登记，Unwire 逆序执行
    /// 后清空——两份手抄订阅清单由此收敛为一份（R2-c07／D15／AUD-ARCH-005）。
    /// </summary>
    private readonly List<Action> detachers = [];
    private bool disposed;
    private bool isBusy;
    private bool isMotionReduced = true;
    private bool motionSettingsApplied;
    private bool settingsSnapshotInitialized;
    private LauncherStatusSnapshot? currentSnapshot;
    private bool isWired;
    private readonly ShellSelfUpdateCoordinator selfUpdateCoordinator;

    /// <summary>Gets the active startup update check so tests can coordinate without timing delays.</summary>
    public Task PendingStartupUpdateCheck => refreshCoordinator.PendingAfterLoadWork;

    /// <summary>Initializes shell lifecycle dependencies and subscribes error handling callbacks.</summary>
    public ShellLifecycle(
        ILauncherCoreService launcherCoreService,
        LauncherSettingsService settingsService,
        ISavedSettingsWriter savedSettingsWriter,
        LocalizationService localizer,
        ToastService toastService,
        LauncherUpdateService launcherUpdateService,
        LauncherSelfUpdateService launcherSelfUpdateService,
        IWindowsLauncherUpdateApplier launcherUpdateApplier,
        LocalDiagnostics diagnostics,
        IErrorHandlingService errorHandling,
        SystemAnimationSettingsProvider systemAnimationSettingsProvider,
        ShellPresentationFamily family,
        IFilePickerService filePickerService)
        : this(
            launcherCoreService,
            settingsService,
            savedSettingsWriter,
            localizer,
            toastService,
            launcherUpdateService,
            launcherSelfUpdateService,
            launcherUpdateApplier,
            diagnostics,
            errorHandling,
            systemAnimationSettingsProvider,
            family,
            filePickerService,
            ownsPresentationCollaborators: false)
    {
    }

    internal ShellLifecycle(
        ILauncherCoreService launcherCoreService,
        LauncherSettingsService settingsService,
        ISavedSettingsWriter savedSettingsWriter,
        LocalizationService localizer,
        ToastService toastService,
        LauncherUpdateService launcherUpdateService,
        LauncherSelfUpdateService launcherSelfUpdateService,
        IWindowsLauncherUpdateApplier launcherUpdateApplier,
        LocalDiagnostics diagnostics,
        IErrorHandlingService errorHandling,
        SystemAnimationSettingsProvider systemAnimationSettingsProvider,
        ShellPresentationFamily family,
        IFilePickerService filePickerService,
        bool ownsPresentationCollaborators)
    {
        // 所有权制度按构造路径分叉（受控测试缝，见 AUD-ARCH-003）：
        // - 生产 DI 路径走公开构造，ownsPresentationCollaborators: false——展示 VM 家族由
        //   DI 组合根（全 Singleton）持有并释放，Dispose 不得触碰它们。
        // - 测试路径经 MainWindowViewModel 的 internal 构造以 owns: true 创建，替身展示
        //   VM 无容器持有，由 ShellLifecycle.Dispose 统一释放。两种制度下 Dispose 的释放
        //   范围不同；与释放顺序相关的回归在测试中不可复现，收敛该分叉需先让测试路径
        //   显式管理替身生命周期（已裁定当前缝可接受，故仅在此书面记录差异）。
        this.filePickerService = filePickerService;
        this.launcherCoreService = launcherCoreService;
        this.settingsService = settingsService;
        this.savedSettingsWriter = savedSettingsWriter;
        this.localizer = localizer;
        this.toastService = toastService;
        this.launcherUpdateService = launcherUpdateService;
        this.launcherSelfUpdateService = launcherSelfUpdateService;
        this.launcherUpdateApplier = launcherUpdateApplier;
        this.diagnostics = diagnostics;
        this.errorHandling = errorHandling;
        this.systemAnimationSettingsProvider = systemAnimationSettingsProvider;
        shell = family.Shell;
        background = family.Background;
        remoteContent = family.RemoteContent;
        dialogs = family.Dialogs;
        operations = family.Operations;
        toasts = family.Toasts;
        windowChrome = family.WindowChrome;
        settings = family.Settings;
        resourcePanel = family.ResourcePanel;
        logViewer = family.LogViewer;
        logExport = family.LogExport;
        debug = family.Debug;
        languageAwarePresentations =
        [
            settings,
            resourcePanel,
            remoteContent,
            dialogs,
            operations,
            debug,
            logExport
        ];
        shell.LanguageAwarePresentations = languageAwarePresentations;
        this.ownsPresentationCollaborators = ownsPresentationCollaborators;
        ModalHost = family.ModalHost;
        modalRegistrar = new ModalRegistrar(ModalHost);

        errorHandling.CriticalErrorRequested += OnCriticalError;
        localizer.LocalizationFailure += OnLocalizationFailure;
        refreshCoordinator = new ShellRefreshCoordinator(
            LoadHostStateAsync,
            AfterLoadAsync);
        selfUpdateCoordinator = new ShellSelfUpdateCoordinator(
            launcherSelfUpdateService,
            launcherUpdateApplier,
            refreshCoordinator.LifetimeToken);
        selfUpdateCoordinator.PreparationStarted += OnSelfUpdatePreparationStarted;
        selfUpdateCoordinator.ProgressReported += OnSelfUpdateProgressReported;
        selfUpdateCoordinator.Finished += OnSelfUpdateFinished;
        startup = new ShellStartup(
            RefreshAsync,
            ApplyMotionSettings,
            ApplyLanguage,
            settings => savedSettingsWriter.ReplaceAsync(settings),
            () => dialogs.IsSetupWizardVisible = false,
            () => dialogs.IsSetupWizardVisible,
            dialogs.SetupWizard);

        Wire();
        ApplyInitialLanguage();
    }

    /// <summary>Gets whether the shell is currently processing an operation.</summary>
    public bool IsBusy => isBusy;

    /// <summary>Gets whether reduced motion is currently effective.</summary>
    public bool IsMotionReduced => isMotionReduced;

    /// <summary>Initializes the shell once by loading settings and launcher state.</summary>
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        // 上一次自更新残留在 %TEMP% 的 helper 副本此刻没有属主(helper 只在主
        // 进程退出后短暂存活),启动时清掉;仍在运行的例外靠文件锁幸免。
        launcherUpdateApplier.CleanupAbandonedHelperDirectories();
        return startup.InitializeAsync(cancellationToken);
    }

    /// <summary>Reapplies the system motion preference when the user chose the system option.</summary>
    public void RefreshSystemMotionPreference()
    {
        if (!settingsSnapshotInitialized)
        {
            return;
        }

        var savedSettings = settings.Editor.GetSavedSnapshot();
        if (savedSettings.MotionMode != MotionModes.System)
        {
            return;
        }

        ApplyMotionSettings(savedSettings);
    }

    /// <summary>Applies the initial automatic language before a launcher snapshot exists.</summary>
    public void ApplyInitialLanguage() => startup.ApplyInitialLanguage();

    /// <summary>
    /// 首启分支的动效偏好由 <see cref="ShellStartup"/> 在向导显示前按默认配置应用,
    /// 参见该模块的规则说明。
    /// </summary>
    public void ApplyFirstLaunchMotionPreference() =>
        startup.ApplyFirstLaunchMotionPreference();

    /// <summary>Reloads launcher state and updates all dependent presentation models.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await refreshCoordinator.RefreshAsync(resumePersistedDownload: true, cancellationToken);
    }

    /// <summary>
    /// Loads settings and launcher state under the refresh gate and applies them
    /// to every dependent presentation model. Returns whether a snapshot loaded.
    /// </summary>
    private async Task<bool> LoadHostStateAsync(CancellationToken refreshToken)
    {
        SetPresentationState(ref isBusy, true);
        shell.IsBusy = true;
        try
        {
            var settingsForLanguage = await settingsService.ReadAsync(refreshToken);
            refreshToken.ThrowIfCancellationRequested();
            settings.Editor.ApplySnapshot(settingsForLanguage);
            settingsSnapshotInitialized = true;
            ApplyMotionSettings(settingsForLanguage);
            ApplyLanguage(settingsForLanguage.Language);
            settings.Appearance.Load(settingsForLanguage);
            settings.Appearance.ApplyFrom(settingsForLanguage);
            shell.SetLoading();
            remoteContent.BeginLoading(settingsForLanguage.ShowRemoteContentCard);

            var snapshot = await launcherCoreService.LoadAsync(refreshToken);
            refreshToken.ThrowIfCancellationRequested();
            currentSnapshot = snapshot;
            await ApplySnapshotAsync(snapshot);
            refreshToken.ThrowIfCancellationRequested();
            return true;
        }
        catch (OperationCanceledException) when (refreshToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            shell.SetRefreshError(exception, settings);
            operations.SetIdlePanels(currentSnapshot);
            await errorHandling.HandleErrorAsync("Launcher core refresh failed.", exception,
                new ErrorHandlingOptions { ToastMessage = localizer.F(LocalizationKeys.LauncherCoreRefreshFailed, exception.Message) });
            return false;
        }
        finally
        {
            if (!disposed)
            {
                remoteContent.EndLoading();
                shell.IsBusy = false;
                SetPresentationState(ref isBusy, false);
            }
        }
    }

    /// <summary>Runs the after-load work and returns the startup update check task.</summary>
    private async Task<Task> AfterLoadAsync(bool resumePersistedDownload, CancellationToken refreshToken)
    {
        if (resumePersistedDownload)
        {
            await operations.ResumePersistedDownloadAsync(refreshToken);
        }

        if (settings.Editor.GetSavedSnapshot().EnableStartupUpdateCheck)
        {
            return CheckForStartupUpdateAsync(refreshCoordinator.LifetimeToken);
        }

        return Task.CompletedTask;
    }

    /// <summary>Cancels lifecycle work and waits until every active refresh has finished.</summary>
    public async Task PrepareForShutdownAsync()
    {
        Task pendingRefreshes = refreshCoordinator.BeginShutdown();
        refreshCoordinator.CancelLifetime();
        operations.StopOperation(GameOperationStopIntent.ProcessExit);
        await refreshCoordinator.WaitForShutdownWorkAsync(pendingRefreshes);
    }

    /// <summary>Applies the follow-through of one saved settings snapshot.</summary>
    public async Task HandleSettingsSavedAsync()
    {
        var savedSettings = settings.Editor.Current;
        var previousPatchUrlGroup = currentSnapshot?.Settings.PatchUrlGroup;
        remoteContent.UpdateRemoteContentVisibility(savedSettings.ShowRemoteContentCard);
        ApplyMotionSettings(savedSettings);

        if (operations.IsDownloadRunning)
        {
            if (currentSnapshot is not null)
            {
                currentSnapshot.Settings = await settingsService.ReadAsync();
            }

            return;
        }

        await RefreshAsync();
        var runtimeState = currentSnapshot?.RuntimeState;
        if (runtimeState is LauncherRuntimeState.Ready or LauncherRuntimeState.UpdateAvailable
            && !string.Equals(previousPatchUrlGroup, savedSettings.PatchUrlGroup, StringComparison.Ordinal))
        {
            dialogs.RepairConfirm.Show(localizer.T(LocalizationKeys.DownloadSourceChangedRepairPrompt));
        }
    }

    /// <summary>Saves completed wizard settings, applies their language, and refreshes the shell.</summary>
    public Task HandleSetupWizardCompletedAsync(LauncherSettings newSettings) =>
        startup.HandleSetupWizardCompletedAsync(newSettings);

    /// <summary>Shows confirmation before switching the resource-panel source.</summary>
    public void ShowResourcePanelSourceConfirmDialog()
    {
        dialogs.ResourcePanelSourceConfirm.Show(localizer.T(LocalizationKeys.ResourcePanelCafeOnlyMessage));
    }

    /// <summary>Switches the confirmed resource-panel source and opens its panel.</summary>
    public async Task SwitchSourceThenOpenPanelAsync()
    {
        try
        {
            await savedSettingsWriter.UpdateAsync(settings => settings.PatchUrlGroup = PatchUrlGroups.Cafe);

            await HandleSettingsSavedAsync();
            await resourcePanel.OpenPanelDirectlyAsync();
        }
        catch (Exception exception)
        {
            await errorHandling.HandleErrorAsync("Resource panel source switch failed.", exception,
                new ErrorHandlingOptions { ToastMessage = localizer.F(LocalizationKeys.ResourcePanelLoadFailed, exception.Message) });
        }
    }

    /// <summary>Restores default settings from the debug panel.</summary>
    public async Task ResetSettingsToDefaultsAsync()
    {
        await savedSettingsWriter.ReplaceAsync(LauncherSettings.CreateDefaults());
        await RefreshAsync();
    }

    /// <summary>Restores default settings from the settings page and reports the outcome.</summary>
    public async Task ResetSettingsFromSettingsPageAsync()
    {
        await ResetSettingsToDefaultsAsync();
        toastService.ShowSuccess(localizer.T(LocalizationKeys.DebugSettingsReset));
    }

    private Task OnResourcePanelSourceSwitchConfirmed() => SwitchSourceThenOpenPanelAsync();

    // 经 windowChrome 的注入缝而非直接调 ExternalLinkService.Open：
    // 壳的全部外部链接出口统一走这一条缝，测试可注入记录委托。
    // 不支持应用内更新的平台上，这里收到的不是文件直链而是版本发布页。
    private void OnUpdateAvailableConfirmed(string url) => windowChrome.OpenExternalUrl(url);

    private void OnSelfUpdateStartRequested(string version, IReadOnlyList<ReleaseFile> files) =>
        selfUpdateCoordinator.Begin(version, files);

    private void OnSelfUpdatePreparationStarted() => dialogs.BeginUpdateApply();

    private void OnSelfUpdateProgressReported(LauncherUpdateProgress progress) =>
        dialogs.ReportUpdateProgress(progress.Fraction, progress.BytesPerSecond);

    /// <summary>Maps one coordinator outcome onto the update dialog, toast, and error pipeline.</summary>
    private void OnSelfUpdateFinished(ShellSelfUpdateOutcome outcome, Exception? exception)
    {
        switch (outcome)
        {
            case ShellSelfUpdateOutcome.Ready:
                dialogs.MarkUpdateReady();
                break;
            case ShellSelfUpdateOutcome.DownloadFailed:
                dialogs.MarkUpdateFailed();
                toastService.ShowError(localizer.T(LocalizationKeys.LauncherUpdateDownloadFailed));
                break;
            case ShellSelfUpdateOutcome.Cancelled:
                dialogs.ResetUpdateApply();
                break;
            case ShellSelfUpdateOutcome.UnexpectedError:
                dialogs.MarkUpdateFailed();
                _ = errorHandling.HandleErrorAsync("Launcher self-update failed.", exception!,
                    new ErrorHandlingOptions { ToastMessage = localizer.T(LocalizationKeys.LauncherUpdateDownloadFailed) });
                break;
        }
    }

    private void OnApplyUpdateRequested()
    {
        // 与下载失败不同：没有就绪包时的“应用”是无人可应的按钮事件，保持沉默；
        // helper 拒收才是用户可见的失败。
        switch (selfUpdateCoordinator.TryApplyPending())
        {
            case ShellSelfUpdateApplyResult.NotReady:
                return;
            case ShellSelfUpdateApplyResult.StartFailed:
                dialogs.MarkUpdateFailed();
                toastService.ShowError(localizer.T(LocalizationKeys.LauncherUpdateApplyFailed));
                return;
        }

        // 主进程必须退出，helper 才能替换文件；走正常关窗路径保存窗口与设置。
        windowChrome.RequestShutdown();
    }

    private void OnCancelUpdateRequested() => selfUpdateCoordinator.CancelPreparation();

    /// <summary>Refreshes shell state after a game operation and records resume behavior.</summary>
    public async Task HandleOperationsRefreshRequestedAsync(GameOperationsRefreshMode mode)
    {
        await refreshCoordinator.RefreshAsync(
            resumePersistedDownload: mode != GameOperationsRefreshMode.SkipPersistedResume);
    }

    /// <summary>Refreshes shell state in response to the debug panel.</summary>
    internal Task HandleDebugRefreshRequestedAsync() => RefreshAsync();

    /// <summary>Opens the log viewer from an operation failure action.</summary>
    internal Task OpenLogViewerAsync() => logViewer.OpenCommand.ExecuteAsync(null);

    /// <summary>Opens the log viewer from a synchronous dialog action.</summary>
    internal void OpenLogViewer()
    {
        logViewer.OpenCommand.Execute(null);
    }

    private async Task PreviewAppearanceAsync(
        LauncherSettings previewSettings,
        string? propertyName,
        CancellationToken cancellationToken)
    {
        settings.Appearance.ApplyFrom(previewSettings);
        background.ApplyBackgroundPresentation(previewSettings);

        if (propertyName is null
            or nameof(LauncherSettings.BackgroundSource)
            or nameof(LauncherSettings.CustomBackgroundPath))
        {
            await background.UpdateBackgroundImageAsync(
                previewSettings,
                currentSnapshot,
                cancellationToken);
        }
    }

    private Task ApplyLanguageAndThemeAsync(LauncherSettings launcherSettings)
    {
        ApplyLanguage(launcherSettings.Language);
        settings.Appearance.ApplyFrom(launcherSettings);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Subscribes cross-feature events once for the active shell lifecycle.
    /// 每条接线经 <see cref="Attach"/> 配对登记退订，登记顺序即 <see cref="Unwire"/>
    /// 的逆序拆卸顺序。
    /// </summary>
    public void Wire()
    {
        if (isWired) return;
        isWired = true;

        Attach(
            () => settings.Appearance.GetBackgroundBitmap = background.GetBackgroundBitmap,
            () => settings.Appearance.GetBackgroundBitmap = null);
        Attach(
            () => settings.PreviewAppearanceAsync = PreviewAppearanceAsync,
            () => settings.PreviewAppearanceAsync = null);
        Attach(
            () => settings.ApplyLanguageAndTheme = ApplyLanguageAndThemeAsync,
            () => settings.ApplyLanguageAndTheme = null);
        Attach(
            () => settings.SettingsSaved += HandleSettingsSavedAsync,
            () => settings.SettingsSaved -= HandleSettingsSavedAsync);

        Attach(
            () => resourcePanel.ResourcePanelSourceConfirmRequested += ShowResourcePanelSourceConfirmDialog,
            () => resourcePanel.ResourcePanelSourceConfirmRequested -= ShowResourcePanelSourceConfirmDialog);
        Attach(
            () => dialogs.ResourcePanelSourceConfirm.Confirmed += OnResourcePanelSourceSwitchConfirmed,
            () => dialogs.ResourcePanelSourceConfirm.Confirmed -= OnResourcePanelSourceSwitchConfirmed);

        Attach(
            () => operations.RefreshRequested += HandleOperationsRefreshRequestedAsync,
            () => operations.RefreshRequested -= HandleOperationsRefreshRequestedAsync);
        Attach(
            () => operations.OpenLogViewerRequested += OpenLogViewerAsync,
            () => operations.OpenLogViewerRequested -= OpenLogViewerAsync);

        Attach(
            () => dialogs.DownloadRunningCloseConfirm.Confirmed += windowChrome.CloseAfterStoppingDownload,
            () => dialogs.DownloadRunningCloseConfirm.Confirmed -= windowChrome.CloseAfterStoppingDownload);
        Attach(
            () => dialogs.CloseRequested += windowChrome.RequestClose,
            () => dialogs.CloseRequested -= windowChrome.RequestClose);
        Attach(
            () => dialogs.ConfirmUpdateAvailableRequested += OnUpdateAvailableConfirmed,
            () => dialogs.ConfirmUpdateAvailableRequested -= OnUpdateAvailableConfirmed);
        Attach(
            () => dialogs.SelfUpdateStartRequested += OnSelfUpdateStartRequested,
            () => dialogs.SelfUpdateStartRequested -= OnSelfUpdateStartRequested);
        Attach(
            () => dialogs.ApplyUpdateRequested += OnApplyUpdateRequested,
            () => dialogs.ApplyUpdateRequested -= OnApplyUpdateRequested);
        Attach(
            () => dialogs.CancelUpdateRequested += OnCancelUpdateRequested,
            () => dialogs.CancelUpdateRequested -= OnCancelUpdateRequested);
        Attach(
            () => dialogs.ErrorViewLogRequested += OpenLogViewer,
            () => dialogs.ErrorViewLogRequested -= OpenLogViewer);

        Attach(
            () => debug.RefreshRequested += HandleDebugRefreshRequestedAsync,
            () => debug.RefreshRequested -= HandleDebugRefreshRequestedAsync);
        Attach(
            () => debug.ResetSettingsRequested += ResetSettingsToDefaultsAsync,
            () => debug.ResetSettingsRequested -= ResetSettingsToDefaultsAsync);
        Attach(
            () => debug.ResetSettingsConfirmationRequested += dialogs.DebugResetConfirm.Show,
            () => debug.ResetSettingsConfirmationRequested -= dialogs.DebugResetConfirm.Show);
        Attach(
            () => dialogs.DebugResetConfirm.Confirmed += debug.ConfirmResetSettingsAsync,
            () => dialogs.DebugResetConfirm.Confirmed -= debug.ConfirmResetSettingsAsync);
        Attach(
            () => dialogs.SettingsResetConfirm.Confirmed += ResetSettingsFromSettingsPageAsync,
            () => dialogs.SettingsResetConfirm.Confirmed -= ResetSettingsFromSettingsPageAsync);

        Attach(
            () => remoteContent.OpenExternalUrlRequested = windowChrome.OpenExternalUrl,
            () => remoteContent.OpenExternalUrlRequested = null);

        startup.Wire();

        Attach(
            () => settings.Editor.CurrentPropertyChanged += OnSettingPropertyChanged,
            () => settings.Editor.CurrentPropertyChanged -= OnSettingPropertyChanged);

        RegisterModals();
    }

    /// <summary>Records the teardown half of one wiring so Unwire cannot miss it.</summary>
    private void Attach(Action attach, Action detach)
    {
        attach();
        detachers.Add(detach);
    }

    /// <summary>
    /// 每个模态一条注册记录：种类、可见性旗标来源、栈内容与 ESC 命令收拢在
    /// 一处（ADR-023）。顺序保持原 Wire 订阅序——多处旗标同时翻转时，栈序
    /// 依赖注册序。
    /// </summary>
    private void RegisterModals()
    {
        modalRegistrar.Register(new ModalRegistration(
            ModalKind.Settings,
            windowChrome,
            nameof(WindowChromeViewModel.IsSettingsVisible),
            () => windowChrome.IsSettingsVisible,
            settings,
            windowChrome.ShowSettingsCommand));
        modalRegistrar.Register(new ModalRegistration(
            ModalKind.UnsavedSettingsConfirmation,
            settings,
            nameof(SettingsViewModel.IsUnsavedChangesVisible),
            () => settings.IsUnsavedChangesVisible,
            settings,
            windowChrome.KeepEditingSettingsCommand));
        modalRegistrar.Register(new ModalRegistration(
            ModalKind.ResourcePanel,
            resourcePanel,
            nameof(ResourcePanelViewModel.IsResourcePanelVisible),
            () => resourcePanel.IsResourcePanelVisible,
            resourcePanel,
            resourcePanel.CloseResourcePanelCommand));
        modalRegistrar.Register(new ModalRegistration(
            ModalKind.LogViewer,
            logViewer,
            nameof(LogViewerDialogViewModel.IsVisible),
            () => logViewer.IsVisible,
            logViewer,
            logViewer.CloseCommand));
        modalRegistrar.Register(new ModalRegistration(
            ModalKind.LogExport,
            logExport,
            nameof(LogExportDialogViewModel.IsVisible),
            () => logExport.IsVisible,
            logExport,
            logExport.CloseCommand));
        modalRegistrar.Register(new ModalRegistration(
            ModalKind.Debug,
            debug,
            nameof(DebugViewModel.IsVisible),
            () => debug.IsVisible,
            debug,
            debug.CloseCommand));
        modalRegistrar.Register(new ModalRegistration(
            ModalKind.DesignGallery,
            dialogs.Gallery,
            nameof(DesignGalleryViewModel.IsVisible),
            () => dialogs.Gallery.IsVisible,
            dialogs.Gallery,
            dialogs.Gallery.CloseCommand));
        modalRegistrar.Register(new ModalRegistration(
            ModalKind.Notice,
            dialogs,
            nameof(DialogsViewModel.IsNoticeDialogVisible),
            () => dialogs.IsNoticeDialogVisible,
            dialogs,
            dialogs.DismissNoticeCommand));
        modalRegistrar.Register(new ModalRegistration(
            ModalKind.Update,
            dialogs,
            nameof(DialogsViewModel.IsUpdateAvailableVisible),
            () => dialogs.IsUpdateAvailableVisible,
            dialogs,
            dialogs.CancelUpdateAvailableCommand));
        modalRegistrar.Register(new ModalRegistration(
            ModalKind.Error,
            dialogs,
            nameof(DialogsViewModel.IsErrorDialogVisible),
            () => dialogs.IsErrorDialogVisible,
            dialogs,
            dialogs.ContinueAfterErrorCommand));
        modalRegistrar.Register(new ModalRegistration(
            ModalKind.SetupWizard,
            dialogs,
            nameof(DialogsViewModel.IsSetupWizardVisible),
            () => dialogs.IsSetupWizardVisible,
            dialogs.SetupWizard,
            dialogs.SetupWizardExitConfirm.ShowCommand));
        RegisterConfirmation(ModalKind.StopConfirmation, dialogs.StopConfirm);
        RegisterConfirmation(ModalKind.DownloadRunningCloseConfirmation, dialogs.DownloadRunningCloseConfirm);
        RegisterConfirmation(ModalKind.UninstallConfirmation, dialogs.UninstallConfirm);
        RegisterConfirmation(ModalKind.RepairConfirmation, dialogs.RepairConfirm);
        RegisterConfirmation(ModalKind.ResourcePanelSourceConfirmation, dialogs.ResourcePanelSourceConfirm);
        RegisterConfirmation(ModalKind.DebugResetConfirmation, dialogs.DebugResetConfirm);
        RegisterConfirmation(ModalKind.SettingsResetConfirmation, dialogs.SettingsResetConfirm);
        RegisterConfirmation(ModalKind.SetupWizardExitConfirmation, dialogs.SetupWizardExitConfirm);
    }

    private void RegisterConfirmation(ModalKind kind, ConfirmationDialogViewModel confirmation)
    {
        modalRegistrar.Register(new ModalRegistration(
            kind,
            confirmation,
            nameof(ConfirmationDialogViewModel.IsVisible),
            () => confirmation.IsVisible,
            dialogs,
            confirmation.CancelCommand));
    }

    /// <summary>
    /// Removes cross-feature event subscriptions established by <see cref="Wire"/>.
    /// 按登记的逆序执行退订——这是本次收敛唯一的行为差异；模态注册与
    /// startup 接线不在记录表里，按同一逆序原则手工排在首尾。
    /// </summary>
    public void Unwire()
    {
        if (!isWired) return;
        isWired = false;

        modalRegistrar.Dispose();
        for (var i = detachers.Count - 1; i >= 0; i--)
        {
            detachers[i]();
        }

        detachers.Clear();
        startup.Unwire();
    }

    /// <summary>Handles Escape for the active modal and returns whether a modal consumed it.</summary>
    public bool TryHandleEscape()
    {
        var top = ModalHost.Top;
        return top is not null && modalRegistrar.TryDispatchEscape(top.Kind);
    }

    /// <summary>Unsubscribes lifecycle callbacks and releases lifecycle-owned resources.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        Task pendingRefreshes = refreshCoordinator.BeginShutdown();
        refreshCoordinator.CancelLifetime();
        selfUpdateCoordinator.Dispose();
        Unwire();
        operations.StopOperation(GameOperationStopIntent.ProcessExit);
        if (ownsPresentationCollaborators)
        {
            operations.Dispose();
            shell.Dispose();
            settings.Dispose();
            remoteContent.Dispose();
            background.Dispose();
            toasts.Dispose();
            resourcePanel.Dispose();
            debug.Dispose();
            dialogs.SetupWizard.Dispose();
        }

        errorHandling.CriticalErrorRequested -= OnCriticalError;
        localizer.LocalizationFailure -= OnLocalizationFailure;

        Task pendingWork = refreshCoordinator.WaitForShutdownWorkAsync(pendingRefreshes);
        if (pendingWork.IsCompleted)
        {
            DisposeLifetimeResources();
        }
        else
        {
            _ = pendingWork.ContinueWith(
                _ => DisposeLifetimeResources(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    private void DisposeLifetimeResources() => refreshCoordinator.Dispose();

    private async Task CheckForStartupUpdateAsync(CancellationToken cancellationToken)
    {
        try
        {
            var savedSettings = settings.Editor.GetSavedSnapshot();
            var result = await launcherUpdateService.CheckForUpdateAsync(
                savedSettings.UpdateChannel,
                cancellationToken);

            if (result.IsSuccessful && result.IsUpdateAvailable)
            {
                toastService.Show(new ToastOptions
                {
                    Message = localizer.F(LocalizationKeys.StartupUpdateAvailable, result.LatestVersion),
                    Severity = ToastSeverity.Info,
                    Duration = ToastDuration.Extended,
                    PrimaryAction = new ToastAction(
                        localizer.T(LocalizationKeys.LauncherUpdateView),
                        _ =>
                        {
                            settings.CheckForUpdatesCommand.Execute(null);
                            return Task.FromResult(ToastActionResult.Success());
                        },
                        Timeout: null)
                });
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await diagnostics.DebugAsync(
                "StartupUpdateCheck",
                $"Startup update check failed (non-critical): {exception.Message}",
                CancellationToken.None);
        }
    }

    private async Task ApplySnapshotAsync(LauncherStatusSnapshot snapshot)
    {
        ApplySettingsSnapshot(snapshot.Settings);
        ApplyLanguage(snapshot.Settings.Language);
        settings.Appearance.ApplyTheme(snapshot.Settings.ThemeMode);
        await background.UpdateBackgroundImageAsync(
            snapshot.Settings,
            snapshot,
            refreshCoordinator.LifetimeToken);
        settings.Appearance.ApplyThemeColor(
            snapshot.Settings.ThemeColorMode,
            ColorUtils.ParseColorOrDefault(snapshot.Settings.CustomThemeColor));

        shell.ApplySnapshot(snapshot, settings);
        operations.ApplySnapshot(snapshot);
        remoteContent.Apply(snapshot.Remote, snapshot.Settings, refreshCoordinator.LifetimeToken);
        remoteContent.SetLoadError(snapshot.RuntimeState == LauncherRuntimeState.RemoteUnavailable);
        await dialogs.ShowNoticeDialogIfNeededAsync(snapshot.Remote.BaseConfig, refreshCoordinator.LifetimeToken);
    }

    private void ApplySettingsSnapshot(LauncherSettings savedSettings)
    {
        settings.ApplyLauncherSettings(savedSettings);
        resourcePanel.ApplySettings(savedSettings);
        ApplyMotionSettings(savedSettings);
    }

    private void ApplyMotionSettings(LauncherSettings savedSettings)
    {
        var systemAnimationsEnabled = savedSettings.MotionMode == MotionModes.System
            ? systemAnimationSettingsProvider.GetSystemAnimationsEnabled()
            : null;
        var reduceMotion = MotionSettingsResolver.ShouldReduceMotion(
            savedSettings.MotionMode,
            systemAnimationsEnabled);
        if (motionSettingsApplied && reduceMotion == isMotionReduced)
        {
            return;
        }

        motionSettingsApplied = true;
        SetPresentationState(ref isMotionReduced, reduceMotion);
        remoteContent.ApplyMotionPreference(reduceMotion);
        toasts.ApplyMotionPreference(reduceMotion);
        background.ApplyMotionPreference(reduceMotion);
    }

    private void ApplyLanguage(string language)
    {
        // 扇出名单已注入 shell（LanguageAwarePresentations）：任何入口（生命周期、
        // 设置保存、直调 Shell.ApplyLanguage 的测试）都能得到同一份完整刷新。
        shell.ApplyLanguage(language, currentSnapshot is not null);
    }

    private void OnLocalizationFailure(object? sender, LocalizationFailureEventArgs eventArgs)
    {
        _ = errorHandling.HandleErrorAsync(
            "Localization resources could not be loaded.",
            eventArgs.Exception,
            new ErrorHandlingOptions
            {
                ToastMessage = "Localization unavailable.",
                IncludeExceptionDetails = false
            });
    }

    private void OnStatusDetailModeChanged()
    {
        StatusDetailModeChanged?.Invoke();
    }

    private void SetPresentationState(ref bool field, bool value)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        PresentationChanged?.Invoke();
    }

    private void OnCriticalError(CriticalErrorInfo info)
    {
        dialogs.ShowCriticalError(info.Message, info.Details);
    }

    private void OnSettingPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LauncherSettings.StatusDetailMode))
        {
            OnStatusDetailModeChanged();
        }
    }

}
