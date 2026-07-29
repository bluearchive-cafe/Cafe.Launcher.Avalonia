using System;
using System.IO;
using System.Text.RegularExpressions;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Helpers;

namespace Cafe.Launcher.Avalonia.Services;

/// <summary>
/// Tracks install attribution via a "clickCode" file.
/// Mirrors the original Electron launcher's click code system — the code is extracted
/// from the installer filename, stored in user data, and copied to the game directory on launch.
/// </summary>
public sealed class ClickCodeService
{
    private const string ClickCodeFileName = "clickCode";

    private static string UserDataDir => LauncherUserDataDirectory.Path;

    /// <summary>
    /// Reads the clickCode from the app directory (if it exists from installer),
    /// extracts the hash, saves to user data, and deletes the original.
    /// Call once on application startup.
    /// </summary>
    public void SaveClickCode()
    {
        var exeDir = Path.GetDirectoryName(AppContext.BaseDirectory) ?? "";
        var installerClickCode = Path.Combine(exeDir, ClickCodeFileName);
        if (!File.Exists(installerClickCode))
            return;

        try
        {
            var content = File.ReadAllText(installerClickCode).Trim();
            var match = Regex.Match(content, @"^.*?_install_(.*?)_\d+\.\d+\.\d+.*\.exe$");
            if (match.Success)
            {
                var hash = match.Groups[1].Value;
                var userDataClickCode = Path.Combine(UserDataDir, ClickCodeFileName);
                Directory.CreateDirectory(UserDataDir);
                File.WriteAllText(userDataClickCode, hash);
            }
        }
        catch
        {
            // Best effort
        }

        try
        {
            File.Delete(installerClickCode);
        }
        catch
        {
            // Best effort
        }
    }

    /// <summary>
    /// Copies the clickCode from user data to the game directory when launching the game.
    /// </summary>
    public void WriteClickCodeToGamePath(string gamePath)
    {
        var sourcePath = Path.Combine(UserDataDir, ClickCodeFileName);
        if (!File.Exists(sourcePath))
            return;

        try
        {
            // Defense-in-depth: validate that the target stays within the game directory
            var targetPath = GamePathValidator.GetSafePath(
                Path.GetFullPath(gamePath),
                ClickCodeFileName);
            Directory.CreateDirectory(gamePath);
            File.WriteAllText(targetPath, File.ReadAllText(sourcePath).Trim());
        }
        catch
        {
            // Best effort
        }
    }
}
