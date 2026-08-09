using System;
using System.ComponentModel;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.ViewModels;

namespace Cafe.Launcher.Avalonia.Features.Shell;

/// <summary>
/// Coordinates modal overlay visibility and escape-key resolution across child view models.
/// </summary>
public sealed class ShellCoordinator
{
    private readonly ModalHostViewModel modalHost;
    private readonly WindowChromeViewModel windowChrome;
    private readonly SettingsViewModel settings;
    private readonly ResourcePanelViewModel resourcePanel;
    private readonly LogViewerDialogViewModel logViewer;
    private readonly DebugViewModel debug;
    private readonly DialogsViewModel dialogs;
    private bool isWired;

    /// <summary>Raised when the status detail mode setting changes; the shell raises its dependent property notifications.</summary>
    public event Action? StatusDetailModeChanged;

    /// <summary>The modal host this coordinator manages.</summary>
    public ModalHostViewModel ModalHost => modalHost;

    public ShellCoordinator(
        ModalHostViewModel modalHost,
        WindowChromeViewModel windowChrome,
        SettingsViewModel settings,
        ResourcePanelViewModel resourcePanel,
        LogViewerDialogViewModel logViewer,
        DebugViewModel debug,
        DialogsViewModel dialogs)
    {
        this.modalHost = modalHost;
        this.windowChrome = windowChrome;
        this.settings = settings;
        this.resourcePanel = resourcePanel;
        this.logViewer = logViewer;
        this.debug = debug;
        this.dialogs = dialogs;
    }

    /// <summary>Subscribes to PropertyChanged on child view models for modal sync.</summary>
    public void Wire()
    {
        if (isWired) return;
        isWired = true;
        windowChrome.PropertyChanged += OnWindowChromePropertyChanged;
        settings.PropertyChanged += OnSettingsPropertyChanged;
        settings.Editor.CurrentPropertyChanged += OnSettingPropertyChanged;
        resourcePanel.PropertyChanged += OnResourcePanelPropertyChanged;
        logViewer.PropertyChanged += OnLogViewerPropertyChanged;
        debug.PropertyChanged += OnDebugPropertyChanged;
        dialogs.PropertyChanged += OnDialogsPropertyChanged;
    }

    /// <summary>Unsubscribes from all PropertyChanged handlers.</summary>
    public void Unwire()
    {
        if (!isWired) return;
        isWired = false;
        windowChrome.PropertyChanged -= OnWindowChromePropertyChanged;
        settings.PropertyChanged -= OnSettingsPropertyChanged;
        settings.Editor.CurrentPropertyChanged -= OnSettingPropertyChanged;
        resourcePanel.PropertyChanged -= OnResourcePanelPropertyChanged;
        logViewer.PropertyChanged -= OnLogViewerPropertyChanged;
        debug.PropertyChanged -= OnDebugPropertyChanged;
        dialogs.PropertyChanged -= OnDialogsPropertyChanged;
    }

