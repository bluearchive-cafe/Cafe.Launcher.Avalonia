using System;
using System.Collections.Generic;
using System.Threading;
using Cafe.Launcher.UI.Services.GameRuntime;
using Cafe.Launcher.Core.Services.GameRuntime;
using Cafe.Launcher.Testing;

namespace Cafe.Launcher.Tests;

public sealed class LinuxProcessScannerTests
{
    private static readonly IReadOnlyList<string> Family = GameProcessNames.FromLaunchConfiguration(
        "xldr_BlueArchiveOnline_JP_loader_x64",
        ["BlueArchive.exe"]);

    private static readonly RunningGameQuery Query = new(Family);

    [Fact]
    public void Scan_WhenProcEnumerationFails_PropagatesTheFailure()
    {
        using var directory = TestDirectory.Create();

        Assert.Throws<DirectoryNotFoundException>(() =>
            LinuxProcessScanner.Scan(Query, directory.Sub("missing-proc"), CancellationToken.None));
    }

    [Fact]
    public void Scan_WhenOneProcessDisappears_StillFindsTheFamily()
    {
        using var directory = TestDirectory.Create();
        Directory.CreateDirectory(directory.Sub("123"));
        Directory.CreateDirectory(directory.Sub("456"));
        File.WriteAllText(Path.Combine(directory.Sub("456"), "comm"), "BlueArchive.exe");

        Assert.Equal(["BlueArchive"], LinuxProcessScanner.Scan(Query, directory.Path, CancellationToken.None));
    }

    [Theory]
    [InlineData("xxd-0.xem", true, false, "xxd-0.xem")]
    [InlineData("wine64-preloader", false, true, "BlueArchive")]
    [InlineData("unrelated", false, false, null)]
    public void Scan_WhenProcessHasNoExeArgument_UsesOwnershipAndMappings(
        string comm, bool marked, bool mapped, string? expected)
    {
        using var directory = TestDirectory.Create();
        var process = directory.Sub("123");
        Directory.CreateDirectory(process);
        File.WriteAllText(Path.Combine(process, "comm"), comm);
        File.WriteAllText(Path.Combine(process, "cmdline"), comm + "\0");
        File.WriteAllText(Path.Combine(process, "environ"), marked
            ? UnixGameProcessMatcher.OwnershipMarkerKey + "=blue-archive-jp\0"
            : "");
        File.WriteAllText(Path.Combine(process, "maps"), mapped
            ? "1000-2000 r-xp 00000000 00:00 123 /games/BlueArchive/BlueArchive.exe\n"
            : "");

        var running = LinuxProcessScanner.Scan(
            new RunningGameQuery(Family, "/games/BlueArchive"), directory.Path, CancellationToken.None);

        if (expected is null)
        {
            Assert.Empty(running);
        }
        else
        {
            Assert.Equal([expected], running);
        }
    }

    [Fact]
    public void SelectRunning_PrefersFamilyNamesWhenBothMarkerAndFamilyMatch()
    {
        var helper = Record(
            "winedevice.exe",
            ["winedevice.exe"],
            environment: [(UnixGameProcessMatcher.OwnershipMarkerKey, "blue-archive-jp")]);
        var game = Record(
            "BlueArchive.exe",
            ["wine64-preloader", "S:\\YostarGames\\BlueArchive_JP\\BlueArchive.exe"]);

        var running = LinuxProcessScanner.SelectRunning([helper, game], Query);

        Assert.Equal(["BlueArchive"], running);
    }

    [Theory]
    [InlineData("umu-run")]
    [InlineData("wine")]
    [InlineData("wine64")]
    [InlineData("python3")]
    [InlineData("srt-bwrap")]
    public void SelectRunning_WhenMarkedRunnerOnlyMentionsGameArguments_ReportsRunnerName(string runner)
    {
        var process = Record(runner,
            ["/usr/bin/" + runner, "/games/xldr_BlueArchiveOnline_JP_loader_x64.exe", "BlueArchive.exe"],
            environment: [(UnixGameProcessMatcher.OwnershipMarkerKey, "blue-archive-jp")]);

        Assert.Equal([runner], LinuxProcessScanner.SelectRunning([process], Query));
    }

