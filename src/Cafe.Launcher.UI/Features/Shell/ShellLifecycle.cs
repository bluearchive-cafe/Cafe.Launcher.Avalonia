using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.UI.Constants;
using Cafe.Launcher.UI.Features.Diagnostics;
using Cafe.Launcher.UI.Features.GameOperations;
using Cafe.Launcher.UI.Helpers;
using Cafe.Launcher.UI.Features.ResourcePanel;
using Cafe.Launcher.UI.Features.Settings;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.Core.Services.Diagnostics;
using Cafe.Launcher.Core.Services.Update;
using Cafe.Launcher.UI.ViewModels;
using Cafe.Launcher.Core;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Services;

namespace Cafe.Launcher.UI.Features.Shell;

/// <summary>
/// Owns shell startup, refresh, settings-save, first-run wizard completion,
/// resource-panel switching, and maps the in-app self-update state machine
/// (<see cref="ShellSelfUpdateCoordinator"/>) onto dialogs, toasts, and shutdown.
/// Cross-feature subscriptions are owned by <see cref="ShellPresentationWiring"/>;
/// this type keeps the lifecycle verbs those subscriptions call into.
/// The window (MainWindowViewModel) only presents shell state.
/// </summary>
internal sealed class ShellLifecycle : IDisposable
{
    /// <summary>Raised when shell presentation state changes.</summary>
    public event Action? PresentationChanged;

    /// <summary>Raised after a saved status-detail setting changes the shell presentation mode.</summary>
    public event Action? StatusDetailModeChanged;

    private readonly ILauncherCoreService launcherCoreService;
    private readonly ILauncherSettingsService settingsService;
    private readonly ISavedSettingsWriter savedSettingsWriter;
    private readonly LocalizationService localizer;
    private readonly ToastService toastService;
    private readonly ILauncherUpdateService launcherUpdateService;
    private readonly ILauncherUpdatePresenter updatePresenter;
    private readonly ILauncherSelfUpdateService launcherSelfUpdateService;
    private readonly IWindowsLauncherUpdateApplier launcherUpdateApplier;
    private readonly ILauncherDiagnostics diagnostics;
    private readonly IErrorHandlingService errorHandling;
    private readonly ISystemAnimationSettingsProvider systemAnimationSettingsProvider;
    private readonly LauncherBuildIdentity? buildIdentity;
    private readonly ShellViewModel shell;
    private readonly BackgroundViewModel background;
    private readonly RemoteContentViewModel remoteContent;
    private readonly DialogsViewModel dialogs;
    private readonly GameOperationsViewModel operations;
    private readonly ToastHostViewModel toasts;
    private readonly WindowChromeViewModel windowChrome;
    private readonly SettingsViewModel settings;
    private readonly ResourcePanelViewModel resourcePanel;
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
    private readonly SavedSettingsFollowThrough savedSettingsFollowThrough;
    private readonly ShellPresentationWiring presentationWiring;
    private bool disposed;
    private bool isBusy;
    private bool isMotionReduced = true;
    private bool motionSettingsApplied;
    private bool settingsSnapshotInitialized;
    private LauncherStatusSnapshot? currentSnapshot;
    private readonly ShellSelfUpdateCoordinator selfUpdateCoordinator;

    /// <summary>Gets the active startup update check so tests can coordinate without timing delays.</summary>
    public Task PendingStartupUpdateCheck => refreshCoordinator.PendingAfterLoadWork;

    /// <summary>Initializes shell lifecycle dependencies and subscribes error handling callbacks.</summary>
    public ShellLifecycle(
        ILauncherCoreService launcherCoreService,
        ILauncherSettingsService settingsService,
        ISavedSettingsWriter savedSettingsWriter,
        LocalizationService localizer,
        ToastService toastService,
        ILauncherUpdateService launcherUpdateService,
        ILauncherUpdatePresenter updatePresenter,
        ILauncherSelfUpdateService launcherSelfUpdateService,
        IWindowsLauncherUpdateApplier launcherUpdateApplier,
        ILauncherDiagnostics diagnostics,
        IErrorHandlingService errorHandling,
        ISystemAnimationSettingsProvider systemAnimationSettingsProvider,
        ShellPresentationFamily family,
        IFilePickerService filePickerService,
        LauncherBuildIdentity? buildIdentity = null)
        : this(
            launcherCoreService,
            settingsService,
            savedSettingsWriter,
            localizer,
            toastService,
            launcherUpdateService,
            updatePresenter,
            launcherSelfUpdateService,
            launcherUpdateApplier,
            diagnostics,
            errorHandling,
            systemAnimationSettingsProvider,
            family,
            filePickerService,
            ownsPresentationCollaborators: false,
            buildIdentity: buildIdentity)
    {
    }

