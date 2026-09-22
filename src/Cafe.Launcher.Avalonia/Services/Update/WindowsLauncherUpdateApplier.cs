using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Cafe.Launcher.Avalonia.Services.Diagnostics;

namespace Cafe.Launcher.Avalonia.Services.Update;

/// <summary>
/// Starts the update helper: copies the shipped helper executable into a fresh temp
/// directory (so it is not subject to the file locks that will be released when the
/// launcher exits), then launches it with the staged package details. The helper
/// waits for this process to exit before touching the installation.
/// </summary>
internal sealed class WindowsLauncherUpdateApplier : IWindowsLauncherUpdateApplier
{
    private const string TempFolderName = "CafeLauncherUpdate";
    internal const string ApplyLogFileName = "update-apply.log";

    private readonly LauncherDataRoot dataRoot;
    private readonly LocalDiagnostics diagnostics;

    public WindowsLauncherUpdateApplier(LauncherDataRoot dataRoot, LocalDiagnostics diagnostics)
    {
        this.dataRoot = dataRoot;
        this.diagnostics = diagnostics;
    }

    /// <inheritdoc />
    public bool TryStartApply(LauncherSelfUpdatePreparation preparation)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        if (preparation.Status != LauncherSelfUpdatePreparationStatus.Ready
            || string.IsNullOrWhiteSpace(preparation.PackagePath))
        {
            return false;
        }

        var installDirectory = TrimTrailingSeparator(AppContext.BaseDirectory);
        var helperSource = Path.Combine(installDirectory, UpdateHelperCommand.HelperExecutableName);
        if (!File.Exists(helperSource))
        {
            ReportFailure($"The update helper is missing: {helperSource}");
            return false;
        }

        var executableName = Path.GetFileName(Environment.ProcessPath);
        if (string.IsNullOrWhiteSpace(executableName))
        {
            ReportFailure("The launcher executable name could not be resolved.");
            return false;
        }

        var tempDirectory = Path.Combine(
            Path.GetTempPath(),
            TempFolderName,
            Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDirectory);
            var helperPath = Path.Combine(tempDirectory, UpdateHelperCommand.HelperExecutableName);
            File.Copy(helperSource, helperPath, overwrite: true);

            var arguments = UpdateHelperCommand.BuildArguments(
                preparation.Target,
                preparation.PackagePath!,
                installDirectory,
                executableName,
                Environment.ProcessId,
                preparation.ExpectedSha256,
                Path.Combine(dataRoot.Root, ApplyLogFileName));

            var startInfo = new ProcessStartInfo
            {
                FileName = helperPath,
                WorkingDirectory = tempDirectory,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            Process.Start(startInfo);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception)
        {
            ReportFailure($"Failed to start the update helper: {exception.Message}");
            return false;
        }
    }

    private void ReportFailure(string message) =>
        _ = diagnostics.WarningAsync("LauncherSelfUpdate", message, System.Threading.CancellationToken.None);

    private static string TrimTrailingSeparator(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return trimmed.Length == 0 ? path : trimmed;
    }
}
