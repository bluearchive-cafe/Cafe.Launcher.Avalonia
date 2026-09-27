namespace Cafe.Launcher.Avalonia.Features.SetupWizard;

/// <summary>Identifies the current availability of the setup wizard game installation path.</summary>
internal enum SetupWizardGamePathStatus
{
    NotSelected,
    Checking,
    AvailableForInstallation,
    ValidInstallation,
    CorruptedInstallation,
    Inaccessible,
    NotWritable,
}
