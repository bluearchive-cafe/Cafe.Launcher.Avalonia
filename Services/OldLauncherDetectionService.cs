using System;
using System.IO;
using Cafe.Launcher.Avalonia.Constants;

namespace Cafe.Launcher.Avalonia.Services;

/// <summary>
/// Detects the old Electron-based BlueArchive_JP_Gamelauncher and reads
/// its configuration for migration into the new Cafe Launcher.
/// </summary>
public sealed class OldLauncherDetectionService
{
    private static readonly string[] WindowsLauncherPaths =
    [
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            GamePaths.OldLauncherAppName)
    ];

    private static readonly string[] LinuxLauncherPaths =
    [
        "/opt/BlueArchive_JP_Gamelauncher",
    ];

    /// <summary>
    /// Detects the old launcher and reads migratable settings.
    /// Returns null if the old launcher was never used on this machine.
    /// </summary>
    public OldLauncherDetectionResult? Detect()
    {
        var userDataPath = ResolveOldUserDataPath();
        if (userDataPath is null || !Directory.Exists(userDataPath))
            return null;

        var result = new OldLauncherDetectionResult
        {
            OldUserDataPath = userDataPath
        };

        // Try LevelDB reading first
        var levelDbPath = Path.Combine(userDataPath, "Local Storage", "leveldb");
        if (Directory.Exists(levelDbPath))
        {
            var levelDbValues = LevelDbReader.TryReadValues(levelDbPath);
            result.LevelDbReadSuccess = levelDbValues.Count > 0;

            if (levelDbValues.TryGetValue("downloadPath", out var gamePath))
                result.GamePath = ValidateGamePath(gamePath);

            if (levelDbValues.TryGetValue("proxy-config", out var proxy))
                result.ProxyMode = proxy;

            if (levelDbValues.TryGetValue("close-choice", out var closeChoice))
                result.CloseBehavior = closeChoice;
        }

        // Fallback: detect game path from old launcher install location
        if (string.IsNullOrWhiteSpace(result.GamePath))
        {
            result.GamePath = DetectGamePathFromInstallLocation();
        }

        // Check for clickCode file
        var clickCodePath = Path.Combine(userDataPath, "clickCode");
        result.ClickCodeFound = File.Exists(clickCodePath);

        return result;
    }

    /// <summary>
    /// Returns the old launcher's userData directory path, or null if the platform isn't supported.
    /// </summary>
    public static string? ResolveOldUserDataPath()
    {
        if (OperatingSystem.IsWindows())
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, GamePaths.OldLauncherAppName);
        }

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(home))
                return Path.Combine(home, ".config", GamePaths.OldLauncherAppName);
        }

        return null;
    }

    /// <summary>
    /// Copies the clickCode file from the old launcher's userData to the new launcher's userData.
    /// Respects the existing ClickCodeService convention.
    /// </summary>
    public static void CopyClickCode(string oldUserDataPath)
    {
        var sourcePath = Path.Combine(oldUserDataPath, "clickCode");
        if (!File.Exists(sourcePath))
            return;

        try
        {
            var newUserDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                LauncherConstants.ProductName);
            var targetPath = Path.Combine(newUserDataDir, "clickCode");

            // Only copy if target doesn't already exist
            if (File.Exists(targetPath))
                return;

            Directory.CreateDirectory(newUserDataDir);
            File.Copy(sourcePath, targetPath, overwrite: false);
        }
        catch
        {
            // Best effort
        }
    }

    private static string? ValidateGamePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        // The path from LevelDB should be a directory
        if (Directory.Exists(path))
            return path;

        // Try normalizing to the expected game folder structure
        var normalized = Path.Combine(path, GamePaths.GameFolderName);
        if (Directory.Exists(normalized))
            return normalized;

        normalized = Path.Combine(path, GamePaths.RootFolderName, GamePaths.GameFolderName);
        if (Directory.Exists(normalized))
            return normalized;

        // Path doesn't exist — return it anyway so user can see what was detected
        return path;
    }

    private static string? DetectGamePathFromInstallLocation()
    {
        string[] searchPaths;

        if (OperatingSystem.IsWindows())
            searchPaths = WindowsLauncherPaths;
        else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            searchPaths = LinuxLauncherPaths;
        else
            return null;

        foreach (var launcherPath in searchPaths)
        {
            // The old launcher installs games at: <dir above launcher>/YostarGames/BlueArchive_JP
            var parentDir = Path.GetDirectoryName(launcherPath);
            if (string.IsNullOrWhiteSpace(parentDir))
                continue;

            var gamePath = Path.Combine(parentDir, GamePaths.RootFolderName, GamePaths.GameFolderName);
            if (Directory.Exists(gamePath))
                return gamePath;

            // Also try one more level up
            parentDir = Path.GetDirectoryName(parentDir);
            if (!string.IsNullOrWhiteSpace(parentDir))
            {
                gamePath = Path.Combine(parentDir, GamePaths.RootFolderName, GamePaths.GameFolderName);
                if (Directory.Exists(gamePath))
                    return gamePath;
            }
        }

        return null;
    }
}

/// <summary>
/// Detection result from the old launcher's configuration.
/// </summary>
public sealed class OldLauncherDetectionResult
{
    /// <summary>Game installation path from old launcher, if found.</summary>
    public string? GamePath { get; set; }

    /// <summary>Proxy mode: "direct" or "system".</summary>
    public string? ProxyMode { get; set; }

    /// <summary>Close behavior: "minimize" or "exit".</summary>
    public string? CloseBehavior { get; set; }

    /// <summary>Whether the clickCode file exists in old user data.</summary>
    public bool ClickCodeFound { get; set; }

    /// <summary>Whether LevelDB was successfully read.</summary>
    public bool LevelDbReadSuccess { get; set; }

    /// <summary>Path to the old launcher's user data directory.</summary>
    public string OldUserDataPath { get; set; } = "";
}
