using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Services;
using CommunityToolkit.Mvvm.Input;

namespace Cafe.Launcher.Avalonia.ViewModels;

public partial class ToastHostViewModel : ViewModelBase, IDisposable
{
    private readonly ToastService toastService;
    private readonly LocalizationService localizer;
    private readonly SettingsViewModel settings;
    private readonly Func<Action, Task> invokeOnUiAsync;
    private readonly Func<TimeSpan, CancellationToken, Task> delayAsync;
    private readonly CancellationTokenSource lifetimeCts = new();
    private bool reduceMotion = true;
    private bool disposed;

    public ObservableCollection<ToastNotification> ActiveToasts { get; } = [];

    public ToastHostViewModel(ToastService toastService, LocalizationService localizer, SettingsViewModel settings)
        : this(
            toastService,
            localizer,
            settings,
            async action => await Dispatcher.UIThread.InvokeAsync(action),
            static (delay, cancellationToken) => Task.Delay(delay, cancellationToken))
    {
    }

    internal ToastHostViewModel(
        ToastService toastService,
        LocalizationService localizer,
        SettingsViewModel settings,
        Func<Action, Task> invokeOnUiAsync,
        Func<TimeSpan, CancellationToken, Task> delayAsync)
    {
        this.toastService = toastService;
        this.localizer = localizer;
        this.settings = settings;
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
    private Task DismissToastAsync(string toastId) =>
        ExitToastAsync(toastId, lifetimeCts.Token);

    private void OnToastRaised(ToastNotification notification)
    {
        notification.SeverityLabel = notification.Severity switch
        {
            ToastSeverity.Success => localizer.T("toastSuccess"),
            ToastSeverity.Warning => localizer.T("toastWarning"),
            ToastSeverity.Error => localizer.T("toastError"),
            _ => localizer.T("toastInfo")
        };
        _ = ShowToastAsync(notification, lifetimeCts.Token);
    }

    private async Task ShowToastAsync(ToastNotification notification, CancellationToken cancellationToken)
    {
        if (!settings.Editor.GetSavedSnapshot().ToastNotificationsEnabled)
        {
            return;
        }

        try
        {
            if (string.IsNullOrWhiteSpace(notification.Message))
            {
                return;
            }

            await invokeOnUiAsync(() => ActiveToasts.Add(notification));
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
            System.Diagnostics.Debug.WriteLine(
                $"ToastHost: toast notification lifecycle failed: {ex.Message}");
        }
    }

    private async Task ExitToastAsync(string toastId, CancellationToken cancellationToken)
    {
        ToastNotification? exitingToast = null;
        var shouldWaitForAnimation = false;
        await invokeOnUiAsync(
            () =>
            {
                var toast = ActiveToasts.FirstOrDefault(candidate => candidate.Id == toastId);
                if (toast is null || toast.IsExiting)
                {
                    return;
                }

                if (reduceMotion)
                {
                    ActiveToasts.Remove(toast);
                    return;
                }

                toast.IsExiting = true;
                exitingToast = toast;
                shouldWaitForAnimation = true;
            });

        if (!shouldWaitForAnimation || exitingToast is null)
        {
            return;
        }

        try
        {
            await delayAsync(AnimationTimings.ExitAnimationDuration, cancellationToken);
            await invokeOnUiAsync(
                () => ActiveToasts.Remove(exitingToast));
        }
        catch (OperationCanceledException exception) when (
            exception.CancellationToken == cancellationToken
            && cancellationToken.IsCancellationRequested)
        {
            // The host lifetime ended while the exit animation was pending.
        }
    }

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