    private void OnSettingPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LauncherSettings.StatusDetailMode))
        {
            StatusDetailModeChanged?.Invoke();
        }
    }

    private void OnWindowChromePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WindowChromeViewModel.IsSettingsVisible))
        {
            SyncModal(ModalKind.Settings, windowChrome.IsSettingsVisible, settings);
        }
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.IsUnsavedChangesVisible))
        {
            SyncModal(
                ModalKind.UnsavedSettingsConfirmation,
                settings.IsUnsavedChangesVisible,
                settings);
        }
    }

    private void OnResourcePanelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ResourcePanelViewModel.IsResourcePanelVisible))
        {
            SyncModal(
                ModalKind.ResourcePanel,
                resourcePanel.IsResourcePanelVisible,
                resourcePanel);
        }
    }

    private void OnLogViewerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LogViewerDialogViewModel.IsVisible))
        {
            SyncModal(ModalKind.LogViewer, logViewer.IsVisible, logViewer);
        }
    }

    private void OnDebugPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DebugViewModel.IsVisible))
        {
            SyncModal(ModalKind.Debug, debug.IsVisible, debug);
        }
    }

    private void OnDialogsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(DialogsViewModel.IsNoticeDialogVisible):
                SyncModal(ModalKind.Notice, dialogs.IsNoticeDialogVisible, dialogs);
                break;
            case nameof(DialogsViewModel.IsUpdateAvailableVisible):
                SyncModal(ModalKind.Update, dialogs.IsUpdateAvailableVisible, dialogs);
                break;
            case nameof(DialogsViewModel.IsCrashRecoveryVisible):
                SyncModal(ModalKind.CrashRecovery, dialogs.IsCrashRecoveryVisible, dialogs);
                break;
            case nameof(DialogsViewModel.IsErrorDialogVisible):
                SyncModal(ModalKind.Error, dialogs.IsErrorDialogVisible, dialogs);
                break;
            case nameof(DialogsViewModel.IsDebugResetConfirmationVisible):
                SyncModal(
                    ModalKind.DebugResetConfirmation,
                    dialogs.IsDebugResetConfirmationVisible,
                    dialogs);
                break;
            case nameof(DialogsViewModel.IsSetupWizardVisible):
                SyncModal(ModalKind.SetupWizard, dialogs.IsSetupWizardVisible, dialogs.SetupWizard);
                break;
            case nameof(DialogsViewModel.IsSetupWizardExitConfirmVisible):
                SyncModal(
                    ModalKind.SetupWizardExitConfirmation,
                    dialogs.IsSetupWizardExitConfirmVisible,
                    dialogs);
                break;
            case nameof(DialogsViewModel.IsRepairConfirmVisible):
                SyncModal(ModalKind.RepairConfirmation, dialogs.IsRepairConfirmVisible, dialogs);
                break;
            case nameof(DialogsViewModel.IsResourcePanelSourceConfirmVisible):
                SyncModal(
                    ModalKind.ResourcePanelSourceConfirmation,
                    dialogs.IsResourcePanelSourceConfirmVisible,
                    dialogs);
                break;
            case nameof(DialogsViewModel.IsUninstallConfirmVisible):
                SyncModal(
                    ModalKind.UninstallConfirmation,
                    dialogs.IsUninstallConfirmVisible,
                    dialogs);
                break;
            case nameof(DialogsViewModel.IsStopConfirmVisible):
                SyncModal(ModalKind.StopConfirmation, dialogs.IsStopConfirmVisible, dialogs);
                break;
            case nameof(DialogsViewModel.IsDownloadRunningCloseConfirmVisible):
                SyncModal(
                    ModalKind.DownloadRunningCloseConfirmation,
                    dialogs.IsDownloadRunningCloseConfirmVisible,
                    dialogs);
                break;
        }
    }

    private void SyncModal(ModalKind kind, bool isVisible, IModalContentViewModel content)
    {
        if (isVisible)
        {
            modalHost.Open(kind, content);
        }
        else
        {
            modalHost.Close(kind);
        }
    }

    /// <summary>
    /// Attempts to handle the Escape key press.
    /// Returns true if a visible overlay/dialog was dismissed, false if no action was needed.
    /// </summary>
    public bool TryHandleEscape()
    {
        switch (modalHost.Top?.Kind)
        {
            case ModalKind.DownloadRunningCloseConfirmation:
                dialogs.CancelCloseWhileDownloadingCommand.Execute(null);
                break;
            case ModalKind.StopConfirmation:
                dialogs.CancelStopCommand.Execute(null);
                break;
            case ModalKind.UnsavedSettingsConfirmation:
                windowChrome.KeepEditingSettingsCommand.Execute(null);
                break;
            case ModalKind.RepairConfirmation:
                dialogs.CancelRepairCommand.Execute(null);
                break;
            case ModalKind.ResourcePanelSourceConfirmation:
                dialogs.CancelResourcePanelSourceSwitchCommand.Execute(null);
                break;
            case ModalKind.UninstallConfirmation:
                dialogs.CancelUninstallCommand.Execute(null);
                break;
            case ModalKind.Notice:
                dialogs.DismissNoticeCommand.Execute(null);
                break;
            case ModalKind.Update:
                dialogs.CancelUpdateAvailableCommand.Execute(null);
                break;
            case ModalKind.CrashRecovery:
                dialogs.ContinueAfterCrashCommand.Execute(null);
                break;
            case ModalKind.Error:
                dialogs.ContinueAfterErrorCommand.Execute(null);
                break;
            case ModalKind.LogViewer:
                logViewer.CloseCommand.Execute(null);
                break;
            case ModalKind.Debug:
                debug.CloseCommand.Execute(null);
                break;
            case ModalKind.DebugResetConfirmation:
                dialogs.CancelDebugResetCommand.Execute(null);
                break;
            case ModalKind.SetupWizardExitConfirmation:
                dialogs.CancelSetupWizardExitCommand.Execute(null);
                break;
            case ModalKind.Settings:
                windowChrome.ShowSettingsCommand.Execute(null);
                break;
            case ModalKind.SetupWizard:
                dialogs.RequestSetupWizardExitCommand.Execute(null);
                break;
            case ModalKind.ResourcePanel:
                resourcePanel.CloseResourcePanelCommand.Execute(null);
                break;
            default:
                return false;
        }

        return true;
    }
}
