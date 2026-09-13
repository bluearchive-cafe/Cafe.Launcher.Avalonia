using System;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cafe.Launcher.Avalonia.ViewModels;

/// <summary>
/// Owns the confirmation-dialog machinery once for the whole dialog family:
/// visibility, message text, and the cancel/confirm command pair. The confirm
/// contract has a single implementation here — hide the dialog first, then
/// invoke subscribers sequentially in subscription order, logging a failure
/// so one broken handler can neither swallow the dialog's closure nor crash
/// the session. <see cref="DialogsViewModel"/> aggregates one instance per
/// confirmation; a new confirmation is a field plus an AXAML block.
/// </summary>
public sealed partial class ConfirmationDialogViewModel : ViewModelBase
{
    private const string LogTitle = "ConfirmationDialog";
    private readonly LocalDiagnostics diagnostics;
    private readonly string operationContext;

    /// <summary>Creates one confirmation dialog with instance-scoped diagnostics and operation context.</summary>
    public ConfirmationDialogViewModel(
        LocalDiagnostics diagnostics,
        string operationContext)
    {
        this.diagnostics = diagnostics;
        this.operationContext = operationContext;
        ShowCommand = new RelayCommand(Show);
        CancelCommand = new RelayCommand(Cancel);
        ConfirmCommand = new AsyncRelayCommand(ConfirmAsync);
    }

    [ObservableProperty]
    private bool isVisible;

    [ObservableProperty]
    private string message = "";

    /// <summary>Gets the operation name included in diagnostics when confirmation handling fails.</summary>
    public string OperationContext => operationContext;

    /// <summary>Raised after the user confirms and the dialog has closed.</summary>
    public event Func<Task>? Confirmed;

    /// <summary>Command that presents the dialog (for surfaces that trigger it purely from XAML).</summary>
    public IRelayCommand ShowCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public IAsyncRelayCommand ConfirmCommand { get; }

    /// <summary>Presents the confirmation without changing its message text.</summary>
    public void Show() => IsVisible = true;

    /// <summary>Presents the confirmation with the supplied message text.</summary>
    public void Show(string message)
    {
        Message = message;
        IsVisible = true;
    }

    private void Cancel() => IsVisible = false;

    private async Task ConfirmAsync()
    {
        IsVisible = false;
        try
        {
            await AsyncEvent.InvokeSequentiallyAsync(Confirmed);
        }
        catch (Exception exception)
        {
            await diagnostics.ErrorAsync(
                LogTitle,
                $"{operationContext} confirmation handler failed.",
                exception,
                CancellationToken.None);
        }
    }
}
