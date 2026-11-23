using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Cafe.Launcher.UI.Constants;
using Cafe.Launcher.UI.Helpers;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.Core.Services.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cafe.Launcher.UI.ViewModels;

internal partial class ToastHostViewModel : ObservableObject, IDisposable
{
    /// <summary>栈内同时可见的通知上限；超出时淘汰最早且没有动作的一条（ADR-070）。</summary>
    private const int MaxActiveToasts = 4;

    private readonly ToastService toastService;
    private readonly LocalizationService localizer;
    private readonly ILauncherDiagnostics diagnostics;
    private readonly Func<Action, Task> invokeOnUiAsync;
    private readonly Func<TimeSpan, CancellationToken, Task> delayAsync;
    private readonly CancellationTokenSource lifetimeCts = new();
    // 令牌在构造时取出：Dispose 之后 fire-and-forget 的管线仍可能读到它，
    // 而 CancellationTokenSource.Token 在释放后会抛 ObjectDisposedException。
    private readonly CancellationToken lifetimeToken;
    private readonly Dictionary<string, ToastLifecycle> lifecycles = [];
    private bool reduceMotion = true;
    private bool disposed;

    public ObservableCollection<ToastNotification> ActiveToasts { get; } = [];

    /// <summary>Initializes the toast host with UI services supplied by dependency injection.</summary>
    public ToastHostViewModel(
        ToastService toastService,
        LocalizationService localizer,
        ILauncherDiagnostics diagnostics)
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
        ILauncherDiagnostics diagnostics,
        Func<Action, Task> invokeOnUiAsync,
        Func<TimeSpan, CancellationToken, Task> delayAsync)
    {
        this.toastService = toastService;
        this.localizer = localizer;
        this.diagnostics = diagnostics;
        this.invokeOnUiAsync = invokeOnUiAsync;
        this.delayAsync = delayAsync;
        lifetimeToken = lifetimeCts.Token;
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

    /// <summary>Gets whether the display countdown of <paramref name="toastId"/> is suspended.</summary>
    internal bool IsCountdownSuspended(string toastId) =>
        lifecycles.TryGetValue(toastId, out var lifecycle)
        && lifecycle.Countdown is { Suspended: true };

    /// <summary>
    /// Gets whether the display countdown of <paramref name="toastId"/> is parked waiting for the
    /// pointer to leave its card.
    /// </summary>
    internal bool IsCountdownAwaitingResume(string toastId) =>
        lifecycles.TryGetValue(toastId, out var lifecycle)
        && lifecycle.Countdown is { Resumed: not null };

    /// <summary>Gets the in-flight display countdown task of <paramref name="toastId"/>, if any.</summary>
    internal Task? PendingCountdownTask(string toastId) =>
        lifecycles.TryGetValue(toastId, out var lifecycle)
            ? lifecycle.CountdownTask
            : null;

    /// <summary>
    /// Suspends the display countdown while the toast is hovered or contains keyboard focus.
    /// When both are clear, the countdown restarts with its full duration.
    /// </summary>
    /// <remarks>Call on the UI thread.</remarks>
    internal void SetToastInteractionActive(string toastId, bool isActive)
    {
        if (disposed
            || !lifecycles.TryGetValue(toastId, out var lifecycle)
            || lifecycle.Countdown is not { } countdown
            || countdown.Suspended == isActive)
        {
            return;
        }

        countdown.Suspended = isActive;
        if (isActive)
        {
            countdown.Interruption?.Cancel();
            return;
        }

        ResumeCountdown(countdown);
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task DismissToastAsync(string toastId)
    {
        if (disposed)
        {
            return;
        }

        CancelActionToken(toastId);
        var canDismiss = false;
        await invokeOnUiAsync(() =>
        {
            var toast = ActiveToasts.FirstOrDefault(candidate => candidate.Id == toastId);
            canDismiss = toast is not null;
        });
        if (canDismiss && !disposed)
        {
            await ExitToastAsync(toastId, lifetimeToken);
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
        if (disposed)
        {
            return;
        }

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

        _ = ShowToastAsync(notification, lifetimeToken);
    }

    private async Task ShowToastAsync(ToastNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            if (disposed || string.IsNullOrWhiteSpace(notification.Message))
            {
                return;
            }

        var lifecycle = new ToastLifecycle();
        var merged = false;
        string? evictedToastId = null;
        await invokeOnUiAsync(() =>
        {
            // 同 key 的重复通知不新增卡片：并入已有卡片计数并刷新内容（ADR-070）。
            merged = TryMergeIntoExistingCard(notification);
            if (merged)
            {
                return;
            }

            // 记录的建立与拆除各只有一处：随提示条进入栈建立，随其离开栈拆除。
            if (!notification.HasActions)
            {
                lifecycle.Countdown = new ToastCountdown();
            }

            lifecycles[notification.Id] = lifecycle;
            ActiveToasts.Insert(0, notification);

            // 超出上限时淘汰最早且没有动作的卡片；带动作的入口不参与淘汰（ADR-070）。
            if (ActiveToasts.Count > MaxActiveToasts)
            {
                evictedToastId = FindEarliestEvictableToastId(notification.Id);
            }
        });
        if (merged)
        {
            return;
        }

        if (evictedToastId is { } toastId)
        {
            _ = ExitToastAsync(toastId, lifetimeToken);
        }

        if (notification.HasActions)
        {
            return;
        }

        lifecycle.CountdownTask = RunDisplayCountdownAsync(notification, cancellationToken);
        await lifecycle.CountdownTask;
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

    /// <summary>
    /// 把带 key 的通知并入栈里同 key 的卡片：计数加一、消息换为最新一条、倒计时按原档位
    /// 重新计满；标题保留第一条的，并在计数超过一时追加本地化的「 ×N」后缀。
    /// </summary>
    /// <remarks>只动已有卡片，不新增、不拆除记录；带动作的卡片没有倒计时，只更新内容。</remarks>
    private bool TryMergeIntoExistingCard(ToastNotification notification)
    {
        if (notification.Key is not { } key)
        {
            return false;
        }

        var existing = ActiveToasts.FirstOrDefault(candidate => candidate.Key == key && !candidate.IsExiting);
        if (existing is null)
        {
            return false;
        }

        if (!lifecycles.TryGetValue(existing.Id, out var lifecycle))
        {
            return false;
        }

        lifecycle.MergeBaseTitle ??= existing.Title;
        // 重新计满：打断在途等待后，倒计时循环会按原档位重开一轮（与指针离开后的恢复一致）。
        lifecycle.Countdown?.Interruption?.Cancel();

        existing.MergeCount++;
        existing.Message = notification.Message;
        if (lifecycle.MergeBaseTitle is { } baseTitle)
        {
            existing.Title = baseTitle + string.Format(
                CultureInfo.CurrentCulture,
                localizer.T(LocalizationKeys.ToastMergeCountSuffix),
                existing.MergeCount);
        }

        return true;
    }

    /// <summary>
    /// Runs the display countdown of one toast until its duration elapses. The countdown is
    /// suspended while the pointer rests on the toast card: the wait in flight is interrupted and
    /// the loop, on its next pass, either waits for the pointer to leave or ends because the toast
    /// left the stack.
    /// </summary>
    private async Task RunDisplayCountdownAsync(ToastNotification toast, CancellationToken cancellationToken)
    {
        var duration = ToastDurations.Resolve(toast.Duration);
        while (true)
        {
            var interruption = await WaitForDisplayAttemptAsync(toast, cancellationToken);
            if (interruption is null)
            {
                return;
            }

            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                interruption.Token);
            try
            {
                await delayAsync(duration, attempt.Token);
                return;
            }
            catch (OperationCanceledException) when (
                interruption.IsCancellationRequested
                && !cancellationToken.IsCancellationRequested)
            {
                // Suspended by the pointer, or stopped along with the toast — re-evaluate.
            }
        }
    }

    /// <summary>
    /// Returns the interruption source for the next display attempt once the countdown may run,
    /// or <see langword="null"/> when it must end because the toast left the stack. A suspended
    /// countdown waits here until the pointer leaves the card.
    /// </summary>
    private async Task<CancellationTokenSource?> WaitForDisplayAttemptAsync(
        ToastNotification toast,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            CancellationTokenSource? interruption = null;
            TaskCompletionSource? resumed = null;
            await invokeOnUiAsync(() =>
            {
                if (!lifecycles.TryGetValue(toast.Id, out var lifecycle)
                    || lifecycle.Countdown is not { } countdown)
                {
                    return;
                }

                if (countdown.Suspended)
                {
                    resumed = countdown.Resumed ??= new TaskCompletionSource(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    return;
                }

                interruption = countdown.Interruption = new CancellationTokenSource();
            });

            if (interruption is not null)
            {
                return interruption;
            }

            if (resumed is null)
            {
                return null;
            }

            await resumed.Task.WaitAsync(cancellationToken);
        }
    }

    /// <summary>
    /// 栈满时挑选淘汰对象：从最早（栈底）向最新扫描，返回第一条既无动作也不在退场动画中的
    /// 卡片。带动作的卡片保留到用户处理；刚插入的这条不在候选之列。无可淘汰者时返回
    /// <see langword="null"/>，栈暂时超过上限。
    /// </summary>
    private string? FindEarliestEvictableToastId(string excludedToastId)
    {
        for (var index = ActiveToasts.Count - 1; index >= 0; index--)
        {
            var candidate = ActiveToasts[index];
            if (candidate.Id != excludedToastId
                && !candidate.HasActions
                && !candidate.IsExiting)
            {
                return candidate.Id;
            }
        }

        return null;
    }

    /// <summary>
    /// 拆除一条提示条的运行期状态：释放悬挂的倒计时等待，并丢弃整条记录。
    /// </summary>
    /// <remarks>
    /// 记录的建立与拆除各只有一处，因此不存在「某条拆除路径忘了动某一个字典」这回事——此前三个
    /// 按 id 平行的字典各有自己的拆除点，其中一个还只在无动作的提示条上建立。
    /// 在途动作的令牌刻意不在这里取消：它由动作自身的流程与 Dismiss 负责（与合并前一致）。
    /// </remarks>
    private void EndLifecycle(string toastId)
    {
        if (!lifecycles.Remove(toastId, out var lifecycle))
        {
            return;
        }

        if (lifecycle.Countdown is { } countdown)
        {
            countdown.Interruption?.Cancel();
            ResumeCountdown(countdown);
        }
    }

    private static void ResumeCountdown(ToastCountdown countdown)
    {
        var resumed = countdown.Resumed;
        countdown.Resumed = null;
        resumed?.TrySetResult();
    }

    private async Task ExecuteToastActionAsync(
        string toastId,
        Func<ToastNotification, ToastAction?> selectAction)
    {
        if (disposed)
        {
            return;
        }

        ToastNotification? toast = null;
        ToastAction? action = null;
        ToastLifecycle? lifecycle = null;
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
            lifecycle = lifecycles.TryGetValue(toastId, out var found) ? found : null;
        });

        if (toast is null || action is null || lifecycle is null || disposed)
        {
            return;
        }

        CancellationTokenSource? actionTimeoutCts = null;
        try
        {
            actionTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
            if (action.Timeout is { } timeout)
            {
                actionTimeoutCts.CancelAfter(timeout);
            }

            lifecycle.ActionToken = actionTimeoutCts;
            var result = await action.ExecuteAsync(actionTimeoutCts.Token);
            if (result.IsSuccess)
            {
                await ExitToastAsync(toast.Id, lifetimeToken);
                return;
            }

            await ApplyActionFailureAsync(toast, result.Message!, result.Title);
        }
        catch (OperationCanceledException) when (actionTimeoutCts?.IsCancellationRequested == true
            && !lifetimeToken.IsCancellationRequested)
        {
            // Dismiss killed the action — exit quietly.
            await ExitToastAsync(toast.Id, lifetimeToken);
        }
        catch (OperationCanceledException exception) when (
            exception.CancellationToken == lifetimeToken
            && lifetimeToken.IsCancellationRequested)
        {
            // Host lifetime ended while the action was running.
        }
        catch (Exception exception)
        {
            await diagnostics.ErrorAsync(
                "Toast",
                "Running the toast action failed.",
                exception,
                CancellationToken.None);
            await ApplyActionFailureAsync(
                toast,
                localizer.T(LocalizationKeys.ToastActionFailedMessage),
                localizer.T(LocalizationKeys.ToastActionFailedTitle));
        }
        finally
        {
            // 记录可能已在动作执行期间随提示条离开而被拆除：写回一个已脱离的对象无害，
            // 关键是不要再按 id 查字典——那会把条目重新种回一个已经不属于它的栈位置。
            lifecycle.ActionToken = null;
            actionTimeoutCts?.Dispose();
        }
    }

    private void CancelActionToken(string toastId)
    {
        if (lifecycles.TryGetValue(toastId, out var lifecycle)
            && lifecycle.ActionToken is { } token)
        {
            token.Cancel();
            lifecycle.ActionToken = null;
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

                if (!lifecycles.TryGetValue(toastId, out var lifecycle))
                {
                    lifecycle = new ToastLifecycle();
                    lifecycles[toastId] = lifecycle;
                }

                if (lifecycle.ExitCompletion is not null)
                {
                    exitCompletion = lifecycle.ExitCompletion;
                    return;
                }

                if (reduceMotion)
                {
                    ActiveToasts.Remove(toast);
                    EndLifecycle(toastId);
                    return;
                }

                toast.IsExiting = true;
                exitingToast = toast;
                exitCompletion = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                lifecycle.ExitCompletion = exitCompletion;
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
            EndLifecycle(toastId);
        });

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        toastService.ToastRaised -= OnToastRaised;
        // 只取消、不释放：已排队的 fire-and-forget 管线仍可能在这个令牌上注册回调，
        // 释放会把它们的收尾变成 ObjectDisposedException。宿主是应用级单例，进程退出即回收。
        lifetimeCts.Cancel();
    }

    /// <summary>
    /// 一条提示条在提示栈里的运行期状态。
    /// </summary>
    /// <remarks>
    /// 三个槽位各有自己的所有者与生命周期：倒计时从显示贯穿到离开（只有无动作的提示条会有）、
    /// 动作令牌只在动作执行期、退出信号只在退场动画期。收在一条记录里是为了让建立与拆除各有
    /// 一处，而不是因为三者寿命相同。提示条的 IsExiting 不在此处——它是模型上被 XAML 绑定的
    /// 属性，不随这条记录一起拆除。
    /// </remarks>
    private sealed class ToastLifecycle
    {
        public ToastCountdown? Countdown { get; set; }

        /// <summary>
        /// 首条标题的原文：合并只在原文上追加「 ×N」后缀，反复合并不会累积出「×2 ×3」。
        /// </summary>
        public string? MergeBaseTitle { get; set; }

        /// <summary>
        /// 测试缝：倒计时循环随提示条 fire-and-forget，这里是唯一能拿到它引用的地方。
        /// 拆除路径必须唤醒挂起的等待，否则这个任务会一直阻塞到宿主 Dispose——守卫用例盯的就是它。
        /// </summary>
        public Task? CountdownTask { get; set; }

        public CancellationTokenSource? ActionToken { get; set; }

        public TaskCompletionSource? ExitCompletion { get; set; }
    }

    /// <summary>
    /// Tracks the display countdown of one toast. Only the UI thread touches these members:
    /// the countdown task reads and writes them through <see cref="invokeOnUiAsync"/>, and the
    /// pointer handlers of the toast card call in from the UI thread.
    /// </summary>
    private sealed class ToastCountdown
    {
        /// <summary>Gets or sets whether the pointer currently rests on the toast card.</summary>
        public bool Suspended { get; set; }

        /// <summary>Gets or sets the wait that completes once a suspended countdown may continue.</summary>
        public TaskCompletionSource? Resumed { get; set; }

        /// <summary>Gets or sets the source that cancels the display wait currently in flight.</summary>
        public CancellationTokenSource? Interruption { get; set; }
    }
}
