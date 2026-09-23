using System;
using System.Globalization;

namespace Cafe.Launcher.Avalonia.Services.Update;

/// <summary>
/// Builds the detached helper's command line. The option names and mode values here
/// are mirrored by <c>Cafe.Launcher.Updater.UpdaterArguments</c>; a round-trip test
/// keeps the two halves of the contract in sync.
/// </summary>
internal static class UpdateHelperCommand
{
    internal const string HelperExecutableName = "Cafe.Launcher.Updater.exe";
    internal const string ModeOption = "--mode";
    internal const string PackageOption = "--package";
    internal const string InstallDirOption = "--install-dir";
    internal const string ExeOption = "--exe";
    internal const string ParentPidOption = "--parent-pid";
    internal const string Sha256Option = "--sha256";
    internal const string LogOption = "--log";
    internal const string InstallerMode = "installer";
    internal const string PortableMode = "portable";

    /// <summary>Maps the selection target to the helper's mode value.</summary>
    public static string ModeName(LauncherUpdateTarget target) => target switch
    {
        LauncherUpdateTarget.WindowsInstaller => InstallerMode,
        LauncherUpdateTarget.WindowsPortable => PortableMode,
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, "Only Windows targets have a helper mode.")
    };

    /// <summary>Builds the helper argument vector as alternating option/value tokens.</summary>
    public static string[] BuildArguments(
        LauncherUpdateTarget target,
        string packagePath,
        string installDirectory,
        string executableName,
        int parentProcessId,
        string expectedSha256,
        string logPath) =>
    [
        ModeOption, ModeName(target),
        PackageOption, packagePath,
        InstallDirOption, installDirectory,
        ExeOption, executableName,
        ParentPidOption, parentProcessId.ToString(CultureInfo.InvariantCulture),
        Sha256Option, expectedSha256,
        LogOption, logPath
    ];
}
