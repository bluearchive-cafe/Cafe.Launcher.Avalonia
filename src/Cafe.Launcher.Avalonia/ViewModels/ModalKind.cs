namespace Cafe.Launcher.Avalonia.ViewModels;

/// <summary>Identifies every modal surface hosted by the main window.</summary>
public enum ModalKind
{
    Settings,
    ResourcePanel,
    LogViewer,
    LogExport,
    Debug,
    DesignGallery,
    DebugResetConfirmation,
    Notice,
    Update,
    Error,
    SetupWizard,
    SetupWizardExitConfirmation,
    UnsavedSettingsConfirmation,
    SettingsResetConfirmation,
    RepairConfirmation,
    ResourcePanelSourceConfirmation,
    UninstallConfirmation,
    StopConfirmation,
    DownloadRunningCloseConfirmation,
}
