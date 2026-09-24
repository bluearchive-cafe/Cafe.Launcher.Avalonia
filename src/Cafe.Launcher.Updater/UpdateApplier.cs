using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;

namespace Cafe.Launcher.Updater;

/// <summary>
/// Applies a verified launcher update once the parent process has exited. Installer
/// builds hand off to the Inno setup executable (elevating); portable builds swap the
/// installation directory with the extracted package, rolling back if the swap fails.
/// The portable swap keeps the previous version until the new one has actually started:
/// a package without the launcher executable is rejected before the swap, and a new
/// version that fails to start is replaced by the restored previous version. Every
/// failure names itself in the log and leaves a runnable launcher behind.
/// </summary>
public static class UpdateApplier
{
    /// <summary>How long to wait for the launcher to exit before giving up.</summary>
    internal static readonly TimeSpan ParentExitTimeout = TimeSpan.FromMinutes(2);

    /// <summary>Runs the apply flow and returns a process exit code (0 on success).</summary>
    public static async Task<int> ApplyAsync(UpdaterArguments arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var stopwatch = Stopwatch.StartNew();
        UpdateLog.Write(arguments, $"Apply started (mode={arguments.Mode}, parentPid={arguments.ParentProcessId}).");

        try
        {
            await WaitForParentExitAsync(arguments.ParentProcessId, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            UpdateLog.Write(arguments, "The launcher process did not exit in time; aborting.");
            return 1;
        }

        if (!await PackageIntegrity
            .MatchesSha256Async(arguments.PackagePath, arguments.ExpectedSha256, cancellationToken)
            .ConfigureAwait(false))
        {
            UpdateLog.Write(arguments, "Package SHA-256 verification failed; aborting.");
            return 2;
        }

        return arguments.Mode == UpdateApplyMode.Installer
            ? RunInstaller(arguments, stopwatch)
            : ReplacePortableDirectory(arguments, stopwatch);
    }

    private static async Task WaitForParentExitAsync(int parentProcessId, CancellationToken cancellationToken)
    {
        try
        {
            using var parent = Process.GetProcessById(parentProcessId);
            await parent
                .WaitForExitAsync(cancellationToken)
                .WaitAsync(ParentExitTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ArgumentException)
        {
            // The process already exited before we looked it up.
        }
    }

    private static int RunInstaller(UpdaterArguments arguments, Stopwatch stopwatch)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = arguments.PackagePath,
            Arguments = "/SILENT /SUPPRESSMSGBOXES /NORESTART",
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = Path.GetDirectoryName(arguments.PackagePath) ?? ""
        };

        Process? installer;
        try
        {
            installer = Process.Start(startInfo);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            UpdateLog.Write(arguments, $"Failed to start the installer: {exception.Message}");
            return 3;
        }

        if (installer is null)
        {
            UpdateLog.Write(arguments, "The installer did not start.");
            return 3;
        }

        using (installer)
        {
            installer.WaitForExit();
            if (installer.ExitCode != 0)
            {
                UpdateLog.Write(arguments, $"The installer exited with code {installer.ExitCode}.");
                return 4;
            }
        }

        LogApplyCompleted(arguments, stopwatch);
        return LaunchApplication(arguments) ? 0 : 5;
    }

    private static int ReplacePortableDirectory(UpdaterArguments arguments, Stopwatch stopwatch)
    {
        var staging = UpdateApplyPlan.StagingDirectory(arguments.InstallDirectory, arguments.ParentProcessId);
        var backup = UpdateApplyPlan.BackupDirectory(arguments.InstallDirectory, arguments.ParentProcessId);

        try
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }

            Directory.CreateDirectory(staging);
            ZipFile.ExtractToDirectory(arguments.PackagePath, staging, overwriteFiles: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            UpdateLog.Write(arguments, $"Failed to extract the package: {exception.Message}");
            return 6;
        }

        if (!File.Exists(UpdateApplyPlan.ExecutablePath(staging, arguments.ExecutableName)))
        {
            UpdateLog.Write(
                arguments,
                $"The package does not contain '{arguments.ExecutableName}'; keeping the current installation.");
            TryDeleteDirectory(staging);
            return 10;
        }

