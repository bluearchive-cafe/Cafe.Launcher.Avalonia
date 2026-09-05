namespace Cafe.Launcher.Avalonia.ViewModels;

/// <summary>Identifies every modal surface hosted by the main window.</summary>
public enum ModalKind
{
    Settings,
    ResourcePanel,
    LogViewer,
    Debug,
    DesignGallery,
    DebugResetConfirmation,
    Notice,
    Update,
    Error,
    SetupWizard,
    SetupWizardExitConfirmation,
    UnsavedSettingsConfirmation,
    RepairConfirmation,
    ResourcePanelSourceConfirmation,
    UninstallConfirmation,
    StopConfirmation,
    DownloadRunningCloseConfirmation,
}
