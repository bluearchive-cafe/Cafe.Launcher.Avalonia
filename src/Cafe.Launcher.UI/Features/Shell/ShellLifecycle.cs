using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.UI.Constants;
using Cafe.Launcher.Core.Constants;
using Cafe.Launcher.UI.Features.Diagnostics;
using Cafe.Launcher.UI.Features.Games;
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
/// resource-panel switching, and the lifecycle verbs behind cross-feature wiring.
/// The in-app self-update outcome mapping lives in <see cref="ShellSelfUpdateFlow"/>;
/// cross-feature subscriptions are owned by <see cref="ShellPresentationWiring"/>.
/// The window (MainWindowViewModel) only presents shell state.
/// </summary>
internal sealed class ShellLifecycle : IDisposable
{
    /// <summary>Raised when shell presentation state changes.</summary>
    public event Action? PresentationChanged;

    /// <summary>Raised after a saved status-detail setting changes the shell presentation mode.</summary>
    public event Action? StatusDetailModeChanged;

    private readonly ILauncherStatusSnapshotService launcherStatusSnapshotService;
    private readonly ILauncherSettingsService settingsService;
    private readonly ISavedSettingsWriter savedSettingsWriter;
    private readonly LocalizationService localizer;
    private readonly INotificationService toastService;
    private readonly ILauncherUpdateService launcherUpdateService;
    private readonly ILauncherUpdatePresenter updatePresenter;
    private readonly ILauncherDiagnostics diagnostics;
    private readonly IErrorHandlingService errorHandling;
    private readonly ISystemAnimationSettingsProvider systemAnimationSettingsProvider;
    private readonly LauncherBuildIdentity? buildIdentity;
    private readonly ShellViewModel defaultShell;
    private ShellViewModel shell => games?.Active.Shell ?? defaultShell;
    private readonly BackgroundViewModel background;
    private readonly RemoteContentViewModel defaultRemoteContent;
    private RemoteContentViewModel remoteContent => games?.Active.RemoteContent ?? defaultRemoteContent;
    private readonly DialogsViewModel dialogs;
    private readonly GameOperationsViewModel defaultOperations;
    private GameOperationsViewModel operations => games?.Active.Operations ?? defaultOperations;
    private readonly GameLibraryViewModel? games;
    private readonly IGameFileOperationCoordinator? fileOperations;
    private readonly ToastHostViewModel toasts;
    private readonly WindowChromeViewModel windowChrome;
    private readonly ModalHostViewModel modalHost;
    private readonly LogViewerDialogViewModel logViewer;
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
    private bool startupUpdateCheckStarted;
    private LauncherStatusSnapshot? defaultSnapshot;
    private LauncherStatusSnapshot? currentSnapshot
    {
        get => games is not null ? games.Active.Snapshot : defaultSnapshot;
        set
        {
            if (games is null)
            {
                defaultSnapshot = value;
            }
            else if (value is not null)
            {
                games.Get(value.GameId).Snapshot = value;
            }
        }
    }

    private readonly ShellSelfUpdateFlow selfUpdateFlow;

    /// <summary>Gets the active startup update check so tests can coordinate without timing delays.</summary>
    public Task PendingStartupUpdateCheck => refreshCoordinator.PendingAfterLoadWork;

    /// <summary>Initializes shell lifecycle dependencies and subscribes error handling callbacks.</summary>
    public ShellLifecycle(
        ILauncherStatusSnapshotService launcherStatusSnapshotService,
        ILauncherSettingsService settingsService,
        ISavedSettingsWriter savedSettingsWriter,
        LocalizationService localizer,
        INotificationService toastService,
        ILauncherUpdateService launcherUpdateService,
        ILauncherUpdatePresenter updatePresenter,
        ILauncherSelfUpdateService launcherSelfUpdateService,
        IWindowsLauncherUpdateApplier launcherUpdateApplier,
        ILauncherDiagnostics diagnostics,
        IErrorHandlingService errorHandling,
        ISystemAnimationSettingsProvider systemAnimationSettingsProvider,
        ShellPresentationFamily family,
        IFilePickerService filePickerService,
        LauncherBuildIdentity? buildIdentity = null,
        GameLibraryViewModel? games = null,
        IGameFileOperationCoordinator? fileOperations = null)
        : this(
            launcherStatusSnapshotService,
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
            buildIdentity: buildIdentity,
            games: games,
            fileOperations: fileOperations)
    {
    }

