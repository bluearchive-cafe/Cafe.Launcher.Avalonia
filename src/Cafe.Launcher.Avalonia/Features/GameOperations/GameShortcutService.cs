using System;
using System.Globalization;
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
    private readonly Func<bool> isWindowsPlatform;
    private readonly Func<bool> isLinuxPlatform;
    private readonly Func<string?> launcherExecutablePath;

    /// <summary>
    /// Platform and external-effect dependencies, kept together so adding the next
    /// dependency cannot grow the constructor again.
    /// </summary>
    internal sealed record ShortcutEnvironment(
        Func<string, bool> OpenDirectory,
        Func<bool> IsWindowsPlatform,
        Func<bool> IsLinuxPlatform,
        Func<string?> LauncherExecutablePath)
    {
        public static ShortcutEnvironment ForCurrentPlatform() => new(
            ShellFolderOpener.OpenInFileManager,
            OperatingSystem.IsWindows,
            OperatingSystem.IsLinux,
            () => Environment.ProcessPath);
    }

    public GameShortcutService(LocalizationService localizer)
        : this(localizer, ShortcutEnvironment.ForCurrentPlatform())
    {
    }

    internal GameShortcutService(LocalizationService localizer, Func<string, bool> openDirectory)
        : this(localizer, new ShortcutEnvironment(
            openDirectory,
            OperatingSystem.IsWindows,
            OperatingSystem.IsLinux,
            () => Environment.ProcessPath))
    {
    }

    internal GameShortcutService(LocalizationService localizer, ShortcutEnvironment environment)
    {
        this.localizer = localizer;
        openDirectory = environment.OpenDirectory;
        isWindowsPlatform = environment.IsWindowsPlatform;
        isLinuxPlatform = environment.IsLinuxPlatform;
        launcherExecutablePath = environment.LauncherExecutablePath;
    }

    public Task<GameShortcutResult> CreateDesktopShortcutAsync(LauncherStatusSnapshot snapshot)
    {
        var desktopDirectory = OperatingSystem.IsWindows() || OperatingSystem.IsLinux()
            ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
            : null;
        return CreateShortcutInDirectoryAsync(snapshot, desktopDirectory);
    }

    internal async Task<GameShortcutResult> CreateShortcutInDirectoryAsync(
        LauncherStatusSnapshot snapshot,
        string? targetDirectory)
    {
        if (isLinuxPlatform())
        {
            return CreateLinuxDesktopEntry(snapshot, targetDirectory);
        }

        // The OperatingSystem check satisfies the platform analyzer in front of the
        // COM call; the seam only exists so tests can pin the selected branch.
        if (!isWindowsPlatform()
            || !OperatingSystem.IsWindows()
            || string.IsNullOrWhiteSpace(targetDirectory))
        {
            return new GameShortcutResult(GameShortcutStatus.UnsupportedPlatform);
        }

        var targetResolution = GameLaunchTargetResolution.Resolve(snapshot);
        if (!targetResolution.Resolved)
        {
            return new GameShortcutResult(GameShortcutStatus.GameNotResolved);
        }

        var target = targetResolution.Target!;

        // The shortcut deliberately bypasses the launcher (unlike the Linux .desktop,
        // which routes through --launch-game): double-clicking must behave like a
        // direct game start. Running the game executable alone does not start the
        // game, so the target is the distribution's own run.bat start script.
        var startScriptPath = Path.Combine(target.WorkingDirectory, GamePaths.GameStartScriptFileName);
        if (!File.Exists(startScriptPath))
        {
            return new GameShortcutResult(GameShortcutStatus.GameNotResolved);
        }

        var shortcutFileName = ResolveShortcutFileName(target.ExecutablePath);
        var shortcutPath = Path.Combine(targetDirectory, $"{shortcutFileName}.lnk");
        var iconPath = ResolveShortcutIconPath(snapshot, target.ExecutablePath);
        try
        {
            CreateShortcut(startScriptPath, target.WorkingDirectory, iconPath, shortcutPath);
            return new GameShortcutResult(GameShortcutStatus.Created, shortcutPath);
        }
        catch (Exception exception)
        {
            return new GameShortcutResult(GameShortcutStatus.Failed, exception.Message);
        }
    }

    /// <summary>
    /// Linux desktop entry. Unlike the Windows .lnk (which points straight at the
    /// game executable), the entry always goes through the launcher with
    /// --launch-game so manifest validation, update checks, clickCode, runner
    /// selection, and diagnostics all apply (cross-platform runtime design §13/§14).
    /// </summary>
    internal GameShortcutResult CreateLinuxDesktopEntry(
        LauncherStatusSnapshot snapshot,
        string? targetDirectory)
    {
        if (!isLinuxPlatform() || string.IsNullOrWhiteSpace(targetDirectory))
        {
            return new GameShortcutResult(GameShortcutStatus.UnsupportedPlatform);
        }

        var launcherPath = launcherExecutablePath();
        if (string.IsNullOrWhiteSpace(launcherPath) || !File.Exists(launcherPath))
        {
            // Keep the service result structured. The journey maps this status to
            // the existing localized "shortcut target missing" message.
            return new GameShortcutResult(GameShortcutStatus.GameNotResolved);
        }

        if (!GameLaunchTargetResolution.Resolve(snapshot).Resolved)
        {
            return new GameShortcutResult(GameShortcutStatus.GameNotResolved);
        }

        var entryName = ResolveShortcutFileName(launcherPath);
        var entryPath = Path.Combine(targetDirectory, $"{entryName}.desktop");
        var iconPath = ResolveLinuxIconPath(launcherPath);
        try
        {
            WriteDesktopEntry(entryPath, localizer.T(LocalizationKeys.GameDisplayName), launcherPath, iconPath);
            if (OperatingSystem.IsLinux())
            {
                // Desktop environments only launch entries marked executable.
                File.SetUnixFileMode(
                    entryPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }

            return new GameShortcutResult(GameShortcutStatus.Created, entryPath);
        }
        catch (Exception exception)
        {
            return new GameShortcutResult(GameShortcutStatus.Failed, exception.Message);
        }
    }

    /// <summary>
    /// Renders the desktop-entry file content for one launch point. The entry goes
    /// through the launcher with --launch-game (never the game executable directly)
    /// so manifest validation, update checks, clickCode, runner selection, and
    /// diagnostics all apply, per the cross-platform runtime design §13/§14.
    /// Exposed for tests: asserts the documented entry shape byte-for-byte.
    /// </summary>
    internal static string BuildDesktopEntry(string displayName, string launcherPath, string? iconPath)
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("[Desktop Entry]");
        builder.AppendLine("Type=Application");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Name={displayName}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Exec=\"{launcherPath}\" {Program.LaunchGameArgument}");
        if (!string.IsNullOrWhiteSpace(iconPath))
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"Icon={iconPath}");
        }

        builder.AppendLine("Terminal=false");
        builder.Append("Categories=Game;");
        return builder.ToString();
    }

    /// <summary>
    /// The launcher icon copied next to the executable (Assets/app-icon.ico). No icon
    /// line when it is missing — desktops fall back to a generic icon.
    /// </summary>
    private static string? ResolveLinuxIconPath(string launcherPath)
    {
        var candidate = Path.Combine(Path.GetDirectoryName(launcherPath) ?? "", "Assets", "app-icon.ico");
        return File.Exists(candidate) ? candidate : null;
    }

    private void WriteDesktopEntry(string entryPath, string displayName, string launcherPath, string? iconPath) =>
        File.WriteAllText(entryPath, BuildDesktopEntry(displayName, launcherPath, iconPath), new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

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
        var displayName = SanitizeFileName(localizer.T(LocalizationKeys.GameDisplayName));
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
