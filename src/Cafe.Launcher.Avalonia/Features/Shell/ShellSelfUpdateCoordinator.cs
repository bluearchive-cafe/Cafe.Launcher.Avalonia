using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Update;

namespace Cafe.Launcher.Avalonia.Features.Shell;

/// <summary>How an in-app self-update preparation run ended.</summary>
internal enum ShellSelfUpdateOutcome
{
    /// <summary>The staged package is verified and ready to apply.</summary>
    Ready,

    /// <summary>Verification or preparation failed; the release-page fallback applies.</summary>
    DownloadFailed,

    /// <summary>The user or the shell lifetime cancelled the run; the dialog returns to neutral.</summary>
    Cancelled,

    /// <summary>An unexpected exception escaped the run; the error pipeline should report it.</summary>
    UnexpectedError
}

/// <summary>What happened when the shell tried to hand a ready package to the helper.</summary>
internal enum ShellSelfUpdateApplyResult
{
    /// <summary>No verified preparation is pending; requesting apply is a no-op.</summary>
    NotReady,

    /// <summary>The helper refused the package; the dialog reports the failure.</summary>
    StartFailed,

    /// <summary>The helper owns the package now; the shell must shut down so it can apply.</summary>
    Started
}

/// <summary>
/// The shell's in-app self-update state machine (AUD-ARCH-001): one pending preparation
/// and its cancellation token, from the confirmed release to the moment the helper takes
/// over. The coordinator knows nothing about dialogs, toasts, or localization — it raises
/// narrow outcome events and the shell lifecycle maps them onto presentation and shutdown.
/// A re-run replaces the previous run; the shell lifetime token (linked into every run)
/// cancels whatever is in flight when the window closes.
/// </summary>
internal sealed class ShellSelfUpdateCoordinator
{
    private readonly LauncherSelfUpdateService selfUpdateService;
    private readonly IWindowsLauncherUpdateApplier updateApplier;
    private readonly CancellationToken lifetimeToken;
    private CancellationTokenSource? preparationCts;

    /// <summary>Raised synchronously when a run starts, before the first await.</summary>
    public event Action? PreparationStarted;

    /// <summary>Raised on the captured UI context as download progress arrives.</summary>
    public event Action<LauncherUpdateProgress>? ProgressReported;

    /// <summary>Raised once when a run reaches a terminal outcome.</summary>
    public event Action<ShellSelfUpdateOutcome, Exception?>? Finished;

    /// <summary>Gets the verified preparation awaiting <see cref="TryApplyPending"/>, if any.</summary>
    public LauncherSelfUpdatePreparation? PendingPreparation { get; private set; }

    /// <summary>Initializes the coordinator with the services it drives and the shell lifetime token.</summary>
    public ShellSelfUpdateCoordinator(
        LauncherSelfUpdateService selfUpdateService,
        IWindowsLauncherUpdateApplier updateApplier,
        CancellationToken lifetimeToken)
    {
        this.selfUpdateService = selfUpdateService;
        this.updateApplier = updateApplier;
        this.lifetimeToken = lifetimeToken;
    }

    /// <summary>Starts a download-and-verify run; outcomes arrive through <see cref="Finished"/>.</summary>
    public void Begin(string version, IReadOnlyList<ReleaseFile> files) => _ = RunAsync(version, files);

    /// <summary>Cancels the in-flight run, if any; the run reports <see cref="ShellSelfUpdateOutcome.Cancelled"/>.</summary>
    public void CancelPreparation() => preparationCts?.Cancel();

    /// <summary>
    /// Hands a ready preparation to the apply seam. Not-ready requests stay silent —
    /// there is nothing to report about a button press for an absent package.
    /// </summary>
    public ShellSelfUpdateApplyResult TryApplyPending()
    {
        var preparation = PendingPreparation;
        if (preparation is null || preparation.Status != LauncherSelfUpdatePreparationStatus.Ready)
        {
            return ShellSelfUpdateApplyResult.NotReady;
        }

        return updateApplier.TryStartApply(preparation)
            ? ShellSelfUpdateApplyResult.Started
            : ShellSelfUpdateApplyResult.StartFailed;
    }

    /// <summary>Cancels the in-flight run. The source is intentionally not disposed: in-flight
    /// code must still be able to read its token, and the next Begin reclaims it.</summary>
    public void Dispose() => CancelPreparation();

    private async Task RunAsync(string version, IReadOnlyList<ReleaseFile> files)
    {
        PendingPreparation = null;
        preparationCts?.Dispose();
        preparationCts = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
        var token = preparationCts.Token;
        PreparationStarted?.Invoke();
        try
        {
            // Progress<T> 把回调投递回 Begin 被调用时捕获的上下文，事件处理器因此
            // 始终在 UI 线程上运行——这里的 await 不得加 ConfigureAwait(false)。
            var progress = new Progress<LauncherUpdateProgress>(update => ProgressReported?.Invoke(update));
            var preparation = await selfUpdateService
                .PrepareAsync(files, version, progress, token)
                .ConfigureAwait(true);
            if (token.IsCancellationRequested)
            {
                // PrepareAsync 在取消后正常返回的窄竞态：没有失败可报告，也不把
                // 对话框从“准备中”改写成任何终态。
                return;
            }

            if (preparation.Status == LauncherSelfUpdatePreparationStatus.Ready)
            {
                PendingPreparation = preparation;
                Finished?.Invoke(ShellSelfUpdateOutcome.Ready, null);
                return;
            }

            Finished?.Invoke(ShellSelfUpdateOutcome.DownloadFailed, null);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            Finished?.Invoke(ShellSelfUpdateOutcome.Cancelled, null);
        }
        catch (Exception exception)
        {
            Finished?.Invoke(ShellSelfUpdateOutcome.UnexpectedError, exception);
        }
    }
}
