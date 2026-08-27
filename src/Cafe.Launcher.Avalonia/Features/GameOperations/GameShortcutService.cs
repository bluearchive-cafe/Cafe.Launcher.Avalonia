using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Features.GameOperations;

/// <summary>Terminal outcome of a desktop shortcut creation attempt.</summary>
public enum GameShortcutStatus
{
    Created,
    UnsupportedPlatform,
    GameNotResolved,
    Failed
}

/// <summary>Result of a desktop shortcut creation attempt, with failure detail when applicable.</summary>
public sealed record GameShortcutResult(GameShortcutStatus Status, string Detail = "");

/// <summary>Desktop-level integrations for the installed game: shortcuts and folder access.</summary>
public interface IGameShortcutService
{
    /// <summary>Creates a desktop shortcut that starts the installed game.</summary>
    Task<GameShortcutResult> CreateDesktopShortcutAsync(LauncherStatusSnapshot snapshot);

    /// <summary>Opens the installed game folder in the platform file manager.</summary>
    bool TryOpenGameFolder(LauncherStatusSnapshot snapshot);
}

public sealed class GameShortcutService : IGameShortcutService
{
    private readonly LocalizationService localizer;
    private readonly Func<string, bool> openDirectory;

    public GameShortcutService(LocalizationService localizer)
        : this(localizer, OpenDirectoryInFileManager)
    {
    }

    internal GameShortcutService(LocalizationService localizer, Func<string, bool> openDirectory)
    {
        this.localizer = localizer;
        this.openDirectory = openDirectory;
    }

    public Task<GameShortcutResult> CreateDesktopShortcutAsync(LauncherStatusSnapshot snapshot)
    {
        var desktopDirectory = OperatingSystem.IsWindows()
            ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
            : null;
        return CreateShortcutInDirectoryAsync(snapshot, desktopDirectory);
    }

