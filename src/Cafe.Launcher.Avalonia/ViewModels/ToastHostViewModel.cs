using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using CommunityToolkit.Mvvm.Input;

namespace Cafe.Launcher.Avalonia.ViewModels;

public partial class ToastHostViewModel : ViewModelBase, IDisposable
{
    private readonly ToastService toastService;
    private readonly LocalizationService localizer;
    private readonly LocalDiagnostics diagnostics;
    private readonly Func<Action, Task> invokeOnUiAsync;
    private readonly Func<TimeSpan, CancellationToken, Task> delayAsync;
    private readonly CancellationTokenSource lifetimeCts = new();
    private readonly Dictionary<string, TaskCompletionSource> exitCompletions = [];
    private readonly Dictionary<string, CancellationTokenSource> actionTokens = [];
    private bool reduceMotion = true;
    private bool disposed;

    public ObservableCollection<ToastNotification> ActiveToasts { get; } = [];

    /// <summary>Initializes the toast host with UI services supplied by dependency injection.</summary>
    public ToastHostViewModel(
        ToastService toastService,
        LocalizationService localizer,
        LocalDiagnostics diagnostics)
        : this(
            toastService,
            localizer,
            diagnostics,
            async action => await Dispatcher.UIThread.InvokeAsync(action),
            static (delay, cancellationToken) => Task.Delay(delay, cancellationToken))
    {
    }

    internal ToastHostViewModel(
        ToastService toastService,
        LocalizationService localizer,
        LocalDiagnostics diagnostics,
        Func<Action, Task> invokeOnUiAsync,
        Func<TimeSpan, CancellationToken, Task> delayAsync)
    {
        this.toastService = toastService;
        this.localizer = localizer;
        this.diagnostics = diagnostics;
        this.invokeOnUiAsync = invokeOnUiAsync;
        this.delayAsync = delayAsync;
        toastService.ToastRaised += OnToastRaised;
    }

