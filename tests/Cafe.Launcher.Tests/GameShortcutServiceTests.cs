using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading.Tasks;
using Cafe.Launcher.UI.Constants;
using Cafe.Launcher.UI.Features.GameOperations;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.Testing;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Constants;

namespace Cafe.Launcher.Tests;

[Collection(nameof(LocalizationServiceTestIsolation))]
public sealed class GameShortcutServiceTests : IDisposable
{
    private readonly TestDirectory tempDirectory = TestDirectory.Create();

    public void Dispose()
    {
        tempDirectory.Dispose();
    }

    [Fact]
    public void DeleteShortcutInDirectory_WhenShortcutExists_RemovesItAndReportsDeleted()
    {
        var shortcutDirectory = Directory.CreateDirectory(Path.Combine(tempDirectory, "desktop")).FullName;
        var gameDirectory = Directory.CreateDirectory(Path.Combine(tempDirectory, "game")).FullName;
        var service = new GameShortcutService(LauncherProfiles.BlueArchiveJapan, new LocalizationService());
        var snapshot = new LauncherStatusSnapshot
        {
            LocalGame = new LocalInstallationState { GamePath = gameDirectory }
        };
        // 名字必须与创建侧同源：稳定的游戏标识 + 平台后缀（ADR-030 / AUD-GAME-002）。
        var shortcutName = service.ResolveStableShortcutFileName(
            Path.Combine(gameDirectory, LauncherProfiles.BlueArchiveJapan.GameExecutableFileName));
        var extension = OperatingSystem.IsLinux() ? ".desktop" : ".lnk";
        var shortcutPath = Path.Combine(shortcutDirectory, $"{shortcutName}{extension}");
        File.WriteAllText(shortcutPath, "stub entry");

        var result = service.DeleteShortcutInDirectory(snapshot, shortcutDirectory);

        Assert.Equal(GameShortcutStatus.Deleted, result.Status);
        Assert.False(File.Exists(shortcutPath));
    }

    [Fact]
    public void DeleteShortcutInDirectory_WhenShortcutIsMissing_ReportsNotFound()
    {
        var shortcutDirectory = Directory.CreateDirectory(Path.Combine(tempDirectory, "desktop")).FullName;
        var service = new GameShortcutService(LauncherProfiles.BlueArchiveJapan, new LocalizationService());
        var snapshot = new LauncherStatusSnapshot
        {
            LocalGame = new LocalInstallationState
            {
                GamePath = Directory.CreateDirectory(Path.Combine(tempDirectory, "game")).FullName
            }
        };

        var result = service.DeleteShortcutInDirectory(snapshot, shortcutDirectory);

        // 桌面本来就没有这个快捷方式不算失败：卸载不该因此变成失败（ADR-030）。
        Assert.Equal(GameShortcutStatus.NotFound, result.Status);
    }

    [Fact]
    public void DeleteShortcutInDirectory_WhenTargetDirectoryIsBlank_ReportsUnsupportedPlatform()
    {
        var service = new GameShortcutService(LauncherProfiles.BlueArchiveJapan, new LocalizationService());

        var result = service.DeleteShortcutInDirectory(new LauncherStatusSnapshot(), null);

        Assert.Equal(GameShortcutStatus.UnsupportedPlatform, result.Status);
    }

    [Fact]
    public async Task CreateShortcutInDirectoryAsync_WhenOnlyRemoteNameAvailable_ReturnsGameNotResolved()
    {
        var gameDirectory = Directory.CreateDirectory(Path.Combine(tempDirectory, "game")).FullName;
        File.WriteAllText(Path.Combine(gameDirectory, "RemoteGame.exe"), string.Empty);
        CreateStartScript(gameDirectory);
        var shortcutDirectory = Directory.CreateDirectory(Path.Combine(tempDirectory, "desktop")).FullName;
        var service = new GameShortcutService(LauncherProfiles.BlueArchiveJapan, new LocalizationService());
        var snapshot = new LauncherStatusSnapshot
        {
            LocalGame = new LocalInstallationState
            {
                GamePath = gameDirectory,
                GameConfig = new GameLauncherConfig()
            },
            Remote = new LauncherRemoteState
            {
                GameConfig = new GameConfigResponse { GameStartExeName = "RemoteGame" }
            }
        };

        var result = await service.CreateShortcutInDirectoryAsync(snapshot, shortcutDirectory);

        Assert.Equal(GameShortcutStatus.GameNotResolved, result.Status);
        Assert.False(File.Exists(Path.Combine(shortcutDirectory, "BlueArchive_JP.lnk")));
    }

