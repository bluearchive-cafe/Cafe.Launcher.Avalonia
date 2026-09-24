namespace Cafe.Launcher.Avalonia.Services.Update;

/// <summary>
/// How a launcher update should be delivered on the current host. Only Windows has
/// an in-app apply path; everything else keeps the browser hand-off.
/// </summary>
public enum LauncherUpdateTarget
{
    /// <summary>No in-app apply path: hand the download page to the browser.</summary>
    ExternalDownload,

    /// <summary>Run the Inno installer silently through the detached helper.</summary>
    WindowsInstaller,

    /// <summary>Replace the portable installation directory from the release zip.</summary>
    WindowsPortable
}