    internal async Task<GameShortcutResult> CreateShortcutInDirectoryAsync(
        LauncherStatusSnapshot snapshot,
        string? targetDirectory)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(targetDirectory))
        {
            return new GameShortcutResult(GameShortcutStatus.UnsupportedPlatform);
        }

        if (!TryResolveGameExecutable(snapshot, out var executablePath, out var workingDirectory, out _))
        {
            return new GameShortcutResult(GameShortcutStatus.GameNotResolved);
        }

        var shortcutFileName = ResolveShortcutFileName(executablePath);
        var shortcutPath = Path.Combine(targetDirectory, $"{shortcutFileName}.lnk");
        var iconPath = ResolveShortcutIconPath(snapshot, executablePath);
        try
        {
            CreateShortcut(executablePath, workingDirectory, iconPath, shortcutPath);
            return new GameShortcutResult(GameShortcutStatus.Created, shortcutPath);
        }
        catch (Exception exception)
        {
            return new GameShortcutResult(GameShortcutStatus.Failed, exception.Message);
        }
    }

    public bool TryOpenGameFolder(LauncherStatusSnapshot snapshot)
    {
        var gamePath = snapshot.LocalGame.GamePath;
        if (string.IsNullOrWhiteSpace(gamePath) || !Directory.Exists(gamePath))
        {
            return false;
        }

        return openDirectory(gamePath);
    }

    /// <summary>
    /// Takes the shortcut icon from the actual game client (BlueArchive.exe) when it
    /// exists; the resolved start entry can be a loader wrapper without a game icon.
    /// </summary>
    internal static string ResolveShortcutIconPath(LauncherStatusSnapshot snapshot, string executablePath)
    {
        var gamePath = snapshot.LocalGame.GamePath;
        if (string.IsNullOrWhiteSpace(gamePath))
        {
            return executablePath;
        }

        var gameExecutablePath = Path.Combine(gamePath, GamePaths.GameExecutableFileName);
        return File.Exists(gameExecutablePath) ? gameExecutablePath : executablePath;
    }

    /// <summary>Names the desktop shortcut after the localized game display name, sanitized for file systems.</summary>
    internal string ResolveShortcutFileName(string executablePath)
    {
        var displayName = SanitizeFileName(localizer.T("gameDisplayName"));
        return displayName.Length > 0
            ? displayName
            : Path.GetFileNameWithoutExtension(executablePath);
    }

    internal static string SanitizeFileName(string name)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var builder = new System.Text.StringBuilder(name.Length);
        foreach (var character in name)
        {
            builder.Append(Array.IndexOf(invalidChars, character) >= 0 ? ' ' : character);
        }

        return builder.ToString().Trim();
    }

    /// <summary>Resolves the game executable the same way the launch flow does: local name first, remote fallback.</summary>
    internal static bool TryResolveGameExecutable(
        LauncherStatusSnapshot snapshot,
        out string executablePath,
        out string workingDirectory,
        out string shortcutName)
    {
        executablePath = "";
        workingDirectory = "";
        shortcutName = "";

        var gamePath = snapshot.LocalGame.GamePath;
        if (string.IsNullOrWhiteSpace(gamePath))
        {
            return false;
        }

        var localName = snapshot.LocalGame.GameConfig?.Name;
        var remoteName = snapshot.Remote.GameConfig?.GameStartExeName;
        var executableName = !string.IsNullOrWhiteSpace(localName) ? localName : remoteName;

        // Defense-in-depth: reject executable names containing path separators.
        if (string.IsNullOrWhiteSpace(executableName)
            || executableName.Contains('/')
            || executableName.Contains('\\'))
        {
            return false;
        }

        var candidate = Path.Combine(gamePath, $"{executableName}.exe");
        if (!File.Exists(candidate))
        {
            return false;
        }

        executablePath = candidate;
        workingDirectory = gamePath;
        shortcutName = Path.GetFileNameWithoutExtension(executableName);
        return true;
    }

    private static bool OpenDirectoryInFileManager(string directory)
    {
        var startInfo = OperatingSystem.IsWindows()
            ? new ProcessStartInfo
            {
                FileName = "explorer.exe",
                ArgumentList = { directory },
                UseShellExecute = true
            }
            : new ProcessStartInfo
            {
                FileName = directory,
                UseShellExecute = true
            };

        return Process.Start(startInfo) is not null;
    }

    [SupportedOSPlatform("windows")]
    private static void CreateShortcut(
        string executablePath,
        string workingDirectory,
        string iconPath,
        string shortcutPath)
    {
        var shellLink = (IShellLinkW)new ShellLink();
        try
        {
            shellLink.SetPath(executablePath);
            shellLink.SetWorkingDirectory(workingDirectory);

            // Pin the icon to the game's main executable, not the loader wrapper.
            shellLink.SetIconLocation(iconPath, 0);
            ((IPersistFile)shellLink).Save(shortcutPath, fRemember: true);
        }
        finally
        {
            _ = Marshal.ReleaseComObject(shellLink);
        }
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink
    {
    }

    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath(
            [Out][MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszFile,
            int cchMax,
            IntPtr findData,
            uint flags);

        void GetIDList(out IntPtr pointerIdList);

        void SetIDList(IntPtr pointerIdList);

        void GetDescription(
            [Out][MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszFile,
            int cchMax);

        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszFile);

        void GetWorkingDirectory(
            [Out][MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszFile,
            int cchMax);

        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszFile);

        void GetArguments(
            [Out][MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszFile,
            int cchMax);

        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszFile);

        void GetHotkey(out short hotkey);

        void SetHotkey(short hotkey);

        void GetShowCmd(out int showCmd);

        void SetShowCmd(int showCmd);

        void GetIconLocation(
            [Out][MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszIconPath,
            int cchMax,
            out int iconIndex);

        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iconIndex);

        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRelative, uint reserved);

        void Resolve(IntPtr hwnd, uint flags);

        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }
}
