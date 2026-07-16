namespace Cafe.Launcher.Avalonia.Features.Shell;

/// <summary>Identifies every modal surface hosted by the main window.</summary>
public enum ModalKind
{
    Settings,
    ResourcePanel,
    LogViewer,
    Notice,
    Update,
    CrashRecovery,
    SetupWizard,
    SetupWizardExitConfirmation,
    UnsavedSettingsConfirmation,
    RepairConfirmation,
    ResourcePanelSourceConfirmation,
    UninstallConfirmation,
    StopConfirmation,
    DownloadRunningCloseConfirmation,
}
