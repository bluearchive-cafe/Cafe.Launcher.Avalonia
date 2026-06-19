using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Cafe.Launcher.Avalonia.Services;
using CommunityToolkit.Mvvm.Input;

namespace Cafe.Launcher.Avalonia.ViewModels;

public partial class ToastHostViewModel : ViewModelBase, IDisposable
{
    private readonly ToastService toastService;
    private readonly LocalizationService localizer;
    private SettingsViewModel? settings;
    private readonly CancellationTokenSource lifetimeCts = new();
    private bool disposed;

    public ObservableCollection<ToastNotification> ActiveToasts { get; } = [];

    public ToastHostViewModel(ToastService toastService, LocalizationService localizer)
    {
        this.toastService = toastService;
        this.localizer = localizer;
        toastService.ToastRaised += OnToastRaised;
    }

    public void Configure(SettingsViewModel settings)
    {
        this.settings = settings;
    }

    [RelayCommand]
    private void DismissToast(string toastId)
    {
        var toast = ActiveToasts.FirstOrDefault(t => t.Id == toastId);
        if (toast is not null)
        {
            ActiveToasts.Remove(toast);
        }
    }

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
        if (settings is not null && !settings.ToastNotificationsEnabled)
        {
            return;
        }

        try
        {
            if (string.IsNullOrWhiteSpace(notification.Message))
            {
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() => ActiveToasts.Add(notification));
            await Task.Delay(notification.DurationMs, cancellationToken);
            await Dispatcher.UIThread.InvokeAsync(
                () =>
                {
                    try { ActiveToasts.Remove(notification); }
                    catch (InvalidOperationException) { }
                },
                DispatcherPriority.Background,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
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