    /// <summary>
    /// Controls whether subsequent Toast exits skip the exit animation delay.
    /// </summary>
    /// <param name="reduceMotion">
    /// <see langword="true"/> to remove subsequent Toasts immediately; otherwise,
    /// <see langword="false"/> to wait for the exit animation.
    /// </param>
    public void ApplyMotionPreference(bool reduceMotion)
    {
        this.reduceMotion = reduceMotion;
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task DismissToastAsync(string toastId)
    {
        CancelActionToken(toastId);
        var canDismiss = false;
        await invokeOnUiAsync(() =>
        {
            var toast = ActiveToasts.FirstOrDefault(candidate => candidate.Id == toastId);
            canDismiss = toast is not null;
        });
        if (canDismiss)
        {
            await ExitToastAsync(toastId, lifetimeCts.Token);
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task ExecutePrimaryToastActionAsync(string toastId) =>
        ExecuteToastActionAsync(toastId, static toast => toast.PrimaryAction);

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task ExecuteSecondaryToastActionAsync(string toastId) =>
        ExecuteToastActionAsync(toastId, static toast => toast.SecondaryAction);

    private void OnToastRaised(ToastNotification notification)
    {
        notification.SeverityLabel = notification.Severity switch
        {
            ToastSeverity.Success => localizer.T(LocalizationKeys.ToastSuccess),
            ToastSeverity.Warning => localizer.T(LocalizationKeys.ToastWarning),
            ToastSeverity.Error => localizer.T(LocalizationKeys.ToastError),
            _ => localizer.T(LocalizationKeys.ToastInfo)
        };
        if (string.IsNullOrWhiteSpace(notification.Title))
        {
            notification.Title = notification.SeverityLabel;
        }

        _ = ShowToastAsync(notification, lifetimeCts.Token);
    }

    private async Task ShowToastAsync(ToastNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(notification.Message))
            {
                return;
            }

            await invokeOnUiAsync(() => ActiveToasts.Insert(0, notification));
            if (notification.HasActions)
            {
                return;
            }

            await delayAsync(TimeSpan.FromMilliseconds(notification.DurationMs), cancellationToken);
            await ExitToastAsync(notification.Id, cancellationToken);
        }
        catch (OperationCanceledException exception) when (
            exception.CancellationToken == cancellationToken
            && cancellationToken.IsCancellationRequested)
        {
            // Toast lifecycle was cancelled — nothing to do.
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 取消已在上方过滤分支中重抛；此处为真实失败，需确保日志不被取消吞掉。
            await diagnostics.WarningAsync(
                "ToastLifecycleFailed",
                $"ToastHost: toast notification lifecycle failed: {ex.Message}",
                CancellationToken.None);
        }
    }

    private async Task ExecuteToastActionAsync(
        string toastId,
        Func<ToastNotification, ToastAction?> selectAction)
    {
        ToastNotification? toast = null;
        ToastAction? action = null;
        await invokeOnUiAsync(() =>
        {
            var candidate = ActiveToasts.FirstOrDefault(item => item.Id == toastId);
            if (candidate is null || candidate.IsExiting || candidate.IsActionExecuting)
            {
                return;
            }

            var selectedAction = selectAction(candidate);
            if (selectedAction is null)
            {
                return;
            }

            candidate.IsActionExecuting = true;
            toast = candidate;
            action = selectedAction;
        });

        if (toast is null || action is null)
        {
            return;
        }

        CancellationTokenSource? actionTimeoutCts = null;
        try
        {
            actionTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(lifetimeCts.Token);
            if (action.Timeout is { } timeout)
            {
                actionTimeoutCts.CancelAfter(timeout);
            }

            actionTokens[toast.Id] = actionTimeoutCts;
            var result = await action.ExecuteAsync(actionTimeoutCts.Token);
            if (result.IsSuccess)
            {
                await ExitToastAsync(toast.Id, lifetimeCts.Token);
                return;
            }

            await ApplyActionFailureAsync(toast, result.Message!, result.Title);
        }
        catch (OperationCanceledException) when (actionTimeoutCts?.IsCancellationRequested == true
            && !lifetimeCts.IsCancellationRequested)
        {
            // Dismiss killed the action — exit quietly.
            await ExitToastAsync(toast.Id, lifetimeCts.Token);
        }
        catch (OperationCanceledException exception) when (
            exception.CancellationToken == lifetimeCts.Token
            && lifetimeCts.IsCancellationRequested)
        {
            // Host lifetime ended while the action was running.
        }
        catch (Exception exception)
        {
            await diagnostics.ErrorAsync("Toast action failed.", exception, CancellationToken.None);
            await ApplyActionFailureAsync(
                toast,
                localizer.T(LocalizationKeys.ToastActionFailedMessage),
                localizer.T(LocalizationKeys.ToastActionFailedTitle));
        }
        finally
        {
            actionTokens.Remove(toast.Id);
            actionTimeoutCts?.Dispose();
        }
    }

    private void CancelActionToken(string toastId)
    {
        if (actionTokens.TryGetValue(toastId, out var cts))
        {
            cts.Cancel();
            actionTokens.Remove(toastId);
        }
    }

    private Task ApplyActionFailureAsync(ToastNotification toast, string message, string? title) =>
        invokeOnUiAsync(() =>
        {
            if (!ActiveToasts.Contains(toast))
            {
                return;
            }

            toast.Severity = ToastSeverity.Error;
            toast.SeverityLabel = localizer.T(LocalizationKeys.ToastError);
            if (!string.IsNullOrWhiteSpace(title))
            {
                toast.Title = title;
            }

            toast.Message = message;
            toast.IsActionExecuting = false;
        });

    private async Task ExitToastAsync(string toastId, CancellationToken cancellationToken)
    {
        ToastNotification? exitingToast = null;
        TaskCompletionSource? exitCompletion = null;
        var ownsExit = false;
        await invokeOnUiAsync(
            () =>
            {
                var toast = ActiveToasts.FirstOrDefault(candidate => candidate.Id == toastId);
                if (toast is null)
                {
                    return;
                }

                if (exitCompletions.TryGetValue(toastId, out var existingCompletion))
                {
                    exitCompletion = existingCompletion;
                    return;
                }

                if (reduceMotion)
                {
                    ActiveToasts.Remove(toast);
                    return;
                }

                toast.IsExiting = true;
                exitingToast = toast;
                exitCompletion = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                exitCompletions.Add(toastId, exitCompletion);
                ownsExit = true;
            });

        if (exitCompletion is null)
        {
            return;
        }

        if (!ownsExit)
        {
            try
            {
                await exitCompletion.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Cancellation swallowed — the owner handles cleanup.
            }
            return;
        }

        try
        {
            await delayAsync(AnimationTimings.ExitAnimationDuration, cancellationToken);
            await FinishExitOnUiThread(toastId, exitingToast!);
            exitCompletion.TrySetResult();
        }
        catch (OperationCanceledException exception) when (
            exception.CancellationToken == cancellationToken
            && cancellationToken.IsCancellationRequested)
        {
            // The host lifetime ended while the exit animation was pending.
            await FinishExitOnUiThread(toastId, exitingToast!);
            exitCompletion.TrySetResult();
        }
        catch (OperationCanceledException exception)
        {
            exitCompletion.TrySetCanceled(exception.CancellationToken);
            await exitCompletion.Task;
        }
        catch (Exception exception)
        {
            exitCompletion.TrySetException(exception);
            await exitCompletion.Task;
        }
    }

    private Task FinishExitOnUiThread(string toastId, ToastNotification toast) =>
        invokeOnUiAsync(() =>
        {
            ActiveToasts.Remove(toast);
            exitCompletions.Remove(toastId);
        });

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        toastService.ToastRaised -= OnToastRaised;
        lifetimeCts.Cancel();
        lifetimeCts.Dispose();
    }
}
