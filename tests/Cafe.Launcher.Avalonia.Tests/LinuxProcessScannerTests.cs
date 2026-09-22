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

        var running = LinuxProcessScanner.SelectRunning([helper, game], Family);

        Assert.Equal(["BlueArchive"], running);
    }

    [Fact]
    public void SelectRunning_WhenOnlyTheOwnershipMarkerIsPresent_ReportsThatProcess()
    {
        var game = Record(
            "mysuperlonggam",
            ["mysuperlonggamename"],
            environment: [(UnixGameProcessMatcher.OwnershipMarkerKey, "blue-archive-jp")]);

        var running = LinuxProcessScanner.SelectRunning([game], Family);

        Assert.Equal(["mysuperlonggam"], running);
    }

    [Fact]
    public void SelectRunning_WhenNothingIdentifiesAProcess_ReturnsEmpty()
    {
        var unrelated = Record("vim", ["vim", "/games/BlueArchive/BlueArchive.exe"]);

        Assert.Empty(LinuxProcessScanner.SelectRunning([unrelated], Family));
    }

    [Fact]
    public void Scan_OnLinux_RunsWithoutThrowing()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "The scanner reads /proc.");

        _ = LinuxProcessScanner.Scan(Family, CancellationToken.None);
    }

    private static UnixProcessRecord Record(
        string comm,
        string[] arguments,
        (string Key, string Value)[]? environment = null) =>
        new(
            ProcessId: 1,
            ParentProcessId: 0,
            Comm: comm,
            Arguments: arguments,
            MappedFiles: [],
            Environment: environment?.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal)
                ?? new Dictionary<string, string>(StringComparer.Ordinal));
}