    internal ShellLifecycle(
        ILauncherCoreService launcherCoreService,
        ILauncherSettingsService settingsService,
        ISavedSettingsWriter savedSettingsWriter,
        LocalizationService localizer,
        ToastService toastService,
        ILauncherUpdateService launcherUpdateService,
        ILauncherUpdatePresenter updatePresenter,
        ILauncherSelfUpdateService launcherSelfUpdateService,
        IWindowsLauncherUpdateApplier launcherUpdateApplier,
        ILauncherDiagnostics diagnostics,
        IErrorHandlingService errorHandling,
        ISystemAnimationSettingsProvider systemAnimationSettingsProvider,
        ShellPresentationFamily family,
        IFilePickerService filePickerService,
        bool ownsPresentationCollaborators,
        LauncherBuildIdentity? buildIdentity = null)
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
        this.updatePresenter = updatePresenter;
        this.launcherSelfUpdateService = launcherSelfUpdateService;
        this.launcherUpdateApplier = launcherUpdateApplier;
        this.diagnostics = diagnostics;
        this.errorHandling = errorHandling;
        this.systemAnimationSettingsProvider = systemAnimationSettingsProvider;
        this.buildIdentity = buildIdentity;
        shell = family.Shell;
        background = family.Background;
        remoteContent = family.RemoteContent;
        dialogs = family.Dialogs;
        operations = family.Operations;
        toasts = family.Toasts;
        windowChrome = family.WindowChrome;
        settings = family.Settings;
        resourcePanel = family.ResourcePanel;
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
        savedSettingsFollowThrough = new SavedSettingsFollowThrough(
            settings,
            resourcePanel,
            remoteContent,
            dialogs,
            operations,
            localizer,
            ApplyLanguage,
            ApplyMotionSettings,
            () => currentSnapshot,
            persisted =>
            {
                if (currentSnapshot is not null)
                {
                    currentSnapshot.Settings = persisted;
                }
            },
            cancellationToken => RefreshAsync(cancellationToken));
        startup = new ShellStartup(
            RefreshAsync,
            ApplyMotionSettings,
            ApplyLanguage,
            newSettings => savedSettingsWriter.ReplaceAsync(newSettings),
            savedSettingsFollowThrough,
            () => dialogs.IsSetupWizardVisible = false,
            () => dialogs.IsSetupWizardVisible,
            dialogs.SetupWizard);
        presentationWiring = new ShellPresentationWiring(
            family,
            savedSettingsFollowThrough,
            startup,
            localizer,
            new ShellWiringActions(
                PreviewAppearanceAsync,
                HandleOperationsRefreshRequestedAsync,
                HandleDebugRefreshRequestedAsync,
                SwitchSourceThenOpenPanelAsync,
                ResetSettingsToDefaultsAsync,
                ResetSettingsFromSettingsPageAsync,
                OnStatusDetailModeChanged,
                OnSelfUpdateStartRequested,
                OnApplyUpdateRequested,
                OnCancelUpdateRequested));

        presentationWiring.Wire();
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

    /// <summary>封闭操作入口并取消生命周期工作，等待刷新与游戏操作收尾后才允许宿主释放依赖。</summary>
    public async Task PrepareForShutdownAsync()
    {
        Task pendingRefreshes = refreshCoordinator.BeginShutdown();
        Task pendingOperations = operations.PrepareForShutdownAsync();
        refreshCoordinator.CancelLifetime();
        await Task.WhenAll(pendingOperations, refreshCoordinator.WaitForShutdownWorkAsync(pendingRefreshes));
    }

    /// <summary>Applies the follow-through of one persisted settings snapshot.</summary>
    public Task HandleSettingsSavedAsync(LauncherSettings savedSettings)
    {
        ArgumentNullException.ThrowIfNull(savedSettings);
        var handoff = savedSettingsFollowThrough.Apply(savedSettings);
        return savedSettingsFollowThrough.CompleteAsync(handoff);
    }

    /// <summary>Saves completed wizard settings, applies their language, and refreshes the shell.</summary>
    public Task HandleSetupWizardCompletedAsync(LauncherSettings newSettings) =>
        startup.HandleSetupWizardCompletedAsync(newSettings);

    /// <summary>Switches the confirmed resource-panel source and opens its panel.</summary>
    public async Task SwitchSourceThenOpenPanelAsync()
    {
        try
        {
            var savedSettings = await savedSettingsWriter.UpdateAsync(
                settings => settings.PatchUrlGroup = PatchUrlGroups.Cafe);

            await HandleSettingsSavedAsync(savedSettings);
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
        var savedSettings = await savedSettingsWriter.ReplaceAsync(LauncherSettings.CreateDefaults(buildIdentity));
        await HandleSettingsSavedAsync(savedSettings);
    }

    /// <summary>Restores default settings from the settings page and reports the outcome.</summary>
    public async Task ResetSettingsFromSettingsPageAsync()
    {
        await ResetSettingsToDefaultsAsync();
        toastService.ShowSuccess(localizer.T(LocalizationKeys.DebugSettingsReset));
    }

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
            case ShellSelfUpdateOutcome.DownloadBlocked:
                dialogs.MarkUpdateFailed();
                toastService.ShowError(localizer.T(LocalizationKeys.LauncherUpdateDownloadBlocked));
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

    private async Task PreviewAppearanceAsync(
        LauncherSettings previewSettings,
        string? propertyName,
        CancellationToken cancellationToken)
    {
        settings.Appearance.ApplyFrom(previewSettings);
        background.ApplyBackgroundPresentation(previewSettings);

        if (!background.IsCurrentBackgroundSelection(previewSettings)
            || propertyName is null
            or nameof(LauncherSettings.BackgroundSource)
            or nameof(LauncherSettings.CustomBackgroundPath))
        {
            await background.UpdateBackgroundImageAsync(
                previewSettings,
                currentSnapshot,
                cancellationToken);
        }
    }

    /// <summary>Subscribes cross-feature events once for the active shell lifecycle.</summary>
    public void Wire() => presentationWiring.Wire();

    /// <summary>Removes the cross-feature subscriptions established by <see cref="Wire"/>.</summary>
    public void Unwire() => presentationWiring.Unwire();

    /// <summary>Handles Escape for the active modal and returns whether a modal consumed it.</summary>
    public bool TryHandleEscape() => presentationWiring.TryDispatchEscape();

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

            updatePresenter.Present(result, LauncherUpdateTrigger.Startup);
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

}
