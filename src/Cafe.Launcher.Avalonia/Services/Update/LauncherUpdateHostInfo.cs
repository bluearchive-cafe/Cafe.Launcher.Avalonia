namespace Cafe.Launcher.Avalonia.Services.Update;

/// <summary>
/// The host facts the package selector answers from. Passed in instead of read from
/// the environment so the platform/install-kind matrix is a pure decision.
/// </summary>
/// <param name="IsWindows">Whether the launcher runs on Windows.</param>
/// <param name="IsX64">Whether the process is the x64 build the release assets target.</param>
/// <param name="IsInstallerInstall">
/// Whether the app was installed by the Inno installer (the <c>.cafe-launcher-install</c>
/// marker sits next to the executable) rather than unpacked from the portable zip.
/// </param>
public sealed record LauncherUpdateHostInfo(
    bool IsWindows,
    bool IsX64,
    bool IsInstallerInstall);