    [Fact]
    public void TryOpenGameFolder_WhenFolderMissing_ReturnsFalseAndSkipsOpener()
    {
        var openerCalls = 0;
        var service = new GameShortcutService(LauncherProfiles.BlueArchiveJapan, new LocalizationService(), _ =>
        {
            openerCalls++;
            return true;
        });
        var snapshot = new LauncherStatusSnapshot
        {
            LocalGame = new LocalInstallationState { GamePath = Path.Combine(tempDirectory, "missing") }
        };

        var opened = service.TryOpenGameFolder(snapshot);

        Assert.False(opened);
        Assert.Equal(0, openerCalls);
    }

    [Fact]
    public void TryOpenGameFolder_WhenFolderExists_InvokesOpenerWithGamePath()
    {
        var gameDirectory = Directory.CreateDirectory(Path.Combine(tempDirectory, "game")).FullName;
        string? openedDirectory = null;
        var service = new GameShortcutService(LauncherProfiles.BlueArchiveJapan, new LocalizationService(), directory =>
        {
            openedDirectory = directory;
            return true;
        });
        var snapshot = new LauncherStatusSnapshot
        {
            LocalGame = new LocalInstallationState { GamePath = gameDirectory }
        };

        var opened = service.TryOpenGameFolder(snapshot);

        Assert.True(opened);
        Assert.Equal(gameDirectory, openedDirectory);
    }

    [Fact]
    public void TryOpenGameFolder_WhenOpenerFails_ReturnsFalse()
    {
        var gameDirectory = Directory.CreateDirectory(Path.Combine(tempDirectory, "game")).FullName;
        var service = new GameShortcutService(LauncherProfiles.BlueArchiveJapan, new LocalizationService(), _ => false);
        var snapshot = new LauncherStatusSnapshot
        {
            LocalGame = new LocalInstallationState { GamePath = gameDirectory }
        };

        var opened = service.TryOpenGameFolder(snapshot);

        Assert.False(opened);
    }

    [WindowsFact]
    public async Task CreateShortcutInDirectoryAsync_WhenGameResolved_WritesStableShortcutFileName()
    {
        var gameDirectory = Directory.CreateDirectory(Path.Combine(tempDirectory, "game")).FullName;
        var executablePath = Path.Combine(gameDirectory, "CafeTestGame.exe");
        File.WriteAllText(executablePath, string.Empty);
        CreateStartScript(gameDirectory);
        var shortcutDirectory = Directory.CreateDirectory(Path.Combine(tempDirectory, "desktop")).FullName;
        var service = new GameShortcutService(LauncherProfiles.BlueArchiveJapan, new LocalizationService());
        var snapshot = new LauncherStatusSnapshot
        {
            LocalGame = new LocalInstallationState
            {
                GamePath = gameDirectory,
                GameConfig = new GameLauncherConfig { Name = "CafeTestGame" }
            }
        };

        var result = await service.CreateShortcutInDirectoryAsync(snapshot, shortcutDirectory);

        Assert.Equal(GameShortcutStatus.Created, result.Status);
        var expectedShortcutPath = Path.Combine(shortcutDirectory, "BlueArchive_JP.lnk");
        Assert.True(File.Exists(expectedShortcutPath));
        Assert.Equal(expectedShortcutPath, result.Detail);
    }

    [WindowsFact]
    public async Task CreateShortcutInDirectoryAsync_OnWindows_TargetsRunBatWithGameFolderWorkingDirectory()
    {
        var gameDirectory = Directory.CreateDirectory(Path.Combine(tempDirectory, "game")).FullName;
        File.WriteAllText(Path.Combine(gameDirectory, "CafeTestGame.exe"), string.Empty);
        var startScriptPath = CreateStartScript(gameDirectory);
        var shortcutDirectory = Directory.CreateDirectory(Path.Combine(tempDirectory, "desktop")).FullName;
        var service = new GameShortcutService(LauncherProfiles.BlueArchiveJapan, new LocalizationService());
        var snapshot = new LauncherStatusSnapshot
        {
            LocalGame = new LocalInstallationState
            {
                GamePath = gameDirectory,
                GameConfig = new GameLauncherConfig { Name = "CafeTestGame" }
            }
        };

        var result = await service.CreateShortcutInDirectoryAsync(snapshot, shortcutDirectory);

        Assert.Equal(GameShortcutStatus.Created, result.Status);
        var (targetPath, workingDirectory, arguments) = ReadShortcutTarget(result.Detail);
        // Direct game start, deliberately not routed through the launcher.
        Assert.Equal(startScriptPath, targetPath, ignoreCase: true);
        Assert.Equal(gameDirectory, workingDirectory, ignoreCase: true);
        Assert.Equal(string.Empty, arguments);
        Assert.DoesNotContain("--launch-game", targetPath, StringComparison.OrdinalIgnoreCase);
    }