    internal ShellLifecycle(
        ILauncherStatusSnapshotService launcherStatusSnapshotService,
        ILauncherSettingsService settingsService,
        ISavedSettingsWriter savedSettingsWriter,
        LocalizationService localizer,
        INotificationService toastService,
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
        LauncherBuildIdentity? buildIdentity = null,
        GameLibraryViewModel? games = null,
        IGameFileOperationCoordinator? fileOperations = null)
    {
        // 所有权制度按构造路径分叉（受控测试缝，见 AUD-ARCH-003）：
        // - 生产 DI 路径走公开构造，ownsPresentationCollaborators: false——展示 VM 家族由
        //   DI 组合根（全 Singleton）持有并释放，Dispose 不得触碰它们。
        // - 测试路径经 MainWindowViewModel 的 internal 构造以 owns: true 创建，替身展示
        //   VM 无容器持有，由 ShellLifecycle.Dispose 统一释放。两种制度下 Dispose 的释放
        //   范围不同；与释放顺序相关的回归在测试中不可复现，收敛该分叉需先让测试路径
        //   显式管理替身生命周期（已裁定当前缝可接受，故仅在此书面记录差异）。
        this.filePickerService = filePickerService;
        this.launcherStatusSnapshotService = launcherStatusSnapshotService;
        this.settingsService = settingsService;
        this.savedSettingsWriter = savedSettingsWriter;
        this.localizer = localizer;
        this.toastService = toastService;
        this.launcherUpdateService = launcherUpdateService;
        this.updatePresenter = updatePresenter;
        this.diagnostics = diagnostics;
        this.errorHandling = errorHandling;
        this.systemAnimationSettingsProvider = systemAnimationSettingsProvider;
        this.buildIdentity = buildIdentity;
        this.games = games;
        this.fileOperations = fileOperations;
        defaultShell = family.Shell;
        background = family.Background;
        defaultRemoteContent = family.RemoteContent;
        dialogs = family.Dialogs;
        defaultOperations = family.Operations;
        toasts = family.Toasts;
        windowChrome = family.WindowChrome;
        modalHost = family.ModalHost;
        logViewer = family.LogViewer;
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
        selfUpdateFlow = new ShellSelfUpdateFlow(
            launcherSelfUpdateService,
            launcherUpdateApplier,
            dialogs.LauncherUpdate,
            toastService,
            localizer,
            errorHandling,
            family.WindowChrome,
            refreshCoordinator.LifetimeToken);
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
            cancellationToken => RefreshAsync(cancellationToken),
            games,
            UpdateActiveBackgroundAsync);
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
                selfUpdateFlow.Begin,
                selfUpdateFlow.Apply,
                selfUpdateFlow.Cancel),
            () => family with { Shell = shell, RemoteContent = remoteContent, Operations = operations },
            operationsWiredByLibrary: games is not null);

        if (games is not null)
        {
            games.CanSwitch = () => !modalHost.HasEntries && !settings.Editor.IsDirty && !settings.IsSaving
                && fileOperations?.IsShuttingDown != true;
            settings.IsSettingsOverlayVisible = () => windowChrome.IsSettingsVisible;
            games.SetSettingsTarget = context =>
            {
                settings.SetGameTarget(context);
                resourcePanel.SetGameTarget(context);
                dialogs.SetNoticeTarget(context);
            };
            games.SetSettingsTarget(games.Active.Core);
            games.RefreshRequested = RefreshGameAsync;
            games.OpenLogViewerAsync = () => logViewer.OpenCommand.ExecuteAsync(null);
            games.TaskSurfaceRequested = presentationWiring.RevealTaskSurface;
            games.PickInstallPathAsync = (context, start) => settings.PickInstallationDraftAsync(context.Core, start);
            games.CommitInstallPathAsync = (context, path) => settings.CommitInstallationPathAsync(context.Core, path);
            games.ActiveChanged += OnActiveGameChanged;
        }

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
        selfUpdateFlow.CleanupAbandonedHelpers();
        return startup.InitializeAsync(cancellationToken);
    }

    /// <summary>启动外部请求明确指定的条目；受阻时即时反馈，不跟随当前页面或排队补做。</summary>
    public async Task RequestGameLaunchAsync(string gameId)
    {
        if (!LauncherProfiles.TryGetSupportedGame(gameId, out _)
            || modalHost.HasEntries || settings.Editor.IsDirty || fileOperations?.IsShuttingDown == true
            || string.Equals(fileOperations?.ActiveOperation?.GameId, gameId, StringComparison.Ordinal))
        {
            toastService.ShowWarning(localizer.T(LocalizationKeys.OperationUnavailableForCurrentState));
            return;
        }

        if (games is null)
        {
            if (string.Equals(operations.FixedGameId, gameId, StringComparison.Ordinal)
                && operations.StartGameCommand.CanExecute(null))
            {
                await operations.StartGameCommand.ExecuteAsync(null);
            }
            else
            {
                toastService.ShowWarning(localizer.T(LocalizationKeys.OperationUnavailableForCurrentState));
            }

            return;
        }

        var target = games.Get(gameId);
        if (!await games.TrySelectGameAsync(gameId))
        {
            return;
        }

        if (target.Snapshot is null)
        {
            await RefreshGameAsync(target, GameOperationsRefreshMode.SkipPersistedResume);
        }

        if (modalHost.HasEntries || settings.Editor.IsDirty || fileOperations?.IsShuttingDown == true
            || !target.Operations.StartGameCommand.CanExecute(null))
        {
            toastService.ShowWarning(localizer.T(LocalizationKeys.OperationUnavailableForCurrentState));
            return;
        }

        await target.Operations.StartGameCommand.ExecuteAsync(null);
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
        if (games is not null)
        {
            return await LoadLibraryStateAsync(refreshToken);
        }

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

            var snapshot = await launcherStatusSnapshotService.LoadAsync(refreshToken);
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
            if (games is not null)
            {
                await ResumeContextAsync(games.Active, refreshToken);
            }
            else
            {
                await operations.ResumePersistedDownloadAsync(refreshToken);
            }
        }

        if (!startupUpdateCheckStarted && settings.Editor.GetSavedSnapshot().EnableStartupUpdateCheck)
        {
            startupUpdateCheckStarted = true;
            return CheckForStartupUpdateAsync(refreshCoordinator.LifetimeToken);
        }

        return Task.CompletedTask;
    }

    /// <summary>封闭操作入口并取消生命周期工作，等待刷新与游戏操作收尾后才允许宿主释放依赖。</summary>
    public async Task PrepareForShutdownAsync()
    {
        if (fileOperations is not null && !fileOperations.TryBeginShutdown(out _))
        {
            return;
        }

        Task pendingRefreshes = refreshCoordinator.BeginShutdown();
        Task pendingOperations = games?.PrepareForShutdownAsync() ?? operations.PrepareForShutdownAsync();
        if (games is not null)
        {
            foreach (var entry in games.Entries)
            {
                entry.Context.Lifetime.Cancel();
            }
        }

        refreshCoordinator.CancelLifetime();
        await Task.WhenAll(pendingOperations, refreshCoordinator.WaitForShutdownWorkAsync(pendingRefreshes));
        if (games is not null)
        {
            await Task.WhenAll(games.Entries.Select(entry => entry.Context.PendingRefresh));
        }
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
            var savedSettings = games is null
                ? await savedSettingsWriter.UpdateAsync(
                    settings => settings.PatchUrlGroup = PatchUrlGroups.Cafe)
                : await savedSettingsWriter.UpdateGameAsync(
                    operations.FixedGameId, settings => settings.PatchUrlGroup = PatchUrlGroups.Cafe);

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
        var savedSettings = await savedSettingsWriter.ReplaceAsync(settings =>
        {
            var defaults = LauncherSettings.CreateDefaults(buildIdentity);
            // 恢复偏好不等于撤销同意：保留已落盘的协议版本，避免下次启动无端重新征询。
            defaults.AcceptedAgreementVersion = settings.AcceptedAgreementVersion;
            return defaults;
        });
        await HandleSettingsSavedAsync(savedSettings);
    }

    /// <summary>Restores default settings from the settings page and reports the outcome.</summary>
    public async Task ResetSettingsFromSettingsPageAsync()
    {
        await ResetSettingsToDefaultsAsync();
        toastService.ShowSuccess(localizer.T(LocalizationKeys.DebugSettingsReset));
    }

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
        ApplyMotionSettings(previewSettings);
        var rendered = GameBackgroundPresentation.Resolve(
            previewSettings,
            settings.EditingGameId,
            settings.IsGlobalBackgroundPreview);
        background.ApplyBackgroundPresentation(rendered);

        if (!background.IsCurrentBackgroundSelection(rendered)
            || propertyName is null
            or nameof(LauncherSettings.BackgroundSource)
            or nameof(LauncherSettings.CustomBackgroundPath))
        {
            await background.UpdateBackgroundImageAsync(
                rendered,
                currentSnapshot,
                cancellationToken);
        }
    }

    /// <summary>Subscribes cross-feature events once for the active shell lifecycle.</summary>
    public void Wire()
    {
        if (games is not null)
        {
            games.OpenLogViewerAsync = () => logViewer.OpenCommand.ExecuteAsync(null);
            games.TaskSurfaceRequested = presentationWiring.RevealTaskSurface;
        }

        presentationWiring.Wire();
    }

    /// <summary>Removes the cross-feature subscriptions established by <see cref="Wire"/>.</summary>
    public void Unwire()
    {
        if (games is not null)
        {
            games.OpenLogViewerAsync = null;
            games.TaskSurfaceRequested = null;
        }

        presentationWiring.Unwire();
    }

    /// <summary>Handles Escape for the active modal and returns whether a modal consumed it.</summary>
    public bool TryHandleEscape() => presentationWiring.TryDispatchEscape();

    /// <summary>Unsubscribes lifecycle callbacks and releases lifecycle-owned resources.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        Task pendingRefreshes = refreshCoordinator.BeginShutdown();
        refreshCoordinator.CancelLifetime();
        selfUpdateFlow.Dispose();
        Unwire();
        if (games is not null)
        {
            games.ActiveChanged -= OnActiveGameChanged;
            games.OpenLogViewerAsync = null;
            games.TaskSurfaceRequested = null;
            games.RefreshRequested = null;
            games.PickInstallPathAsync = null;
            games.CommitInstallPathAsync = null;
            games.SetSettingsTarget = null;
            games.CanSwitch = null;
            games.StopOperation(GameOperationStopIntent.ProcessExit);
        }
        else
        {
            operations.StopOperation(GameOperationStopIntent.ProcessExit);
        }
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

            await updatePresenter.PresentAsync(result, LauncherUpdateTrigger.Startup);
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

    private void OnActiveGameChanged()
    {
        Unwire();
        shell.LanguageAwarePresentations = languageAwarePresentations;
        Wire();
        var saved = settings.Editor.GetSavedSnapshot();
        ApplyLanguage(saved.Language);
        // Cached game content outlives the settings session that changed this
        // global preference; selection must reconcile it without refetching news.
        remoteContent.UpdateRemoteContentVisibility(saved.ShowRemoteContentCard);
        remoteContent.ApplyMotionPreference(isMotionReduced);
        if (games?.Active.Snapshot is not null)
        {
            remoteContent.StartCarouselTimer();
        }

        _ = UpdateActiveBackgroundAsync(saved, games?.Active.Snapshot);
        PresentationChanged?.Invoke();
    }

    private async Task UpdateActiveBackgroundAsync(LauncherSettings saved, LauncherStatusSnapshot? snapshot)
    {
        try
        {
            await background.UpdateBackgroundImageAsync(
                ResolveBackgroundSettings(saved, snapshot),
                snapshot,
                refreshCoordinator.LifetimeToken);
        }
        catch (OperationCanceledException) when (refreshCoordinator.LifetimeToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await diagnostics.DebugAsync(
                "Background",
                $"Selected game background refresh failed: {exception.Message}",
                CancellationToken.None);
        }
    }

    private async Task<bool> LoadLibraryStateAsync(CancellationToken refreshToken)
    {
        SetPresentationState(ref isBusy, true);
        try
        {
            var library = games!;
            var selectionRevision = library.SelectionRevision;
            var switchingAtReadStart = library.IsSwitching;
            var saved = await settingsService.ReadAsync(refreshToken);
            var selectionChangedDuringRead = switchingAtReadStart || library.IsSwitching
                || selectionRevision != library.SelectionRevision;
            if (selectionChangedDuringRead)
            {
                // The selection writer may already have published a newer value to the editor.
                saved.LastViewedGameId = settings.Editor.GetSavedSnapshot().LastViewedGameId;
            }

            if (windowChrome.IsSettingsVisible)
            {
                settings.Editor.ApplySavedUpdate(saved);
            }
            else
            {
                settings.Editor.ApplySnapshot(saved);
            }

            settingsSnapshotInitialized = true;
            if (!windowChrome.IsSettingsVisible && !selectionChangedDuringRead)
            {
                library.RestoreSelection(saved);
            }

            ApplySettingsSnapshot(saved);
            ApplyLanguage(saved.Language);
            settings.Appearance.Load(windowChrome.IsSettingsVisible ? settings.Editor.GetSnapshot() : saved);
            settings.Appearance.ApplyFrom(windowChrome.IsSettingsVisible ? settings.Editor.GetSnapshot() : saved);
            await RefreshGameAsync(library.Active, GameOperationsRefreshMode.SkipPersistedResume);
            refreshToken.ThrowIfCancellationRequested();
            return library.Active.Snapshot is not null;
        }
        catch (OperationCanceledException) when (refreshToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            shell.SetRefreshError(exception, settings, games!.Active.GameId);
            if (!operations.HasPendingOperation)
            {
                operations.SetIdlePanels(currentSnapshot);
                shell.IsBusy = false;
            }

            await errorHandling.HandleErrorAsync("Launcher core refresh failed.", exception,
                new ErrorHandlingOptions { ToastMessage = localizer.F(LocalizationKeys.LauncherCoreRefreshFailed, exception.Message) });
            return false;
        }
        finally
        {
            SetPresentationState(ref isBusy, false);
        }
    }

    private async Task ResumeContextAsync(GamePresentationContext target, CancellationToken token)
    {
        if (target.Snapshot is null)
        {
            return;
        }

        if (string.Equals(target.GameId, Cafe.Launcher.Core.Constants.LauncherProfiles.BlueArchiveJapan.RuntimeId, StringComparison.Ordinal))
        {
            var result = await target.Checkpoint.TryMigrateLegacyBlueArchiveAsync(
                target.Core.Paths.DataRoot,
                target.Snapshot,
                target.Core.LocalInstallationStateStore,
                target.Core.InstallationPath,
                token);
            if (result is LegacyCheckpointMigrationResult.IdentityUnverified
                or LegacyCheckpointMigrationResult.IncompatibleCheckpoint
                or LegacyCheckpointMigrationResult.InvalidCheckpoint
                or LegacyCheckpointMigrationResult.InvalidMigrationMarker
                or LegacyCheckpointMigrationResult.IoFailure)
            {
                toastService.ShowWarning(localizer.T(LocalizationKeys.LegacyDownloadResumeUnavailable));
                return;
            }
        }

        await target.Operations.ResumePersistedDownloadAsync(token);
    }

    private Task RefreshGameAsync(GamePresentationContext target, GameOperationsRefreshMode mode)
    {
        // Per-game operation refreshes only reload that context. Recovery is a separate startup
        // or explicit command, so completing A or viewing B cannot implicitly queue a resume.
        return target.RunRefreshAsync(() => LoadGameAsync(target));
    }

    private async Task LoadGameAsync(GamePresentationContext target)
    {
        var token = target.Lifetime.Token;
        await target.RefreshGate.WaitAsync(token);
        target.Shell.IsBusy = true;
        try
        {
            target.Shell.SetLoading();
            target.RemoteContent.BeginLoading(settings.Editor.GetSavedSnapshot().ShowRemoteContentCard);
            var snapshot = await target.Core.CoreService.LoadAsync(token);
            token.ThrowIfCancellationRequested();
            target.Snapshot = snapshot;
            if (ReferenceEquals(games!.Active, target))
            {
                // Active-game refresh owns the whole visible surface, including global settings
                // and appearance derived from the same snapshot.
                await ApplySnapshotAsync(snapshot);
            }
            else
            {
                target.Shell.ApplySnapshot(snapshot, settings);
                target.Operations.ApplySnapshot(snapshot);
                // The network load can finish after a global preference was saved.
                target.RemoteContent.Apply(snapshot.Remote, settings.Editor.GetSavedSnapshot(), token);
                target.RemoteContent.SetLoadError(snapshot.RuntimeState == LauncherRuntimeState.RemoteUnavailable);
                target.RemoteContent.StopCarouselTimer();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            target.Shell.SetRefreshError(exception, settings, target.GameId);
            await errorHandling.HandleErrorAsync("Game state refresh failed.", exception,
                new ErrorHandlingOptions
                {
                    ToastMessage = localizer.F(
                        LocalizationKeys.GameOperationMessage,
                        GameDisplayName.Resolve(target.GameId, localizer),
                        localizer.F(LocalizationKeys.LauncherCoreRefreshFailed, exception.Message))
                });
        }
        finally
        {
            target.RemoteContent.EndLoading();
            target.Shell.IsBusy = false;
            target.RefreshGate.Release();
        }
    }

    private async Task ApplySnapshotAsync(LauncherStatusSnapshot snapshot)
    {
        ApplySettingsSnapshot(snapshot.Settings);
        ApplyLanguage(snapshot.Settings.Language);
        settings.Appearance.ApplyTheme(
            windowChrome.IsSettingsVisible ? settings.Editor.Current.ThemeMode : snapshot.Settings.ThemeMode);
        await background.UpdateBackgroundImageAsync(
            ResolveBackgroundSettings(snapshot.Settings, snapshot),
            snapshot,
            refreshCoordinator.LifetimeToken);
        settings.Appearance.ApplyThemeColor(
            windowChrome.IsSettingsVisible ? settings.Editor.Current : snapshot.Settings);

        shell.ApplySnapshot(snapshot, settings);
        operations.ApplySnapshot(snapshot);
        remoteContent.Apply(snapshot.Remote, snapshot.Settings, refreshCoordinator.LifetimeToken);
        remoteContent.SetLoadError(snapshot.RuntimeState == LauncherRuntimeState.RemoteUnavailable);
        await dialogs.ShowNoticeDialogIfNeededAsync(snapshot.Remote.BaseConfig, refreshCoordinator.LifetimeToken);
    }

    private void ApplySettingsSnapshot(LauncherSettings savedSettings)
    {
        if (windowChrome.IsSettingsVisible)
        {
            settings.Editor.ApplySavedUpdate(savedSettings);
        }
        else
        {
            settings.ApplyLauncherSettings(savedSettings);
        }

        resourcePanel.ApplySettings(savedSettings);
        ApplyMotionSettings(windowChrome.IsSettingsVisible ? settings.Editor.Current : savedSettings);
    }

    private LauncherSettings ResolveBackgroundSettings(LauncherSettings saved, LauncherStatusSnapshot? snapshot)
    {
        var gameId = snapshot?.GameId ?? games?.Active.GameId ?? settings.EditingGameId;
        return GameBackgroundPresentation.Resolve(
            windowChrome.IsSettingsVisible ? settings.Editor.GetSnapshot() : saved,
            gameId,
            windowChrome.IsSettingsVisible && settings.IsGlobalBackgroundPreview);
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
        games?.RefreshLocalizedText();
        if (games is not null)
        {
            foreach (var entry in games.Entries)
            {
                if (ReferenceEquals(entry.Context, games.Active))
                {
                    continue;
                }

                entry.Context.Shell.ApplyLanguage(language, entry.Context.Snapshot is not null);
                if (entry.Context.Snapshot is { } snapshot)
                {
                    entry.Context.Shell.ApplySnapshot(snapshot, settings);
                }
            }
        }
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
