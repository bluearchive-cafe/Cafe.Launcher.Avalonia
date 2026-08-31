using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Features.GameOperations;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class GameShortcutServiceTests : IDisposable
{
    private readonly string tempDirectory;

    static GameShortcutServiceTests()
    {
        TestLocalizationHelper.Initialize();
    }

    public GameShortcutServiceTests()
    {
        tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
    }

    public void Dispose()
    {
        Directory.Delete(tempDirectory, recursive: true);
    }

    [Fact]
    public void TryResolveGameExecutable_WhenLocalNamePresent_PrefersLocalName()
    {
        var gameDirectory = Directory.CreateDirectory(Path.Combine(tempDirectory, "game")).FullName;
        File.WriteAllText(Path.Combine(gameDirectory, "LocalGame.exe"), string.Empty);
        var snapshot = new LauncherStatusSnapshot
        {
            LocalGame = new LocalInstallationState
            {
                GamePath = gameDirectory,
                GameConfig = new GameLauncherConfig { Name = "LocalGame" }
            },
            Remote = new LauncherRemoteState
            {
                GameConfig = new GameConfigResponse { GameStartExeName = "RemoteGame" }
            }
        };

        var resolved = GameShortcutService.TryResolveGameExecutable(
            snapshot,
            out var executablePath,
            out var workingDirectory,
            out var shortcutName);

        Assert.True(resolved);
        Assert.Equal(Path.Combine(gameDirectory, "LocalGame.exe"), executablePath);
        Assert.Equal(gameDirectory, workingDirectory);
        Assert.Equal("LocalGame", shortcutName);
    }

    [Fact]
    public void TryResolveGameExecutable_WhenLocalNameMissing_FallsBackToRemoteStartExeName()
    {
        var gameDirectory = Directory.CreateDirectory(Path.Combine(tempDirectory, "game")).FullName;
        File.WriteAllText(Path.Combine(gameDirectory, "RemoteGame.exe"), string.Empty);
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

        var resolved = GameShortcutService.TryResolveGameExecutable(
            snapshot,
            out var executablePath,
            out _,
            out var shortcutName);

        Assert.True(resolved);
        Assert.Equal(Path.Combine(gameDirectory, "RemoteGame.exe"), executablePath);
        Assert.Equal("RemoteGame", shortcutName);
    }

    [Fact]
    public void TryResolveGameExecutable_WhenNameContainsSeparator_RejectsResolution()
    {
        var gameDirectory = Directory.CreateDirectory(Path.Combine(tempDirectory, "game")).FullName;
        var snapshot = new LauncherStatusSnapshot
        {
            LocalGame = new LocalInstallationState
            {
                GamePath = gameDirectory,
                GameConfig = new GameLauncherConfig { Name = "..\\evil" }
            }
        };

        var resolved = GameShortcutService.TryResolveGameExecutable(snapshot, out _, out _, out _);

        Assert.False(resolved);
    }

    [Fact]
    public void TryResolveGameExecutable_WhenExecutableMissing_RejectsResolution()
    {
        var gameDirectory = Directory.CreateDirectory(Path.Combine(tempDirectory, "game")).FullName;
        var snapshot = new LauncherStatusSnapshot
        {
            LocalGame = new LocalInstallationState
            {
                GamePath = gameDirectory,
                GameConfig = new GameLauncherConfig { Name = "AbsentGame" }
            }
        };

        var resolved = GameShortcutService.TryResolveGameExecutable(snapshot, out _, out _, out _);

        Assert.False(resolved);
    }

    [Fact]
    public void TryOpenGameFolder_WhenFolderMissing_ReturnsFalseAndSkipsOpener()
    {
        var openerCalls = 0;
        var service = new GameShortcutService(new LocalizationService(), _ =>
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
        var service = new GameShortcutService(new LocalizationService(), directory =>
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
        var service = new GameShortcutService(new LocalizationService(), _ => false);
        var snapshot = new LauncherStatusSnapshot
        {
            LocalGame = new LocalInstallationState { GamePath = gameDirectory }
        };

        var opened = service.TryOpenGameFolder(snapshot);

        Assert.False(opened);
    }

    [WindowsFact]
    public async Task CreateShortcutInDirectoryAsync_WhenGameResolved_WritesShortcutFileNamedAfterGame()
    {
        var gameDirectory = Directory.CreateDirectory(Path.Combine(tempDirectory, "game")).FullName;
        var executablePath = Path.Combine(gameDirectory, "CafeTestGame.exe");
        File.WriteAllText(executablePath, string.Empty);
        CreateStartScript(gameDirectory);
        var shortcutDirectory = Directory.CreateDirectory(Path.Combine(tempDirectory, "desktop")).FullName;
        var service = new GameShortcutService(new LocalizationService());
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
        var expectedShortcutPath = Path.Combine(shortcutDirectory, "Blue Archive.lnk");
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
        var service = new GameShortcutService(new LocalizationService());
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
        var service = new GameShortcutService(new LocalizationService());
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
        Assert.False(File.Exists(Path.Combine(shortcutDirectory, "Blue Archive.lnk")));
    }

    [WindowsFact]
    public async Task CreateShortcutInDirectoryAsync_WhenGameClientPresent_PinsIconToGameClient()
    {
        var gameDirectory = Directory.CreateDirectory(Path.Combine(tempDirectory, "game")).FullName;
        File.WriteAllText(Path.Combine(gameDirectory, "CafeTestGame.exe"), string.Empty);
        CreateStartScript(gameDirectory);
        var gameClientPath = Path.Combine(gameDirectory, GamePaths.GameExecutableFileName);
        File.WriteAllText(gameClientPath, string.Empty);
        var shortcutDirectory = Directory.CreateDirectory(Path.Combine(tempDirectory, "desktop")).FullName;
        var service = new GameShortcutService(new LocalizationService());
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
        var service = new GameShortcutService(new LocalizationService());
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
        File.WriteAllText(Path.Combine(gameDirectory, GamePaths.GameExecutableFileName), string.Empty);
        var snapshot = new LauncherStatusSnapshot
        {
            LocalGame = new LocalInstallationState { GamePath = gameDirectory }
        };

        var iconPath = GameShortcutService.ResolveShortcutIconPath(snapshot, @"C:\games\loader.exe");

        Assert.Equal(Path.Combine(gameDirectory, GamePaths.GameExecutableFileName), iconPath);
    }

    [Fact]
    public void ResolveShortcutIconPath_WhenGameClientMissing_ReturnsStartExecutablePath()
    {
        var gameDirectory = Directory.CreateDirectory(Path.Combine(tempDirectory, "game")).FullName;
        var snapshot = new LauncherStatusSnapshot
        {
            LocalGame = new LocalInstallationState { GamePath = gameDirectory }
        };

        var iconPath = GameShortcutService.ResolveShortcutIconPath(snapshot, @"C:\games\loader.exe");

        Assert.Equal(@"C:\games\loader.exe", iconPath);
    }

    [Fact]
    public void ResolveShortcutFileName_UsesLocalizedGameDisplayName()
    {
        var service = new GameShortcutService(new LocalizationService());

        var fileName = service.ResolveShortcutFileName(@"C:\games\BlueArchive_JP.exe");

        Assert.Equal("Blue Archive", fileName);
    }

    [Fact]
    public void SanitizeFileName_WhenNameContainsInvalidCharacters_ReplacesThem()
    {
        var sanitized = GameShortcutService.SanitizeFileName("Blue<>:Archive|?");

        Assert.Equal("Blue   Archive", sanitized);
    }

    private static (string IconPath, int IconIndex) ReadShortcutIconLocation(string shortcutPath)
    {
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
        var service = new GameShortcutService(new LocalizationService());
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
        var entryPath = Path.Combine(shortcutDirectory, "Blue Archive.desktop");
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
        var service = new GameShortcutService(
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

    private GameShortcutService CreateLinuxService(string launcherPath) =>
        new(
            new LocalizationService(),
            new GameShortcutService.ShortcutEnvironment(
                OpenDirectory: _ => true,
                IsWindowsPlatform: () => false,
                IsLinuxPlatform: () => true,
                LauncherExecutablePath: () => launcherPath));

    /// <summary>Creates the run.bat start script every valid game folder ships with.</summary>
    private static string CreateStartScript(string gameDirectory)
    {
        var startScriptPath = Path.Combine(gameDirectory, GamePaths.GameStartScriptFileName);
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
