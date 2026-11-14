using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Cafe.Launcher.UI.Constants;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Constants;
using Cafe.Launcher.Core.Services;

namespace Cafe.Launcher.UI.Features.GameOperations;

/// <summary>Terminal outcome of a desktop shortcut creation or removal attempt.</summary>
internal enum GameShortcutStatus
{
    Created,
    UnsupportedPlatform,
    GameNotResolved,
    Failed,

    /// <summary>桌面快捷方式已删除（卸载的一部分，ADR-030）。</summary>
    Deleted,

    /// <summary>桌面本来就没有这个快捷方式——不算失败。</summary>
    NotFound
}

/// <summary>Result of a desktop shortcut creation attempt, with failure detail when applicable.</summary>
internal sealed record GameShortcutResult(GameShortcutStatus Status, string Detail = "");

/// <summary>Desktop-level integrations for the installed game: shortcuts and folder access.</summary>
internal interface IGameShortcutService
{
    /// <summary>
    /// Creates a desktop shortcut that starts the installed game. The file name is the
    /// stable game identifier from the profile, so changing the UI language never renames
    /// the entry (AUD-GAME-002).
    /// </summary>
    Task<GameShortcutResult> CreateDesktopShortcutAsync(LauncherStatusSnapshot snapshot);

    /// <summary>
    /// 删除桌面快捷方式。卸载的一部分（标准路径与彻底清除都删，ADR-030）：删除稳定文件名，
    /// 并清理旧版本按任一支持语言生成的本地化入口，因此切换界面语言后卸载也不会残留；
    /// 不依赖游戏文件是否还在，找不到不算失败（AUD-GAME-002）。
    /// </summary>
    Task<GameShortcutResult> DeleteDesktopShortcutAsync(LauncherStatusSnapshot snapshot);

    /// <summary>Opens the installed game folder in the platform file manager.</summary>
    bool TryOpenGameFolder(LauncherStatusSnapshot snapshot);
}

internal sealed class GameShortcutService : IGameShortcutService
{
    private readonly YostarGameProfile gameProfile;
    private readonly LocalizationService localizer;
    private readonly Func<string, bool> openDirectory;
    private readonly Func<bool> isWindowsPlatform;
    private readonly Func<bool> isLinuxPlatform;
    private readonly Func<string?> launcherExecutablePath;

