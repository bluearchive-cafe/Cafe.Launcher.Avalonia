using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Cafe.Launcher.Avalonia.Services.Update;

/// <summary>
/// Reads the current host facts. Installed-vs-portable is decided by the ownership
/// marker the Inno installer writes next to the executable (see the <c>.iss</c>);
/// its literal is mirrored here and kept in sync by a contract test.
/// </summary>
internal sealed class LauncherUpdateHostInfoProvider : ILauncherUpdateHostInfoProvider
{
    /// <summary>Must match the marker written by <c>installer/Cafe.Launcher.Avalonia.iss</c>.</summary>
    internal const string InstallMarkerFileName = ".cafe-launcher-install";

    /// <inheritdoc />
    public LauncherUpdateHostInfo GetHostInfo()
    {
        var isWindows = OperatingSystem.IsWindows();
        return new LauncherUpdateHostInfo(
            IsWindows: isWindows,
            IsX64: RuntimeInformation.ProcessArchitecture == Architecture.X64,
            IsInstallerInstall: isWindows
                && File.Exists(Path.Combine(AppContext.BaseDirectory, InstallMarkerFileName)));
    }
}