    [WindowsFact]
    public async Task CreateShortcutInDirectoryAsync_WhenStartScriptMissing_ReturnsGameNotResolved()
    {
        var gameDirectory = Directory.CreateDirectory(Path.Combine(tempDirectory, "game")).FullName;
        File.WriteAllText(Path.Combine(gameDirectory, "CafeTestGame.exe"), string.Empty);
        var shortcutDirectory = Directory.CreateDirectory(Path.Combine(tempDirectory, "desktop")).FullName;
        var service = new GameShortcutService(LauncherProfiles.BlueArchiveJapan, new LocalizationService());
        var snapshot = new LauncherStatusSnapshot
        {
            LocalGame = new LocalInstallationState
            {
                GamePath = gameDirectory,
                GameConfig = new GameLauncherConfig { Name = "CafeTestGame" }
            }
        };

        var result = await service.CreateShortcutInDirectoryAsync(snapshot, shortcutDirectory);

        Assert.Equal(GameShortcutStatus.GameNotResolved, result.Status);
        Assert.False(File.Exists(Path.Combine(shortcutDirectory, "BlueArchive_JP.lnk")));
    }

    [WindowsFact]
    public async Task CreateShortcutInDirectoryAsync_WhenGameClientPresent_PinsIconToGameClient()
    {
        var gameDirectory = Directory.CreateDirectory(Path.Combine(tempDirectory, "game")).FullName;
        File.WriteAllText(Path.Combine(gameDirectory, "CafeTestGame.exe"), string.Empty);
        CreateStartScript(gameDirectory);
        var gameClientPath = Path.Combine(gameDirectory, LauncherProfiles.BlueArchiveJapan.GameExecutableFileName);
        File.WriteAllText(gameClientPath, string.Empty);
        var shortcutDirectory = Directory.CreateDirectory(Path.Combine(tempDirectory, "desktop")).FullName;
        var service = new GameShortcutService(LauncherProfiles.BlueArchiveJapan, new LocalizationService());
        var snapshot = new LauncherStatusSnapshot
        {
            LocalGame = new LocalInstallationState
            {
                GamePath = gameDirectory,
                GameConfig = new GameLauncherConfig { Name = "CafeTestGame" }
            }
        };

        var result = await service.CreateShortcutInDirectoryAsync(snapshot, shortcutDirectory);

        Assert.Equal(GameShortcutStatus.Created, result.Status);
        var (iconPath, iconIndex) = ReadShortcutIconLocation(result.Detail);
        Assert.Equal(gameClientPath, iconPath);
        Assert.Equal(0, iconIndex);
    }

    [WindowsFact]
    public async Task CreateShortcutInDirectoryAsync_WhenGameClientMissing_FallsBackToStartExecutableIcon()
    {
        var gameDirectory = Directory.CreateDirectory(Path.Combine(tempDirectory, "game")).FullName;
        var executablePath = Path.Combine(gameDirectory, "CafeTestGame.exe");
        File.WriteAllText(executablePath, string.Empty);
        CreateStartScript(gameDirectory);
        var shortcutDirectory = Directory.CreateDirectory(Path.Combine(tempDirectory, "desktop")).FullName;
        var service = new GameShortcutService(LauncherProfiles.BlueArchiveJapan, new LocalizationService());
        var snapshot = new LauncherStatusSnapshot
        {
            LocalGame = new LocalInstallationState
            {
                GamePath = gameDirectory,
                GameConfig = new GameLauncherConfig { Name = "CafeTestGame" }
            }
        };

        var result = await service.CreateShortcutInDirectoryAsync(snapshot, shortcutDirectory);

        Assert.Equal(GameShortcutStatus.Created, result.Status);
        var (iconPath, iconIndex) = ReadShortcutIconLocation(result.Detail);
        Assert.Equal(executablePath, iconPath);
        Assert.Equal(0, iconIndex);
    }

    [Fact]
    public void ResolveShortcutIconPath_WhenGameClientPresent_PrefersGameClientPath()
    {
        var gameDirectory = Directory.CreateDirectory(Path.Combine(tempDirectory, "game")).FullName;
        File.WriteAllText(Path.Combine(gameDirectory, LauncherProfiles.BlueArchiveJapan.GameExecutableFileName), string.Empty);
        var snapshot = new LauncherStatusSnapshot
        {
            LocalGame = new LocalInstallationState { GamePath = gameDirectory }
        };

        var iconPath = GameShortcutService.ResolveShortcutIconPath(snapshot, @"C:\games\loader.exe", LauncherProfiles.BlueArchiveJapan.GameExecutableFileName);

        Assert.Equal(Path.Combine(gameDirectory, LauncherProfiles.BlueArchiveJapan.GameExecutableFileName), iconPath);
    }

    [Fact]
    public void ResolveShortcutIconPath_WhenGameClientMissing_ReturnsStartExecutablePath()
    {
        var gameDirectory = Directory.CreateDirectory(Path.Combine(tempDirectory, "game")).FullName;
        var snapshot = new LauncherStatusSnapshot
        {
            LocalGame = new LocalInstallationState { GamePath = gameDirectory }
        };

        var iconPath = GameShortcutService.ResolveShortcutIconPath(snapshot, @"C:\games\loader.exe", LauncherProfiles.BlueArchiveJapan.GameExecutableFileName);

        Assert.Equal(@"C:\games\loader.exe", iconPath);
    }

