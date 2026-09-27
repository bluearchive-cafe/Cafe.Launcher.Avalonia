namespace Cafe.Launcher.Updater;

/// <summary>How the downloaded package should be applied.</summary>
public enum UpdateApplyMode
{
    /// <summary>Run the Inno setup executable silently, then relaunch the launcher.</summary>
    Installer,

    /// <summary>Extract the zip over the portable installation directory, then relaunch.</summary>
    Portable
}
