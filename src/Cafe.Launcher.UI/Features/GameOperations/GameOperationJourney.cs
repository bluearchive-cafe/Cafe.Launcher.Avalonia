using System;
using System.IO;
using Cafe.Launcher.Core.Helpers;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.UI.Constants;
using Cafe.Launcher.UI.Helpers;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.Core.Services.Diagnostics;
using Cafe.Launcher.Core.Services.GameRuntime;
using Cafe.Launcher.Core.Models;

namespace Cafe.Launcher.UI.Features.GameOperations;

/// <summary>
/// Owns the journey rules for launch, install, repair, and uninstall:
/// state validation, confirmations, retry, refresh, and notifications.
/// The presentation module (GameOperationsViewModel) stays pure — state
/// binding, progress mapping, and thin command delegation — and drives
/// refresh / log-viewer / minimize through the host interface.
/// </summary>
internal sealed class GameOperationJourney : IDisposable
{
    /// <summary>Forwards installation running-state changes to the presentation host.</summary>
    public event Action? IsRunningChanged
    {
        add => executor.IsRunningChanged += value;
        remove => executor.IsRunningChanged -= value;
    }

    private readonly IGameOperationExecutor executor;
    private readonly IGameShortcutService shortcutService;
    private readonly IGameSessionMonitor sessionMonitor;
    private readonly Func<TimeSpan, Task> delayAsync;
    private readonly LocalizationService localizer;
    private readonly ToastService toastService;
    private readonly ILauncherDiagnostics diagnostics;
    private readonly IErrorHandlingService errorHandling;
    private readonly IGameOperationJourneyHost host;

    private readonly ActivityLifetime lifetime = new();
    private int stopRequested;
    private long uninstallRequestId;

    /// <summary>关闭开始后不再接收新操作，已请求停止的任务仍须完成其 finally 与呈现收尾。</summary>
    public bool IsShuttingDown => lifetime.IsShuttingDown;

    /// <summary>封闭入口、按进程退出意图停止下载，并异步等待所有已进入的下载旅程完成。</summary>
    public Task PrepareForShutdownAsync()
    {
        Task pending = lifetime.BeginShutdown();

        // 停止只做一次：重复的停机请求只等同一个排空任务。Cancel 会同步触发回调，
        // 也不能用 IsDownloadRunning 判断是否需要等待：用户停止会先摘除会话，
        // 此时其异常处理与 finally 仍可能在运行。
        if (Interlocked.Exchange(ref stopRequested, 1) == 0)
        {
            executor.Stop(DownloadStopReason.ApplicationExit);
        }

        return pending;
    }

    /// <summary>释放后取消生命周期令牌；在飞操作的租约仍会正常释放。</summary>
    public void Dispose() => lifetime.Dispose();

    private IDisposable? TryBeginOperation() => lifetime.TryBegin();

    /// <summary>
    /// The toast gets one short beat to be read before the window is taken away by the
    /// minimize-to-tray or exit that follows; both post-launch arms share the same beat.
    /// </summary>
    private static readonly TimeSpan AfterLaunchToastBeat = TimeSpan.FromMilliseconds(600);

    /// <summary>Initializes the execution seam and presentation host for game operations.</summary>
    public GameOperationJourney(
        IGameOperationExecutor executor,
        IGameShortcutService shortcutService,
        IGameSessionMonitor sessionMonitor,
        LocalizationService localizer,
        ToastService toastService,
        ILauncherDiagnostics diagnostics,
        IErrorHandlingService errorHandling,
        Func<TimeSpan, Task> delayAsync,
        IGameOperationJourneyHost host)
    {
        this.executor = executor;
        this.shortcutService = shortcutService;
        this.sessionMonitor = sessionMonitor;
        this.delayAsync = delayAsync;
        this.localizer = localizer;
        this.toastService = toastService;
        this.diagnostics = diagnostics;
        this.errorHandling = errorHandling;
        this.host = host;
    }

    /// <summary>Gets whether a download or repair workflow is currently running.</summary>
    public bool IsDownloadRunning => executor.IsDownloadRunning;

    /// <summary>Gets whether the active download workflow is paused.</summary>
    public bool IsPaused => executor.IsPaused;