    [Fact]
    public void ResolveStableShortcutFileName_IgnoresUiLanguageAndExecutableName()
    {
        var localizer = new LocalizationService();
        var service = new GameShortcutService(LauncherProfiles.BlueArchiveJapan, localizer);
        try
        {
            localizer.SetLanguage(LauncherLanguages.Japanese);
            var japaneseFileName = service.ResolveStableShortcutFileName(@"C:\games\BlueArchive_JP.exe");

            localizer.SetLanguage(LauncherLanguages.SimplifiedChinese);
            var chineseFileName = service.ResolveStableShortcutFileName(@"C:\games\BlueArchive_JP.exe");

            // 稳定名来自游戏档案的安装目录名，不随界面语言或宿主可执行文件名变化（AUD-GAME-002）。
            Assert.Equal("BlueArchive_JP", japaneseFileName);
            Assert.Equal(japaneseFileName, chineseFileName);
        }
        finally
        {
            localizer.SetLanguage(LauncherLanguages.English);
        }
    }

    [Fact]
    public void SanitizeFileName_WhenNameContainsPathSeparator_ReplacesIt()
    {
        // 跨平台契约：路径分隔符在所有平台的非法字符集内，替换后修剪尾随空格。
        Assert.Equal("a b", GameShortcutService.SanitizeFileName("a/b"));
    }

    [Fact]
    public void SanitizeFileName_WhenNameContainsWindowsInvalidCharacters_ReplacesThem()
    {
        // 非法字符集来自运行平台的 Path.GetInvalidFileNameChars()——.lnk 与
        // .desktop 分别在各自平台创建，字符集随平台是正确行为（AUD-CI-004）。
        // 本用例钉住 Windows 集（<>:|? 均非法）；Linux 的集合仅含 '/' 与 '\0'。
        Assert.SkipUnless(
            OperatingSystem.IsWindows(),
            "Windows 专属非法字符集断言仅在 Windows 上有效。");
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var sanitized = GameShortcutService.SanitizeFileName("Blue<>:Archive|?");

        Assert.Equal("Blue   Archive", sanitized);
    }

    // 非 Windows 上先做可见跳过（SkipResult 会中止调用方测试），后面的早退
    // return 是给平台分析器（CA1416）的守卫，在跳过之后不可达。
    private static (string IconPath, int IconIndex) ReadShortcutIconLocation(string shortcutPath)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Shell Link COM 接口仅在 Windows 上可用。");
        if (!OperatingSystem.IsWindows())
        {
            return ("", -1);
        }

