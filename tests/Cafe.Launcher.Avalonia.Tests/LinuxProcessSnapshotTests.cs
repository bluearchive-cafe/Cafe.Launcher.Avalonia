using System.Collections.Generic;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.Services.GameRuntime;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class LinuxProcessSnapshotTests
{
    [Fact]
    public void Format_KeepsRunnerProcessesAndDropsUnrelatedOnes()
    {
        var runnerByEnvironment = Record(
            processId: 100,
            comm: "wine64-preloader",
            arguments: ["wine64-preloader", "C:\\game\\BlueArchive.exe"],
            environment: [("WINEPREFIX", "/home/u/pfx/bluearchive")]);
        var runnerByArgument = Record(
            processId: 200,
            comm: "python3",
            arguments: ["python3", "/games/BlueArchive/BlueArchive.exe"]);
        var unrelated = Record(
            processId: 300,
            comm: "bash",
            arguments: ["/bin/bash"],
            environment: [("HOME", "/home/u")]);

        var snapshot = LinuxProcessSnapshot.Format([unrelated, runnerByArgument, runnerByEnvironment]);

        Assert.Contains("pid=100 ", snapshot, StringComparison.Ordinal);
        Assert.Contains("pid=200 ", snapshot, StringComparison.Ordinal);
        Assert.DoesNotContain("pid=300 ", snapshot, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_ShowsOnlyRunnerEnvironmentKeys()
    {
        var record = Record(
            processId: 100,
            comm: "wine64-preloader",
            arguments: ["wine64-preloader"],
            environment:
            [
                ("WINEPREFIX", "/home/u/pfx"),
                ("GAMEID", "umu-bluearchive"),
                ("STEAM_COMPAT_DATA_PATH", "/home/u/steam"),
                ("HOME", "/home/u")
            ]);

        var snapshot = LinuxProcessSnapshot.Format([record]);

        Assert.Contains("env WINEPREFIX=/home/u/pfx", snapshot, StringComparison.Ordinal);
        Assert.Contains("env GAMEID=umu-bluearchive", snapshot, StringComparison.Ordinal);
        Assert.Contains("env STEAM_COMPAT_DATA_PATH=/home/u/steam", snapshot, StringComparison.Ordinal);
        Assert.DoesNotContain("HOME=", snapshot, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_TruncatesWhenTheSnapshotExceedsTheLimit()
    {
        var records = Enumerable.Range(1, 3000).Select(index => Record(
            processId: index,
            comm: "wine64-preloader",
            arguments: ["wine64-preloader", new string('a', 200) + ".exe"],
            environment: [("WINEPREFIX", "/pfx")]));

        var snapshot = LinuxProcessSnapshot.Format(records);

        Assert.Contains("# truncated at", snapshot, StringComparison.Ordinal);
        Assert.True(snapshot.Length < LinuxProcessSnapshot.MaxCharacters + 256);
    }

    [Fact]
    public void ReadProcesses_OnLinux_IncludesTheCurrentProcessWithItsArguments()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "The process snapshot reads /proc, which only exists on Linux.");

        var records = LinuxProcessSnapshot.ReadProcesses(CancellationToken.None);
        var current = records.Single(record => record.ProcessId == Environment.ProcessId);

        Assert.NotEmpty(current.Arguments);
    }

    [Fact]
    public void Collect_OnLinux_ReturnsTheSnapshotHeader()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "The process snapshot reads /proc, which only exists on Linux.");

        var snapshot = LinuxProcessSnapshot.Collect(CancellationToken.None);

        Assert.StartsWith("# Cafe Launcher Linux process snapshot", snapshot, StringComparison.Ordinal);
    }

    private static UnixProcessRecord Record(
        int processId = 1,
        string comm = "proc",
        string[]? arguments = null,
        (string Key, string Value)[]? environment = null) =>
        new(
            ProcessId: processId,
            ParentProcessId: 0,
            Comm: comm,
            Arguments: arguments ?? [],
            MappedFiles: [],
            Environment: environment?.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal)
                ?? new Dictionary<string, string>(StringComparer.Ordinal));
}