    /// <summary>
    /// 启动器支持的界面语言。只用于识别旧版本按语言写下的快捷方式文件名，
    /// 名字本身始终来自本地化资源（<see cref="LocalizationKeys.GameDisplayName"/>）。
    /// </summary>
    private static readonly string[] LocalizedShortcutLanguages =
    [
        LauncherLanguages.English,
        LauncherLanguages.SimplifiedChinese,
        LauncherLanguages.TraditionalChinese,
        LauncherLanguages.Japanese
    ];

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
            ResolveCurrentLauncherExecutablePath);
    }

    public GameShortcutService(YostarGameProfile gameProfile, LocalizationService localizer)
        : this(gameProfile, localizer, ShortcutEnvironment.ForCurrentPlatform())
    {
    }

    internal GameShortcutService(
        YostarGameProfile gameProfile,
        LocalizationService localizer,
        Func<string, bool> openDirectory)
        : this(gameProfile, localizer, new ShortcutEnvironment(
            openDirectory,
            OperatingSystem.IsWindows,
            OperatingSystem.IsLinux,
            ResolveCurrentLauncherExecutablePath))
    {
    }

    internal GameShortcutService(
        YostarGameProfile gameProfile,
        LocalizationService localizer,
        ShortcutEnvironment environment)
    {
        this.gameProfile = gameProfile;
        this.localizer = localizer;
        openDirectory = environment.OpenDirectory;
        isWindowsPlatform = environment.IsWindowsPlatform;
        isLinuxPlatform = environment.IsLinuxPlatform;
        launcherExecutablePath = environment.LauncherExecutablePath;
    }

    private static string? ResolveCurrentLauncherExecutablePath() =>
        ResolveLauncherExecutablePath(Environment.ProcessPath,
            OperatingSystem.IsLinux() ? Environment.GetEnvironmentVariable("APPIMAGE") : null);

    /// <summary>AppImage 快捷方式必须指向镜像文件，而非退出即消失的挂载目录。</summary>
    internal static string? ResolveLauncherExecutablePath(string? processPath, string? appImagePath) =>
        !string.IsNullOrWhiteSpace(appImagePath)
        && Path.IsPathFullyQualified(appImagePath)
        && File.Exists(appImagePath)
            ? appImagePath
            : processPath;

    public Task<GameShortcutResult> CreateDesktopShortcutAsync(LauncherStatusSnapshot snapshot) =>
        CreateShortcutInDirectoryAsync(snapshot, ResolveDesktopDirectory());

    public Task<GameShortcutResult> DeleteDesktopShortcutAsync(LauncherStatusSnapshot snapshot) =>
        Task.FromResult(DeleteShortcutInDirectory(snapshot, ResolveDesktopDirectory()));

    /// <summary>
    /// 桌面目录；平台不受支持时为 null（调用方据此报 UnsupportedPlatform）。
    /// </summary>
    /// <remarks>
    /// 创建那条路径此前直接读 <see cref="OperatingSystem"/>，绕过了
    /// <see cref="ShortcutEnvironment"/> 注入的探针，于是测试换不动它的平台判断——只有删除那条
    /// 能被接缝切换。两个入口共用这一处判定后，平台问题在测试里可复现，生产行为不变（注入的
    /// 探针本来就是同一对 <see cref="OperatingSystem"/> 调用）。
    /// </remarks>
    private string? ResolveDesktopDirectory() =>
        isWindowsPlatform() || isLinuxPlatform()
            ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
            : null;

    /// <summary>
    /// 删除桌面快捷方式（ADR-030）。与创建共用 <see cref="ResolveStableShortcutFileName"/>，
    /// 并额外清理旧版本按各支持语言生成的本地化入口，因此界面语言切换后卸载不会残留
    /// （AUD-GAME-002）；这里刻意不解析启动目标——游戏文件可能已经先一步删掉了，
    /// 而文件名不需要目标存在。
    /// </summary>
    internal GameShortcutResult DeleteShortcutInDirectory(
        LauncherStatusSnapshot snapshot,
        string? targetDirectory)
    {
        if ((!isWindowsPlatform() && !isLinuxPlatform()) || string.IsNullOrWhiteSpace(targetDirectory))
        {
            return new GameShortcutResult(GameShortcutStatus.UnsupportedPlatform);
        }

        var executablePath = Path.Combine(
            snapshot.LocalGame.GamePath ?? "",
            gameProfile.GameExecutableFileName);
        var stableShortcutPath = BuildShortcutPath(
            targetDirectory, ResolveStableShortcutFileName(executablePath));

        // 稳定名先删，再清掉任一语言写下的旧入口；全部找不到才算 NotFound。
        string? deletedPath = null;
        foreach (var shortcutPath in EnumerateExistingShortcutPaths(targetDirectory, executablePath))
        {
            try
            {
                File.Delete(shortcutPath);
                deletedPath ??= shortcutPath;
            }
            catch (Exception exception)
            {
                return new GameShortcutResult(GameShortcutStatus.Failed, exception.Message);
            }
        }

        return deletedPath is null
            ? new GameShortcutResult(GameShortcutStatus.NotFound, stableShortcutPath)
            : new GameShortcutResult(GameShortcutStatus.Deleted, deletedPath);
    }

    /// <summary>
    /// 稳定名与各语言旧名中实际存在的入口，按删除顺序排列。旧名只来自本地化资源，
    /// 不猜别的文件名：同目录下无关的桌面文件不会被碰。
    /// </summary>
    private IEnumerable<string> EnumerateExistingShortcutPaths(
        string targetDirectory,
        string executablePath)
    {
        var stablePath = BuildShortcutPath(
            targetDirectory, ResolveStableShortcutFileName(executablePath));
        if (File.Exists(stablePath))
        {
            yield return stablePath;
        }

        foreach (var legacyPath in EnumerateLegacyShortcutPaths(targetDirectory, executablePath))
        {
            yield return legacyPath;
        }
    }

    /// <summary>
    /// 旧版本按界面语言写下的入口路径（不含稳定名、只含实际存在的文件）。创建后的
    /// 清理与卸载共用这一份枚举，两处对「哪些旧名字算自己的」因此不会分叉。
    /// </summary>
    private IEnumerable<string> EnumerateLegacyShortcutPaths(
        string targetDirectory,
        string executablePath)
    {
        var stablePath = BuildShortcutPath(
            targetDirectory, ResolveStableShortcutFileName(executablePath));
        foreach (var legacyName in ResolveLegacyShortcutFileNames())
        {
            var legacyPath = BuildShortcutPath(targetDirectory, legacyName);

            // 稳定名与旧名理论上不会重合；重合时按稳定名处理，不再当作旧入口。
            if (!string.Equals(legacyPath, stablePath, StringComparison.Ordinal)
                && File.Exists(legacyPath))
            {
                yield return legacyPath;
            }
        }
    }

    /// <summary>
    /// 旧版本按界面语言生成的快捷方式文件名（去掉扩展名）。来源是现有本地化资源里
    /// <see cref="LocalizationKeys.GameDisplayName"/> 在当前支持的语言下的取值，不新建翻译表。
    /// </summary>
    private IEnumerable<string> ResolveLegacyShortcutFileNames()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var language in LocalizedShortcutLanguages)
        {
            var displayName = localizer.LookupForLanguage(language, LocalizationKeys.GameDisplayName);
            if (string.IsNullOrEmpty(displayName))
            {
                continue;
            }

            var sanitized = SanitizeFileName(displayName);
            if (sanitized.Length > 0 && seen.Add(sanitized))
            {
                yield return sanitized;
            }
        }
    }

    private string BuildShortcutPath(string targetDirectory, string shortcutFileName)
    {
        var extension = isLinuxPlatform() ? ".desktop" : ".lnk";
        return Path.Combine(targetDirectory, $"{shortcutFileName}{extension}");
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
        var startScriptPath = Path.Combine(
            target.WorkingDirectory,
            gameProfile.GameStartScriptFileName);
        if (!File.Exists(startScriptPath))
        {
            return new GameShortcutResult(GameShortcutStatus.GameNotResolved);
        }

        var shortcutFileName = ResolveStableShortcutFileName(target.ExecutablePath);
        var shortcutPath = Path.Combine(targetDirectory, $"{shortcutFileName}.lnk");
        var iconPath = ResolveShortcutIconPath(
            snapshot,
            target.ExecutablePath,
            gameProfile.GameExecutableFileName);
        try
        {
            CreateShortcut(startScriptPath, target.WorkingDirectory, iconPath, shortcutPath);

            // 新入口落盘后再清旧语言名：写入失败时旧入口仍在，不会两头落空。
            TryDeleteLegacyShortcutPaths(targetDirectory, target.ExecutablePath);
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
    /// --launch-game so manifest validation, update checks, runner
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

        var entryName = ResolveStableShortcutFileName(launcherPath);
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

            // 新入口落盘后再清旧语言名：写入失败时旧入口仍在，不会两头落空。
            TryDeleteLegacyShortcutPaths(targetDirectory, launcherPath);
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
    /// so manifest validation, update checks, runner selection, and
    /// diagnostics all apply, per the cross-platform runtime design §13/§14.
    /// Exposed for tests: asserts the documented entry shape byte-for-byte.
    /// </summary>
    internal static string BuildDesktopEntry(string displayName, string launcherPath, string? iconPath)
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("[Desktop Entry]");
        builder.AppendLine("Type=Application");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Name={EscapeDesktopValue(displayName)}");
        // GLib 在展开字段码前检查 Exec 的首个程序是否存在；%% 路径需作为 env 的参数展开。
        var executable = launcherPath.Contains('%')
            ? "/usr/bin/env -- " + QuoteDesktopExecutable(launcherPath)
            : QuoteDesktopExecutable(launcherPath);
        builder.AppendLine(CultureInfo.InvariantCulture, $"Exec={executable} {LauncherConstants.LaunchGameArgument}");
        if (!string.IsNullOrWhiteSpace(iconPath))
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"Icon={EscapeDesktopValue(iconPath)}");
        }

        builder.AppendLine("Terminal=false");
        builder.Append("Categories=Game;");
        return builder.ToString();
    }

    // Exec 先转义双引号参数与字段码，再转义 desktop 字符串；反斜杠必须经过两层解析。
    private static string QuoteDesktopExecutable(string path) =>
        "\"" + EscapeDesktopValue(path.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("`", "\\`", StringComparison.Ordinal)
            .Replace("$", "\\$", StringComparison.Ordinal)
            .Replace("%", "%%", StringComparison.Ordinal)) + "\"";

    private static string EscapeDesktopValue(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal);

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
    /// Takes the shortcut icon from the actual game client (<c>BlueArchive.exe</c> for this
    /// game profile) when it exists; the resolved start entry can be a loader wrapper without
    /// a game icon. Kept static and parameterised: it reads no service state, only the
    /// profile's client file name.
    /// </summary>
    internal static string ResolveShortcutIconPath(
        LauncherStatusSnapshot snapshot,
        string executablePath,
        string gameExecutableFileName)
    {
        var gamePath = snapshot.LocalGame.GamePath;
        if (string.IsNullOrWhiteSpace(gamePath))
        {
            return executablePath;
        }

        var gameExecutablePath = Path.Combine(gamePath, gameExecutableFileName);
        return File.Exists(gameExecutablePath) ? gameExecutablePath : executablePath;
    }

    /// <summary>
    /// 快捷方式文件名（去掉扩展名），稳定且不随界面语言变化：取自游戏档案的安装目录名
    /// （<see cref="YostarGameProfile.GameFolderName"/>），因此 Windows 的 .lnk 与 Linux 的
    /// .desktop 同名（AUD-GAME-002）。旧版本用本地化显示名，清理时另行覆盖。
    /// </summary>
    internal string ResolveStableShortcutFileName(string executablePath)
    {
        var profileName = SanitizeFileName(gameProfile.GameFolderName);
        return profileName.Length > 0
            ? profileName
            : Path.GetFileNameWithoutExtension(executablePath);
    }

    /// <summary>
    /// 创建后尽力清理同目录下旧版本按任一语言写下的入口，避免语言切换后出现两个桌面图标。
    /// 清理失败不阻断创建：旧入口仍会在卸载时按全部已知名字再清一次。
    /// </summary>
    private void TryDeleteLegacyShortcutPaths(string targetDirectory, string executablePath)
    {
        foreach (var legacyPath in EnumerateLegacyShortcutPaths(targetDirectory, executablePath))
        {
            try
            {
                File.Delete(legacyPath);
            }
            catch (Exception)
            {
                // 被占用或只读的旧入口不阻断新入口创建；卸载时还会再清一次。
            }
        }
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