        var shellLink = (TestShellLinkW)new ShellLinkCom();
        try
        {
            ((IPersistFile)shellLink).Load(shortcutPath, 0);
            var iconPath = new StringBuilder(260);
            shellLink.GetIconLocation(iconPath, iconPath.Capacity, out var iconIndex);
            return (iconPath.ToString(), iconIndex);
        }
        finally
        {
            _ = Marshal.ReleaseComObject(shellLink);
        }
    }

    private static (string TargetPath, string WorkingDirectory, string Arguments) ReadShortcutTarget(string shortcutPath)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Shell Link COM 接口仅在 Windows 上可用。");
        if (!OperatingSystem.IsWindows())
        {
            return ("", "", "");
        }

        var shellLink = (TestShellLinkW)new ShellLinkCom();
        try
        {
            ((IPersistFile)shellLink).Load(shortcutPath, 0);
            var targetPath = new StringBuilder(260);
            shellLink.GetPath(targetPath, targetPath.Capacity, IntPtr.Zero, 0);
            var workingDirectory = new StringBuilder(260);
            shellLink.GetWorkingDirectory(workingDirectory, workingDirectory.Capacity);
            var arguments = new StringBuilder(260);
            shellLink.GetArguments(arguments, arguments.Capacity);
            return (targetPath.ToString(), workingDirectory.ToString(), arguments.ToString());
        }
        finally
        {
            _ = Marshal.ReleaseComObject(shellLink);
        }
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLinkCom
    {
    }

    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface TestShellLinkW
    {
        void GetPath(
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile,
            int cchMax,
            IntPtr findData,
            uint flags);

        void GetIDList(out IntPtr pointerIdList);

        void SetIDList(IntPtr pointerIdList);

        void GetDescription(
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile,
            int cchMax);

        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszFile);

        void GetWorkingDirectory(
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile,
            int cchMax);

        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszFile);

        void GetArguments(
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile,
            int cchMax);

        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszFile);

        void GetHotkey(out short hotkey);

        void SetHotkey(short hotkey);

        void GetShowCmd(out int showCmd);

        void SetShowCmd(int showCmd);

        void GetIconLocation(
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath,
            int cchMax,
            out int iconIndex);
    }

    [WindowsFact]
    public async Task CreateShortcutInDirectoryAsync_WhenGameUnresolved_ReturnsGameNotResolved()
    {
        var shortcutDirectory = Directory.CreateDirectory(Path.Combine(tempDirectory, "desktop")).FullName;
        var service = new GameShortcutService(LauncherProfiles.BlueArchiveJapan, new LocalizationService());
        var snapshot = new LauncherStatusSnapshot
        {
            LocalGame = new LocalInstallationState { GamePath = tempDirectory }
        };

        var result = await service.CreateShortcutInDirectoryAsync(snapshot, shortcutDirectory);

        Assert.Equal(GameShortcutStatus.GameNotResolved, result.Status);
    }

    [Fact]
    public void BuildDesktopEntry_WithLauncherPath_IncludesLaunchGameArgument()
    {
        var content = GameShortcutService.BuildDesktopEntry(
            "Blue Archive",
            @"/opt/cafe-launcher/Cafe.Launcher",
            "/opt/cafe-launcher/Assets/app-icon.ico");

        var lines = content.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(
            [
                "[Desktop Entry]",
                "Type=Application",
                "Name=Blue Archive",
                $"Exec=\"/opt/cafe-launcher/Cafe.Launcher\" {Program.LaunchGameArgument}",
                "Icon=/opt/cafe-launcher/Assets/app-icon.ico",
                "Terminal=false",
                "Categories=Game;"
            ],
            lines);
    }

    [Fact]
    public void BuildDesktopEntry_WhenIconMissing_OmitsIconLine()
    {
        var content = GameShortcutService.BuildDesktopEntry(
            "Blue Archive",
            @"/opt/cafe-launcher/Cafe.Launcher",
            iconPath: null);

        Assert.DoesNotContain("Icon=", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateShortcutInDirectoryAsync_OnLinux_WritesDesktopEntryThatLaunchesThroughLauncher()
    {
        var launcherDirectory = Directory.CreateDirectory(Path.Combine(tempDirectory, "launcher")).FullName;
        var launcherPath = Path.Combine(launcherDirectory, "Cafe.Launcher");
        File.WriteAllText(launcherPath, string.Empty);
        var gameDirectory = Directory.CreateDirectory(Path.Combine(tempDirectory, "game")).FullName;
        File.WriteAllText(Path.Combine(gameDirectory, "CafeTestGame.exe"), string.Empty);
        var shortcutDirectory = Directory.CreateDirectory(Path.Combine(tempDirectory, "desktop")).FullName;
        var service = CreateLinuxService(launcherPath);
        var snapshot = new LauncherStatusSnapshot
        {
            LocalGame = new LocalInstallationState
            {
                GamePath = gameDirectory,
                GameConfig = new GameLauncherConfig { Name = "CafeTestGame" }
            }
        };

        var result = await service.CreateShortcutInDirectoryAsync(snapshot, shortcutDirectory);

        Assert.Equal(GameShortcutStatus.Created, result.Status);
        var entryPath = Path.Combine(shortcutDirectory, "BlueArchive_JP.desktop");
        Assert.True(File.Exists(entryPath));
        Assert.Equal(entryPath, result.Detail);
        var content = File.ReadAllText(entryPath);
        Assert.Contains("[Desktop Entry]", content, StringComparison.Ordinal);
        Assert.Contains("Type=Application", content, StringComparison.Ordinal);
        Assert.Contains("Name=Blue Archive", content, StringComparison.Ordinal);
        Assert.Contains($"Exec=\"{launcherPath}\" {Program.LaunchGameArgument}", content, StringComparison.Ordinal);
        Assert.Contains("Terminal=false", content, StringComparison.Ordinal);
        Assert.Contains("Categories=Game;", content, StringComparison.Ordinal);
        Assert.DoesNotContain(".exe\"", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateShortcutInDirectoryAsync_OnLinux_WhenIconAssetPresent_ReferencesIt()
    {
        var launcherDirectory = Directory.CreateDirectory(Path.Combine(tempDirectory, "launcher")).FullName;
        var launcherPath = Path.Combine(launcherDirectory, "Cafe.Launcher");
        File.WriteAllText(launcherPath, string.Empty);
        Directory.CreateDirectory(Path.Combine(launcherDirectory, "Assets"));
        var iconPath = Path.Combine(launcherDirectory, "Assets", "app-icon.ico");
        File.WriteAllText(iconPath, string.Empty);
        var gameDirectory = Directory.CreateDirectory(Path.Combine(tempDirectory, "game")).FullName;
        File.WriteAllText(Path.Combine(gameDirectory, "CafeTestGame.exe"), string.Empty);
        var shortcutDirectory = Directory.CreateDirectory(Path.Combine(tempDirectory, "desktop")).FullName;
        var service = CreateLinuxService(launcherPath);
        var snapshot = new LauncherStatusSnapshot
        {
            LocalGame = new LocalInstallationState
            {
                GamePath = gameDirectory,
                GameConfig = new GameLauncherConfig { Name = "CafeTestGame" }
            }
        };

        var result = await service.CreateShortcutInDirectoryAsync(snapshot, shortcutDirectory);

        Assert.Equal(GameShortcutStatus.Created, result.Status);
        Assert.Contains(
            $"Icon={iconPath}",
            File.ReadAllText(result.Detail),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateShortcutInDirectoryAsync_OnLinuxWithMissingLauncher_ReturnsGameNotResolved()
    {
        var shortcutDirectory = Directory.CreateDirectory(Path.Combine(tempDirectory, "desktop")).FullName;
        var service = CreateLinuxService(Path.Combine(tempDirectory, "missing", "Cafe.Launcher"));
        var snapshot = new LauncherStatusSnapshot
        {
            LocalGame = new LocalInstallationState { GamePath = tempDirectory }
        };

        var result = await service.CreateShortcutInDirectoryAsync(snapshot, shortcutDirectory);

        Assert.Equal(GameShortcutStatus.GameNotResolved, result.Status);
        Assert.Empty(result.Detail);
    }

    [Fact]
    public async Task CreateShortcutInDirectoryAsync_OnLinux_WhenGameUnresolved_ReturnsGameNotResolved()
    {
        var launcherDirectory = Directory.CreateDirectory(Path.Combine(tempDirectory, "launcher")).FullName;
        var launcherPath = Path.Combine(launcherDirectory, "Cafe.Launcher");
        File.WriteAllText(launcherPath, string.Empty);
        var shortcutDirectory = Directory.CreateDirectory(Path.Combine(tempDirectory, "desktop")).FullName;
        var service = CreateLinuxService(launcherPath);
        var snapshot = new LauncherStatusSnapshot
        {
            LocalGame = new LocalInstallationState { GamePath = tempDirectory }
        };

        var result = await service.CreateShortcutInDirectoryAsync(snapshot, shortcutDirectory);

        Assert.Equal(GameShortcutStatus.GameNotResolved, result.Status);
    }

    [Fact]
    public async Task CreateShortcutInDirectoryAsync_OnUnsupportedPlatform_ReturnsUnsupportedPlatform()
    {
        var shortcutDirectory = Directory.CreateDirectory(Path.Combine(tempDirectory, "desktop")).FullName;
        var service = new GameShortcutService(LauncherProfiles.BlueArchiveJapan, 
            new LocalizationService(),
            new GameShortcutService.ShortcutEnvironment(
                OpenDirectory: _ => true,
                IsWindowsPlatform: () => false,
                IsLinuxPlatform: () => false,
                LauncherExecutablePath: () => null));

        var result = await service.CreateShortcutInDirectoryAsync(
            new LauncherStatusSnapshot(),
            shortcutDirectory);

        Assert.Equal(GameShortcutStatus.UnsupportedPlatform, result.Status);
    }

    /// <summary>
    /// AUD-GAME-002 的核心回归：语言 A 创建（稳定名）+ 四种语言的旧入口，语言 B 卸载后
    /// 一个都不剩，同目录的无关桌面文件保留。
    /// </summary>
    [Fact]
    public async Task DeleteShortcutInDirectory_AfterLanguageSwitch_RemovesStableAndEveryLocalizedEntry()
    {
        var launcherPath = CreateLauncher();
        var gameDirectory = CreateGameDirectoryWithClient();
        var desktopDirectory = Directory.CreateDirectory(Path.Combine(tempDirectory, "desktop")).FullName;
        var localizer = new LocalizationService();
        var service = CreateLinuxService(launcherPath, localizer);
        var snapshot = Snapshot(gameDirectory);
        try
        {
            localizer.SetLanguage(LauncherLanguages.Japanese);
            var created = await service.CreateShortcutInDirectoryAsync(snapshot, desktopDirectory);
            var stablePath = Path.Combine(desktopDirectory, "BlueArchive_JP.desktop");
            Assert.Equal(GameShortcutStatus.Created, created.Status);
            Assert.Equal(stablePath, created.Detail);

            // 旧版本按任一语言写下的入口；四种语言都必须被认出来。
            var legacyPaths = WriteLegacyLocalizedShortcuts(localizer, desktopDirectory, ".desktop");
            Assert.Equal(4, legacyPaths.Count);
            var unrelatedPath = Path.Combine(desktopDirectory, "Vacation Photo.desktop");
            File.WriteAllText(unrelatedPath, "unrelated entry");

            localizer.SetLanguage(LauncherLanguages.SimplifiedChinese);
            var result = service.DeleteShortcutInDirectory(snapshot, desktopDirectory);

            Assert.Equal(GameShortcutStatus.Deleted, result.Status);
            Assert.False(File.Exists(stablePath));
            Assert.All(legacyPaths, path => Assert.False(File.Exists(path)));
            Assert.True(File.Exists(unrelatedPath));
            Assert.Equal([unrelatedPath], Directory.GetFiles(desktopDirectory));
        }
        finally
        {
            localizer.SetLanguage(LauncherLanguages.English);
        }
    }

    [Fact]
    public void DeleteShortcutInDirectory_WhenOnlyLocalizedLegacyEntryExists_RemovesIt()
    {
        var desktopDirectory = Directory.CreateDirectory(Path.Combine(tempDirectory, "desktop")).FullName;
        var gameDirectory = Directory.CreateDirectory(Path.Combine(tempDirectory, "game")).FullName;
        var localizer = new LocalizationService();
        var service = CreateLinuxService(Path.Combine(tempDirectory, "launcher", "Cafe.Launcher"), localizer);
        var snapshot = new LauncherStatusSnapshot
        {
            LocalGame = new LocalInstallationState { GamePath = gameDirectory }
        };
        try
        {
            var legacyPaths = WriteLegacyLocalizedShortcuts(localizer, desktopDirectory, ".desktop");
            localizer.SetLanguage(LauncherLanguages.TraditionalChinese);

            var result = service.DeleteShortcutInDirectory(snapshot, desktopDirectory);

            Assert.Equal(GameShortcutStatus.Deleted, result.Status);
            Assert.All(legacyPaths, path => Assert.False(File.Exists(path)));
        }
        finally
        {
            localizer.SetLanguage(LauncherLanguages.English);
        }
    }

    /// <summary>创建时清理同目录旧语言入口；无关文件不动，最终只剩稳定名与无关文件。</summary>
    [Fact]
    public async Task CreateShortcutInDirectoryAsync_AfterLanguageSwitch_RemovesLegacyEntriesAndKeepsSingleStableEntry()
    {
        var launcherPath = CreateLauncher();
        var gameDirectory = CreateGameDirectoryWithClient();
        var desktopDirectory = Directory.CreateDirectory(Path.Combine(tempDirectory, "desktop")).FullName;
        var localizer = new LocalizationService();
        var service = CreateLinuxService(launcherPath, localizer);
        var snapshot = Snapshot(gameDirectory);
        try
        {
            var legacyPaths = WriteLegacyLocalizedShortcuts(localizer, desktopDirectory, ".desktop");
            var unrelatedPath = Path.Combine(desktopDirectory, "Notes.desktop");
            File.WriteAllText(unrelatedPath, "unrelated entry");

            localizer.SetLanguage(LauncherLanguages.Japanese);
            var result = await service.CreateShortcutInDirectoryAsync(snapshot, desktopDirectory);

            var stablePath = Path.Combine(desktopDirectory, "BlueArchive_JP.desktop");
            Assert.Equal(GameShortcutStatus.Created, result.Status);
            Assert.Equal(stablePath, result.Detail);
            Assert.True(File.Exists(stablePath));
            Assert.All(legacyPaths, path => Assert.False(File.Exists(path)));
            Assert.True(File.Exists(unrelatedPath));
            Assert.Equal(
                new[] { stablePath, unrelatedPath }.Order(),
                Directory.GetFiles(desktopDirectory).Order());
        }
        finally
        {
            localizer.SetLanguage(LauncherLanguages.English);
        }
    }

    [Fact]
    public async Task CreateShortcutInDirectoryAsync_WhenLanguageChangedBetweenCalls_KeepsOneStableEntry()
    {
        var launcherPath = CreateLauncher();
        var gameDirectory = CreateGameDirectoryWithClient();
        var desktopDirectory = Directory.CreateDirectory(Path.Combine(tempDirectory, "desktop")).FullName;
        var localizer = new LocalizationService();
        var service = CreateLinuxService(launcherPath, localizer);
        var snapshot = Snapshot(gameDirectory);
        try
        {
            localizer.SetLanguage(LauncherLanguages.Japanese);
            var first = await service.CreateShortcutInDirectoryAsync(snapshot, desktopDirectory);
            localizer.SetLanguage(LauncherLanguages.English);
            var second = await service.CreateShortcutInDirectoryAsync(snapshot, desktopDirectory);

            var stablePath = Path.Combine(desktopDirectory, "BlueArchive_JP.desktop");
            Assert.Equal(GameShortcutStatus.Created, first.Status);
            Assert.Equal(GameShortcutStatus.Created, second.Status);
            Assert.Equal(first.Detail, second.Detail);
            Assert.Equal([stablePath], Directory.GetFiles(desktopDirectory));
        }
        finally
        {
            localizer.SetLanguage(LauncherLanguages.English);
        }
    }

    /// <summary>真实 .lnk 的跨语言建删：创建语言 A、清理语言 B 后不残留任何已知名字。</summary>
    [WindowsFact]
    public async Task CreateShortcutInDirectoryAsync_WhenLanguageSwitched_ReplacesLocalizedLnkAndDeletesEveryName()
    {
        var gameDirectory = CreateGameDirectoryWithClient();
        CreateStartScript(gameDirectory);
        var desktopDirectory = Directory.CreateDirectory(Path.Combine(tempDirectory, "desktop")).FullName;
        var localizer = new LocalizationService();
        var service = new GameShortcutService(LauncherProfiles.BlueArchiveJapan, localizer);
        var snapshot = Snapshot(gameDirectory);
        try
        {
            localizer.SetLanguage(LauncherLanguages.Japanese);
            var first = await service.CreateShortcutInDirectoryAsync(snapshot, desktopDirectory);
            var stablePath = Path.Combine(desktopDirectory, "BlueArchive_JP.lnk");
            Assert.Equal(GameShortcutStatus.Created, first.Status);
            Assert.Equal(stablePath, first.Detail);

            var legacyPaths = WriteLegacyLocalizedShortcuts(localizer, desktopDirectory, ".lnk");
            var unrelatedPath = Path.Combine(desktopDirectory, "Groceries.lnk");
            File.WriteAllText(unrelatedPath, "unrelated entry");

            localizer.SetLanguage(LauncherLanguages.English);
            var second = await service.CreateShortcutInDirectoryAsync(snapshot, desktopDirectory);
            Assert.Equal(GameShortcutStatus.Created, second.Status);
            Assert.All(legacyPaths, path => Assert.False(File.Exists(path)));

            var deletion = service.DeleteShortcutInDirectory(snapshot, desktopDirectory);
            Assert.Equal(GameShortcutStatus.Deleted, deletion.Status);
            Assert.False(File.Exists(stablePath));
            Assert.True(File.Exists(unrelatedPath));
        }
        finally
        {
            localizer.SetLanguage(LauncherLanguages.English);
        }
    }

    private GameShortcutService CreateLinuxService(string launcherPath, LocalizationService? localizer = null) =>
        new(
            LauncherProfiles.BlueArchiveJapan,
            localizer ?? new LocalizationService(),
            new GameShortcutService.ShortcutEnvironment(
                OpenDirectory: _ => true,
                IsWindowsPlatform: () => false,
                IsLinuxPlatform: () => true,
                LauncherExecutablePath: () => launcherPath));

    private string CreateLauncher()
    {
        var launcherDirectory = Directory.CreateDirectory(Path.Combine(tempDirectory, "launcher")).FullName;
        var launcherPath = Path.Combine(launcherDirectory, "Cafe.Launcher");
        File.WriteAllText(launcherPath, string.Empty);
        return launcherPath;
    }

    private string CreateGameDirectoryWithClient()
    {
        var gameDirectory = Directory.CreateDirectory(Path.Combine(tempDirectory, "game")).FullName;
        File.WriteAllText(Path.Combine(gameDirectory, "CafeTestGame.exe"), string.Empty);
        return gameDirectory;
    }

    private static LauncherStatusSnapshot Snapshot(string gameDirectory) => new()
    {
        LocalGame = new LocalInstallationState
        {
            GamePath = gameDirectory,
            GameConfig = new GameLauncherConfig { Name = "CafeTestGame" }
        }
    };

    /// <summary>
    /// 按仓库支持的每种界面语言写出旧版本会生成的入口文件名（本地化显示名 + 平台后缀），
    /// 用生产本地化资源而不是测试内翻译表。
    /// </summary>
    private static List<string> WriteLegacyLocalizedShortcuts(
        LocalizationService localizer,
        string directory,
        string extension)
    {
        string[] languages =
        [
            LauncherLanguages.English,
            LauncherLanguages.SimplifiedChinese,
            LauncherLanguages.TraditionalChinese,
            LauncherLanguages.Japanese
        ];

        var paths = new List<string>(languages.Length);
        foreach (var language in languages)
        {
            localizer.SetLanguage(language);
            var displayName = GameShortcutService.SanitizeFileName(
                localizer.T(LocalizationKeys.GameDisplayName));
            var path = Path.Combine(directory, $"{displayName}{extension}");
            File.WriteAllText(path, "legacy entry");
            paths.Add(path);
        }

        return paths;
    }

    /// <summary>Creates the run.bat start script every valid game folder ships with.</summary>
    private static string CreateStartScript(string gameDirectory)
    {
        var startScriptPath = Path.Combine(gameDirectory, LauncherProfiles.BlueArchiveJapan.GameStartScriptFileName);
        File.WriteAllText(startScriptPath, string.Empty);
        return startScriptPath;
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    private sealed class WindowsFactAttribute : FactAttribute
    {
        public WindowsFactAttribute(
            [CallerFilePath] string sourceFilePath = "",
            [CallerLineNumber] int sourceLineNumber = -1)
            : base(sourceFilePath, sourceLineNumber)
        {
            if (!OperatingSystem.IsWindows())
            {
                Skip = "Shortcut creation uses Windows shell COM interfaces.";
            }
        }
    }
}
