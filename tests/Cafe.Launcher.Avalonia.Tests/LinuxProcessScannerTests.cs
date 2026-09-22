using System;
using System.Collections.Generic;
using System.Threading;
using Cafe.Launcher.Avalonia.Services.GameRuntime;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class LinuxProcessScannerTests
{
    private static readonly IReadOnlyList<string> Family = GameProcessNames.FromLaunchConfiguration(
        "xldr_BlueArchiveOnline_JP_loader_x64",
        ["BlueArchive.exe"]);

    private static readonly RunningGameQuery Query = new(Family);

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