        try
        {
            if (Directory.Exists(backup))
            {
                Directory.Delete(backup, recursive: true);
            }

            Directory.Move(arguments.InstallDirectory, backup);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            UpdateLog.Write(arguments, $"Failed to move the current installation aside: {exception.Message}");
            TryDeleteDirectory(staging);
            return 7;
        }

        try
        {
            Directory.Move(staging, arguments.InstallDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            UpdateLog.Write(arguments, $"Failed to install the new version: {exception.Message}");
            TryRollBack(arguments, backup);
            return 8;
        }

        if (LaunchApplication(arguments))
        {
            TryDeleteDirectory(backup);
            LogApplyCompleted(arguments, stopwatch);
            return 0;
        }

        UpdateLog.Write(arguments, "The new version did not start; restoring the previous version.");
        RestorePreviousVersion(arguments, staging, backup);
        return 9;
    }

    /// <summary>
    /// Puts the previous version back after the new one failed to start. The new version
    /// is moved to the now-free staging path (deleted when that rename is refused), the
    /// backup returns to the installation path, and the old launcher is started best
    /// effort. The backup directory is only consumed once it has been restored, so a
    /// failure here still leaves the old version on disk under the backup path.
    /// </summary>
    private static void RestorePreviousVersion(UpdaterArguments arguments, string staging, string backup)
    {
        try
        {
            if (!Directory.Exists(backup))
            {
                UpdateLog.Write(arguments, "No backup directory exists to restore; keeping the new version in place.");
                return;
            }

            if (Directory.Exists(arguments.InstallDirectory))
            {
                try
                {
                    Directory.Move(arguments.InstallDirectory, staging);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    UpdateLog.Write(
                        arguments,
                        $"Could not move the new version aside ({exception.Message}); deleting it instead.");
                    Directory.Delete(arguments.InstallDirectory, recursive: true);
                }
            }

            Directory.Move(backup, arguments.InstallDirectory);
            UpdateLog.Write(arguments, "Restored the previous version.");
            TryDeleteDirectory(staging);

            if (!LaunchApplication(arguments))
            {
                UpdateLog.Write(
                    arguments,
                    "The previous version did not start either; start it manually from the installation directory.");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            UpdateLog.Write(
                arguments,
                $"Restoring the previous version failed: {exception.Message}. The backup is kept at '{backup}'.");
        }
    }

    /// <summary>
    /// The success counterpart to the "Apply started" line: a helper run that replaces
    /// the installation and hands over to the new executable otherwise leaves no trace
    /// in the log, and success could only be inferred from the relaunched process.
    /// </summary>
    private static void LogApplyCompleted(UpdaterArguments arguments, Stopwatch stopwatch) =>
        UpdateLog.Write(
            arguments,
            $"Apply completed (mode={arguments.Mode}, elapsed={stopwatch.Elapsed.TotalSeconds:0.0##}s).");

    private static void TryRollBack(UpdaterArguments arguments, string backup)
    {
        try
        {
            if (Directory.Exists(backup) && !Directory.Exists(arguments.InstallDirectory))
            {
                Directory.Move(backup, arguments.InstallDirectory);
                UpdateLog.Write(arguments, "Rolled back to the previous version.");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            UpdateLog.Write(arguments, $"Rollback failed: {exception.Message}");
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A stranded backup directory is harmless; it can be removed on a later run.
            Console.Error.WriteLine($"Failed to remove the backup directory '{path}': {exception.Message}");
        }
    }

    private static bool LaunchApplication(UpdaterArguments arguments)
    {
        var executablePath = UpdateApplyPlan.ExecutablePath(arguments.InstallDirectory, arguments.ExecutableName);
        if (!File.Exists(executablePath))
        {
            UpdateLog.Write(arguments, $"The launcher executable was not found after applying: {executablePath}");
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = arguments.InstallDirectory,
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            UpdateLog.Write(arguments, $"Failed to relaunch the launcher: {exception.Message}");
            return false;
        }
    }
}