    /// <summary>Starts the game after validating the supplied launcher state.</summary>
    public async Task StartGameAsync(LauncherStatusSnapshot snapshot)
    {
        if (!PrepareShellOnly(snapshot))
        {
            return;
        }

        await RunBusyAsync("Game launch failed.", LocalizationKeys.GameLaunchFailed, async () =>
        {
            var admission = GameOperationAttempt.Evaluate(GameOperationPolicy.Operation.Launch, snapshot);
            var launchResult = admission.Attempt is { } attempt
                ? await executor.LaunchAsync(attempt)
                : GameOperationAttempt.RejectLaunch(snapshot, localizer);
            host.Present(new GameOperationPresentation.LaunchCheck(launchResult.Validation.Message));
            var launchDiagnostic = BuildLaunchDiagnostic(launchResult);

            if (launchResult.Success)
            {
                await diagnostics.MessageAsync("GameLaunch", launchDiagnostic);
                // 会话看护从这一刻接管「游戏到底起没起来」：原生启动时宿主即游戏，
                // 运行器启动时要等游戏进程家族现身（ADR-035）。
                sessionMonitor.BeginSession(launchResult.RunnerId ?? "", launchResult.KnownExeNames);
                await ApplyAfterLaunchBehaviorAsync(snapshot.Settings.AfterLaunchBehavior);
            }
            else
            {
                // Launch verification found damaged files: offer repair directly, matching the
                // official launcher's damage prompt, instead of a toast the user has to act on by
                // finding the repair command themselves. The repair is the full CRC-64 pass rather
                // than the official's manifest-diff shortcut, because this check only compares
                // sizes — a same-size but corrupt file needs hashing to be caught.
                if (launchResult.Validation.HasDamagedFiles)
                {
                    host.Present(new GameOperationPresentation.RepairConfirmation(localizer.T(LocalizationKeys.LaunchDamageRepairPrompt)));
                }
                else
                {
                    toastService.ShowWarning(launchResult.Message);
                }

                await diagnostics.WarningAsync("GameLaunch", launchDiagnostic);
                if (launchResult.DiagnosticException is not null)
                {
                    await diagnostics.ErrorAsync("GameLaunch", launchResult.DiagnosticException);
                }
            }
        });
    }

    /// <summary>
    /// Applies the user's post-launch preference. Minimizing to tray is the shipped behavior, so an
    /// unrecognized value falls back to it — normalization already rejects unknown codes, and the
    /// default arm only covers a snapshot built in code rather than read from disk.
    /// </summary>
    private async Task ApplyAfterLaunchBehaviorAsync(string behavior)
    {
        switch (behavior)
        {
            case AfterLaunchBehaviors.KeepOpen:
                toastService.ShowSuccess(localizer.T(LocalizationKeys.GameLaunched));
                break;
            case AfterLaunchBehaviors.Exit:
                // The window is about to disappear, so the toast gets one short beat to be read
                // before the process goes away — the same beat the minimize path gives itself.
                toastService.ShowSuccess(localizer.T(LocalizationKeys.GameLaunchedExiting));
                await delayAsync(AfterLaunchToastBeat);
                host.Present(new GameOperationPresentation.Window(GameOperationWindowAction.Exit));
                break;
            default:
                toastService.ShowSuccess(localizer.T(LocalizationKeys.GameLaunchedMinimized));
                await delayAsync(AfterLaunchToastBeat);
                host.Present(new GameOperationPresentation.Window(GameOperationWindowAction.Minimize));
                break;
        }
    }

