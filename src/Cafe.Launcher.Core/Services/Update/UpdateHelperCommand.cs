using System;
using Cafe.Launcher.Updater;

namespace Cafe.Launcher.Core.Services.Update;

/// <summary>
/// Builds the detached helper's command line through the updater core's shared contract.
/// </summary>
internal static class UpdateHelperCommand
{
    internal const string HelperExecutableName = "Cafe.Launcher.Updater.exe";

    /// <summary>Maps the selection target to the helper's mode value.</summary>
    public static string ModeName(LauncherUpdateTarget target) =>
        UpdaterArguments.ModeName(ApplyMode(target));

    private static UpdateApplyMode ApplyMode(LauncherUpdateTarget target) => target switch
    {
        LauncherUpdateTarget.WindowsInstaller => UpdateApplyMode.Installer,
        LauncherUpdateTarget.WindowsPortable => UpdateApplyMode.Portable,
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
        new UpdaterArguments(
            ApplyMode(target),
            packagePath,
            installDirectory,
            executableName,
            parentProcessId,
            expectedSha256,
            logPath)
        .ToArgumentArray();
}