    [Theory]
    [InlineData("wine-preloader")]
    [InlineData("wine64-preloader")]
    public void SelectRunning_WhenPreloaderExecutesGame_ReportsGameName(string preloader)
    {
        var process = Record(preloader, ["/usr/bin/" + preloader, "/games/BlueArchive.exe"],
            environment: [(UnixGameProcessMatcher.OwnershipMarkerKey, "blue-archive-jp")]);

        Assert.Equal(["BlueArchive"], LinuxProcessScanner.SelectRunning([process], Query));
    }

    [Fact]
    public void SelectRunning_WhenOnlyTheOwnershipMarkerIsPresent_ReportsThatProcess()
    {
        var game = Record(
            "mysuperlonggam",
            ["mysuperlonggamename"],
            environment: [(UnixGameProcessMatcher.OwnershipMarkerKey, "blue-archive-jp")]);

        var running = LinuxProcessScanner.SelectRunning([game], Query);

        Assert.Equal(["mysuperlonggam"], running);
    }

    [Fact]
    public void SelectRunning_WhenAProcessMapsTheInstallDirectory_Matches()
    {
        var game = Record(
            "wine64-preloader",
            ["wine64-preloader"],
            mappedFiles: ["/usr/lib/libc.so.6", "/games/BlueArchive/BlueArchive.exe"]);

        var running = LinuxProcessScanner.SelectRunning(
            [game],
            new RunningGameQuery(Family, InstallDirectory: "/games/BlueArchive"));

        Assert.Equal(["BlueArchive"], running);
    }

    [Fact]
    public void SelectRunning_WhenAProcessMapsASiblingDirectory_ReturnsEmpty()
    {
        var unrelated = Record(
            "wine64-preloader",
            ["wine64-preloader"],
            mappedFiles: ["/games/BlueArchiveBackup/BlueArchive.exe"]);

        Assert.Empty(LinuxProcessScanner.SelectRunning(
            [unrelated],
            new RunningGameQuery(Family, InstallDirectory: "/games/BlueArchive")));
    }

    [Fact]
    public void SelectRunning_WhenNothingIdentifiesAProcess_ReturnsEmpty()
    {
        var unrelated = Record("vim", ["vim", "/games/BlueArchive/BlueArchive.exe"]);

        Assert.Empty(LinuxProcessScanner.SelectRunning([unrelated], Query));
    }

    [Fact]
    public void SelectRunningProtonBuild_ReturnsTheBuildDeclaredByAMarkedProcess()
    {
        var unmarked = Record("wine", ["wine"], environment: [("PROTONPATH", "/other/proton")]);
        var marked = Record(
            "umu.exe",
            ["umu.exe"],
            environment:
            [
                (UnixGameProcessMatcher.OwnershipMarkerKey, "blue-archive-jp"),
                ("PROTONPATH", "/home/u/.local/share/Steam/compatibilitytools.d/UMU-Proton-10.0-4")
            ]);

        Assert.Equal(
            "/home/u/.local/share/Steam/compatibilitytools.d/UMU-Proton-10.0-4",
            LinuxProcessScanner.SelectRunningProtonBuild([unmarked, marked]));
    }

    [Fact]
    public void Scan_OnLinux_RunsWithoutThrowing()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "The scanner reads /proc.");

        _ = LinuxProcessScanner.Scan(Query, CancellationToken.None);
    }

    private static UnixProcessRecord Record(
        string comm,
        string[] arguments,
        string[]? mappedFiles = null,
        (string Key, string Value)[]? environment = null) =>
        new(
            ProcessId: 1,
            ParentProcessId: 0,
            Comm: comm,
            Arguments: arguments,
            MappedFiles: mappedFiles ?? [],
            Environment: environment?.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal)
                ?? new Dictionary<string, string>(StringComparer.Ordinal));
}