    /// <summary>
    /// 会话看护判成「启动失败」后的呈现规则（ADR-035）：先恢复窗口再报错——Toast 渲染在
    /// 窗口内，窗口还最小化在托盘就等于没报。退出码原样透传，不做成败解读之外的修辞；
    /// 游戏的「正常退出」不走这里，只在状态行静默更新。
    /// </summary>
    public void NotifySessionStartFailed(GameLaunchExitInfo? exit)
    {
        host.Present(new GameOperationPresentation.Window(GameOperationWindowAction.Show));
        toastService.ShowError(localizer.F(LocalizationKeys.GameSessionStartFailed, exit?.ExitCode ?? -1));
        _ = diagnostics.WarningAsync(
            "GameLaunch",
            $"Session monitor: the host process exited with code {exit?.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"} "
            + $"after {exit?.Duration.TotalSeconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"} s "
            + "and the game process family never appeared.");
    }

    /// <summary>Refreshes launcher state and reports whether a game update is available.</summary>
    public async Task CheckForUpdateAsync(LauncherStatusSnapshot snapshot)
    {
        if (!PrepareShellOnly(snapshot))
        {
            return;
        }

        await RunBusyAsync("Game update check failed.", LocalizationKeys.GameCheckUpdateFailed, async () =>
        {
            await RequestRefresh(GameOperationsRefreshMode.SkipPersistedResume);
            ReportUpdateCheck(host.CurrentSnapshot ?? snapshot);
        });
    }

    /// <summary>Creates the desktop shortcut for the installed game and reports the outcome.</summary>
    public async Task CreateDesktopShortcutAsync(LauncherStatusSnapshot snapshot)
    {
        if (!PrepareShellOnly(snapshot))
        {
            return;
        }

        await CreateDesktopShortcutCoreAsync(snapshot);
    }

    private async Task CreateDesktopShortcutCoreAsync(LauncherStatusSnapshot snapshot)
    {
        await RunBusyAsync("Desktop shortcut creation failed.", LocalizationKeys.GameShortcutFailed, async () =>
        {
            var result = await shortcutService.CreateDesktopShortcutAsync(snapshot);
            switch (result.Status)
            {
                case GameShortcutStatus.Created:
                    toastService.ShowSuccess(localizer.T(LocalizationKeys.GameShortcutCreated));
                    break;
                case GameShortcutStatus.UnsupportedPlatform:
                    toastService.ShowWarning(localizer.T(LocalizationKeys.GameShortcutUnsupported));
                    break;
                case GameShortcutStatus.GameNotResolved:
                    toastService.ShowWarning(localizer.T(LocalizationKeys.GameShortcutTargetMissing));
                    break;
                case GameShortcutStatus.Failed:
                    toastService.ShowError(localizer.F(LocalizationKeys.GameShortcutFailed, result.Detail));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(snapshot), result.Status, null);
            }
        });
    }

    /// <summary>Opens the installed game folder in the platform file manager.</summary>
    public void OpenGameFolder(LauncherStatusSnapshot snapshot)
    {
        if (!PrepareShellOnly(snapshot))
        {
            return;
        }

        try
        {
            if (!shortcutService.TryOpenGameFolder(snapshot))
            {
                toastService.ShowWarning(localizer.T(LocalizationKeys.GameFolderMissing));
            }
        }
        catch (Exception exception)
        {
            _ = errorHandling.HandleErrorAsync("Opening the game folder failed.", exception,
                new ErrorHandlingOptions { ToastMessage = localizer.F(LocalizationKeys.UnexpectedError, exception.Message) });
        }
    }

    /// <summary>Installs or updates the game and presents the terminal result.</summary>
    public async Task InstallOrUpdateAsync(LauncherStatusSnapshot snapshot, bool createDesktopShortcut = false)
    {
        using var operation = TryBeginOperation();
        if (operation is null)
        {
            return;
        }

        var result = await RunInstallOrUpdateAttemptAsync(snapshot);
        if (result is null)
        {
            return;
        }

        if (result.Success && createDesktopShortcut && !IsShuttingDown)
        {
            // Keep this follow-through inside the operation lifetime; shortcut failure cannot fail installation.
            var installed = host.CurrentSnapshot;
            if (installed is not null && GamePathValidator.PathComparer.Equals(installed.LocalGame.GamePath, snapshot.LocalGame.GamePath))
                await CreateDesktopShortcutCoreAsync(installed);
        }
        ShowOperationResult(result);
    }

    /// <summary>
    /// 请求修复：先过「当前状态还允许修复吗」的闸门，再经宿主打开确认框。闸门与确认后那道同址
    /// （见 <see cref="RepairAsync"/>），展示层不再各写一遍。
    /// </summary>
    public Task RequestRepairAsync(LauncherStatusSnapshot snapshot)
    {
        if (IsShuttingDown || RejectRepairIfUnavailable(snapshot))
        {
            return Task.CompletedTask;
        }

        host.Present(new GameOperationPresentation.RepairConfirmation(localizer.T(LocalizationKeys.RepairWarning)));
        return Task.CompletedTask;
    }

    /// <summary>Runs a confirmed repair and refreshes launcher state afterward.</summary>
    public async Task RepairAsync(LauncherStatusSnapshot snapshot)
    {
        using var operation = TryBeginOperation();
        if (operation is null)
        {
            return;
        }

        // 用户已经在确认框上点过确认：此时状态若又变得不允许，必须给可见反馈（ADR-027）。
        // 这道闸门必须早于 PrepareOperation，否则拒绝也会启动任务呈现并覆盖上一项结果。
        var admission = GameOperationAttempt.Evaluate(GameOperationPolicy.Operation.Repair, snapshot);
        if (admission.Attempt is not { } attempt)
        {
            ShowOperationUnavailable();
            return;
        }

        if (!PrepareOperation(snapshot, GameOperationKind.Repair))
        {
            return;
        }

        var refreshHandled = false;
        GameOperationResult? result = null;
        try
        {
            result = await executor.RepairAsync(attempt, progress => host.Present(new GameOperationPresentation.Progress(progress)));
            refreshHandled = await RefreshAfterTaskAsync();
        }
        catch (Exception exception)
        {
            await errorHandling.HandleErrorAsync("Game repair failed.", exception,
                new ErrorHandlingOptions { ShowToast = false });
            result = new GameOperationResult { Message = localizer.F(LocalizationKeys.GameRepairFailed, exception.Message), ErrorCode = GameOperationErrorCode.System };
        }
        finally
        {
            host.Present(new GameOperationPresentation.Busy(false));
            if (!refreshHandled)
            {
                ApplySnapshotSafe(snapshot);
            }
        }
        if (result is not null) ShowOperationResult(result);
    }

    /// <summary>
    /// Validates uninstall eligibility and reports the affected file count.
    /// 预检失败时就地报出执行层给出的具体原因，并返回 null（ADR-029）：调用方拿到 null
    /// 即中止流程、无需再表态——拒绝在原地可见，与 ADR-027 对确认后拒绝的处理同构。
    /// </summary>
    public async Task<GameOperationResult?> ValidateUninstallAsync(LauncherStatusSnapshot snapshot)
    {
        if (GameOperationAttempt.Evaluate(GameOperationPolicy.Operation.Uninstall, snapshot).Decision
            == GameOperationDecision.RejectedForCurrentState)
        {
            ShowOperationUnavailable();
            return null;
        }
        var validation = await executor.ValidateUninstallAsync(snapshot.LocalGame.GamePath);
        if (validation.Success)
        {
            return validation;
        }

        // 执行层的失败结果自带可执行的本地化原因（路径缺失／受保护／目录名非法／元数据缺失），
        // 没有原因时才退回笼统的「当前状态不可用」，避免弹出一个没有内容的提示。
        if (string.IsNullOrWhiteSpace(validation.Message))
        {
            ShowOperationUnavailable();
        }
        else
        {
            toastService.ShowWarning(validation.Message);
        }

        return null;
    }

    /// <summary>
    /// 游戏目录与可选受管兼容环境的实测大小（ADR-046），供确认框展示；与删除目标同源。
    /// </summary>
    public Task<UninstallFootprint> MeasureUninstallFootprintAsync(LauncherStatusSnapshot snapshot) =>
        executor.MeasureUninstallFootprintAsync(snapshot);

    /// <summary>Owns uninstall admission and preflight; emits confirmation before waiting for size measurement.</summary>
    public async Task RequestUninstallAsync(LauncherStatusSnapshot snapshot)
    {
        if (!PrepareShellOnly(snapshot)) return;
        var requestId = ++uninstallRequestId;
        if (await ValidateUninstallAsync(snapshot) is null || IsShuttingDown) return;
        host.Present(new GameOperationPresentation.UninstallConfirmation(requestId, snapshot));
        var size = await MeasureUninstallFootprintAsync(snapshot);
        if (!IsShuttingDown)
            host.Present(new GameOperationPresentation.UninstallSize(requestId, size));
    }

    /// <summary>Runs a confirmed uninstall and refreshes launcher state afterward.</summary>
    public async Task<GameOperationResult> ConfirmUninstallAsync(LauncherStatusSnapshot snapshot, UninstallScope scope)
    {
        var admission = GameOperationAttempt.Evaluate(GameOperationPolicy.Operation.Uninstall, snapshot);
        if (admission.Attempt is not { } attempt)
        {
            var rejected = GameOperationRejections.UnavailableResult(localizer);
            host.Present(new GameOperationPresentation.UninstallCompleted(rejected));
            return rejected;
        }

        host.Present(new GameOperationPresentation.UninstallExecuting());
        host.Present(new GameOperationPresentation.Busy(true));
        try
        {
            host.Present(new GameOperationPresentation.Preparing());
            var result = await executor.UninstallAsync(attempt, scope, progress => host.Present(new GameOperationPresentation.Progress(progress)));
            // 刷新失败不改写已经确定的删除终态；用户始终能在原表面查看实际结果。
            try
            {
                await RequestRefresh(GameOperationsRefreshMode.Normal);
            }
            catch (Exception exception)
            {
                await diagnostics.ErrorAsync("GameUninstall", "Refreshing launcher state after uninstall failed.", exception);
            }
            host.Present(new GameOperationPresentation.UninstallCompleted(result));
            return result;
        }
        catch (Exception exception)
        {
            // 与执行层同一口径：异常原文只进日志，界面只说本地化的原因与下一步。
            await diagnostics.ErrorAsync("GameUninstall", "Game uninstall failed.", exception);
            var failed = GameOperationOutcomes.Failed(
                localizer.T(LocalizationKeys.UninstallFailureGeneric), GameOperationErrorCode.System);
            host.Present(new GameOperationPresentation.UninstallCompleted(failed));
            return failed;
        }
        finally
        {
            host.Present(new GameOperationPresentation.Busy(false));
        }
    }

    private void ShowOperationUnavailable() =>
        GameOperationRejections.WarnUnavailable(localizer, toastService);

    /// <summary>
    /// 全新安装落到一个已有内容的目录上时，先说明「启动器只管清单内那部分」。判据是两件事
    /// 同时成立：状态是「未安装」（启动器不认这份安装），而目录里已经有条目（盘上还有东西）。
    /// 单独任何一件都不足以提示——空目录上的全新安装没有任何意外，已安装状态走的是更新。
    /// </summary>
    private void NotifyInstallOverExistingContent(LauncherStatusSnapshot snapshot)
    {
        if (snapshot.RuntimeState != LauncherRuntimeState.NotInstalled)
        {
            return;
        }

        var gamePath = snapshot.LocalGame.GamePath;
        if (!InstallDirectoryContent.HasEntries(gamePath))
        {
            return;
        }

        toastService.Show(localizer.F(LocalizationKeys.InstallOverExistingContentNotice, gamePath));
    }

    /// <summary>
    /// 修复的两道闸门（请求时与确认后）共用同一判据与同一处渲染：返回 true 表示已拒绝并已报出，
    /// 调用方直接中止。策略判定仍归 <see cref="GameOperationPolicy.Decide"/>，这里只表态「被拒绝
    /// 对修复意味着什么」——就地警告并停下（ADR-027）。
    /// </summary>
    private bool RejectRepairIfUnavailable(LauncherStatusSnapshot snapshot)
    {
        if (GameOperationAttempt.Evaluate(GameOperationPolicy.Operation.Repair, snapshot).Decision
            != GameOperationDecision.RejectedForCurrentState)
        {
            return false;
        }

        ShowOperationUnavailable();
        return true;
    }

    /// <summary>Executes the stop after the confirmation flow has completed.</summary>
    public void PerformStop()
    {
        executor.Stop(DownloadStopReason.UserRequested);
    }

    /// <summary>Attempts to continue a persisted download while respecting cancellation.</summary>
    public async Task ResumePersistedAsync(LauncherStatusSnapshot snapshot, CancellationToken cancellationToken)
    {
        using var operation = TryBeginOperation();
        if (operation is null)
        {
            return;
        }

        if (host.IsBusy)
        {
            return;
        }

        var started = 0;
        GameOperationResult? completed = null;
        try
        {
            host.Present(new GameOperationPresentation.Busy(true));
            var result = await executor.ResumePersistedAsync(
                snapshot,
                progress =>
                {
                    if (Interlocked.CompareExchange(ref started, 1, 0) == 0)
                    {
                        host.Present(new GameOperationPresentation.Preparing(progress.OperationKind, Automatic: true));
                    }
                    host.Present(new GameOperationPresentation.Progress(progress));
                },
                cancellationToken);
            if (result is null)
            {
                return;
            }

            if (Volatile.Read(ref started) == 0) host.Present(new GameOperationPresentation.Preparing(GameOperationKind.Download, Automatic: true));
            completed = result;
            await RefreshAfterTaskAsync();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (Volatile.Read(ref started) != 0) completed = new GameOperationResult { Message = localizer.T(LocalizationKeys.OperationStopped), ErrorCode = GameOperationErrorCode.Stopped };
        }
        catch (Exception exception)
        {
            await errorHandling.HandleErrorAsync("Persisted game download resume failed.", exception,
                new ErrorHandlingOptions { ShowToast = false });
            if (Volatile.Read(ref started) != 0) completed = new GameOperationResult { Message = localizer.F(LocalizationKeys.GameInstallOrUpdateFailed, exception.Message), ErrorCode = GameOperationErrorCode.System };
        }
        finally
        {
            host.Present(new GameOperationPresentation.Busy(false));
        }
        if (completed is not null) ShowOperationResult(completed);
    }

    /// <summary>Stops the active workflow, optionally clearing its persisted checkpoint.</summary>
    public void Stop(DownloadStopReason reason)
    {
        executor.Stop(reason);
    }

    /// <summary>Pauses the active download workflow.</summary>
    public void Pause()
    {
        executor.Pause();
    }

    /// <summary>Resumes the active download workflow.</summary>
    public void Resume()
    {
        executor.Resume();
    }

    private async Task<GameOperationResult?> RunInstallOrUpdateAttemptAsync(LauncherStatusSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        if (!PrepareShellOnly(snapshot))
        {
            return null;
        }

        var refreshHandled = false;
        var taskStarted = false;
        try
        {
            // 允许集只读策略表；拒绝后「对用户意味着什么」仍按状态就地分流——
            // 损坏给修复入口，IO/远端失败重读状态，其余状态报不可用（ADR-027）。
            var admission = GameOperationAttempt.Evaluate(GameOperationPolicy.Operation.InstallOrUpdate, snapshot);
            if (admission.Attempt is not { } attempt)
            {
                switch (snapshot.RuntimeState)
                {
                    case LauncherRuntimeState.Corrupted:
                        host.Present(new GameOperationPresentation.RepairConfirmation(localizer.T(LocalizationKeys.RepairWarning)));
                        return null;
                    case LauncherRuntimeState.IoFailure or LauncherRuntimeState.RemoteUnavailable:
                        refreshHandled = await RequestRefresh(GameOperationsRefreshMode.Normal);
                        return null;
                    default:
                        // 快照过期：旧安装结果仍可见时，当前状态可能已经变为 Ready。
                        // 拒绝必须报出来（ADR-027/029），同时保留旧结果。
                        ShowOperationUnavailable();
                        return null;
                }
            }

            // 装之前先说清楚「这次只会下清单里那部分」（2026-09-29 反馈轮）：卸载只删清单
            // 文件，重启安装时目录里往往还留着游戏自行下载的一大片，而清单里没有它们——
            // 于是用户看到的是「忙活一下就装好了」，因为他以为被删掉的东西其实从没被删过。
            host.Present(new GameOperationPresentation.Preparing());
            taskStarted = true;
            NotifyInstallOverExistingContent(snapshot);

            var result = await executor.InstallOrUpdateAsync(attempt, progress => host.Present(new GameOperationPresentation.Progress(progress)), cancellationToken);
            refreshHandled = await RefreshAfterTaskAsync();
            return result;
        }
        catch (Exception exception)
        {
            await errorHandling.HandleErrorAsync("Game install/update failed.", exception,
                new ErrorHandlingOptions
                {
                    ShowToast = !taskStarted,
                    ToastMessage = localizer.F(LocalizationKeys.GameInstallOrUpdateFailed, exception.Message)
                });
            if (!taskStarted) return null;
            return new GameOperationResult
            {
                Success = false,
                Message = localizer.F(LocalizationKeys.GameInstallOrUpdateFailed, exception.Message),
                ErrorCode = GameOperationErrorCode.System
            };
        }
        finally
        {
            host.Present(new GameOperationPresentation.Busy(false));
            if (!refreshHandled)
            {
                ApplySnapshotSafe(snapshot);
            }
        }
    }

    private void ShowOperationResult(GameOperationResult result)
    {
        if (!IsShuttingDown) host.Present(new GameOperationPresentation.Completed(result));
    }

    private async Task<bool> RefreshAfterTaskAsync()
    {
        try { return await RequestRefresh(GameOperationsRefreshMode.SkipPersistedResume); }
        catch (Exception exception)
        {
            await errorHandling.HandleErrorAsync("Post-operation refresh failed.", exception,
                new ErrorHandlingOptions { ShowToast = false });
            return false;
        }
    }

    private void ReportUpdateCheck(LauncherStatusSnapshot snapshot)
    {
        var latestVersion = snapshot.Remote.GameConfig?.GameLatestVersion ?? localizer.T(LocalizationKeys.Unknown);
        switch (snapshot.RuntimeState)
        {
            case LauncherRuntimeState.UpdateAvailable:
                toastService.Show(localizer.F(LocalizationKeys.GameCheckUpdateAvailable, latestVersion));
                break;
            case LauncherRuntimeState.Ready:
                toastService.ShowSuccess(localizer.F(LocalizationKeys.GameCheckUpdateUpToDate, latestVersion));
                break;
            case LauncherRuntimeState.RemoteUnavailable:
                toastService.ShowWarning(localizer.T(LocalizationKeys.GameRemoteStateUnavailable));
                break;
            case LauncherRuntimeState.NotInstalled:
                toastService.Show(localizer.T(LocalizationKeys.GameNotInstalled));
                break;
            case LauncherRuntimeState.BelowLowestVersion:
                toastService.ShowWarning(localizer.T(LocalizationKeys.GameBelowLowestVersion));
                break;
            case LauncherRuntimeState.Corrupted:
                toastService.ShowWarning(localizer.T(LocalizationKeys.GameCorruptedInstallationState));
                break;
            case LauncherRuntimeState.IoFailure:
                toastService.ShowWarning(localizer.T(LocalizationKeys.GameInstallationStateReadFailed));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(snapshot), snapshot.RuntimeState, null);
        }
    }

    private Task<bool> RequestRefresh(GameOperationsRefreshMode mode) =>
        IsShuttingDown ? Task.FromResult(false) : host.RefreshAsync(mode);

    /// <summary>
    /// 无后置处理的入口共用的骨架：进入忙碌、执行、把异常按 (日志上下文, 失败文案键) 报给用户、
    /// 退出忙碌。
    /// </summary>
    /// <remarks>
    /// 三个入口（启动游戏、检查更新、创建快捷方式）此前各写一遍同一段 try/catch/finally，差异只有
    /// 日志上下文与失败文案键。带 <c>refreshHandled</c> 后置处理或自定义
    /// <see cref="ErrorHandlingOptions"/> 的入口（修复、卸载、续传）仍在调用点显式展开——那些是
    /// 入口自己的语义，不是这段骨架的一部分。
    /// </remarks>
    private async Task RunBusyAsync(string errorContext, LocalizationKeys failureMessageKey, Func<Task> body)
    {
        host.Present(new GameOperationPresentation.Busy(true));

        try
        {
            await body();
        }
        catch (Exception exception)
        {
            await errorHandling.HandleErrorAsync(errorContext, exception,
                new ErrorHandlingOptions { ToastMessage = localizer.F(failureMessageKey, exception.Message) });
        }
        finally
        {
            host.Present(new GameOperationPresentation.Busy(false));
        }
    }

    private bool PrepareShellOnly(LauncherStatusSnapshot? snapshot)
    {
        if (IsShuttingDown || host.IsBusy)
        {
            return false;
        }

        if (snapshot is null)
        {
            return false;
        }

        return true;
    }

    private bool PrepareOperation(LauncherStatusSnapshot snapshot, GameOperationKind kind = GameOperationKind.Download)
    {
        if (!PrepareShellOnly(snapshot))
        {
            return false;
        }

        host.Present(new GameOperationPresentation.Preparing(kind));
        return true;
    }

    private void ApplySnapshotSafe(LauncherStatusSnapshot snapshot)
    {
        host.Present(new GameOperationPresentation.Snapshot(snapshot));
    }

    private static string BuildLaunchDiagnostic(GameLaunchResult launchResult)
    {
        if (string.IsNullOrWhiteSpace(launchResult.DiagnosticMessage)
            || string.Equals(launchResult.Message, launchResult.DiagnosticMessage, StringComparison.Ordinal))
        {
            return launchResult.Message;
        }

        return $"{launchResult.Message}{Environment.NewLine}{launchResult.DiagnosticMessage}";
    }
}
