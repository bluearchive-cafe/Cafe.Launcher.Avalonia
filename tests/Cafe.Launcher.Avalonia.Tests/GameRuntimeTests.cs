using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Services.GameRuntime;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class GameRuntimeTests
{
    private static GameLaunchRequest CreateRequest() => new(
        GameId: GameRuntimeIds.BlueArchiveJapan,
        ExecutablePath: @"C:\Games\BlueArchive_JP\BlueArchive.exe",
        WorkingDirectory: @"C:\Games\BlueArchive_JP",
        Arguments: []);

    private static GameRunnerDefinition Definition(
        string id,
        bool supported = true,
        string? executableName = "umu-run",
        GameRuntimeEnvironmentStyle style = GameRuntimeEnvironmentStyle.Umu,
        string displayName = "UMU") =>
        new(
            id,
            supported,
            "Linux",
            displayName,
            executableName,
            "--version",
            style);

    private static GameRuntime CreateRuntime(
        IReadOnlyList<GameRunnerDefinition> definitions,
        RecordingProcessLauncher launcher,
        Func<string, string?, string?>? locate = null,
        Func<string, string, TimeSpan, CancellationToken, Task<RuntimeProbeResult>>? probe = null,
        IGameProcessTracker? tracker = null,
        List<(string Name, string? Path)>? locateCalls = null)
    {
        return new GameRuntime(
            definitions,
            launcher,
            tracker ?? new RecordingProcessTracker(),
            locate ?? ((name, explicitPath) =>
            {
                locateCalls?.Add((name, explicitPath));
                return explicitPath ?? $"/usr/bin/{name}";
            }),
            probe ?? ((_, _, _, _) =>
                Task.FromResult(RuntimeProbeResult.Success("9.0", 0, "", ""))));
    }

    [Fact]
    public async Task LaunchAsync_WhenAvailabilityCheckThrows_ReturnsFailedResultWithException()
    {
        var launcher = new RecordingProcessLauncher();
        var runtime = CreateRuntime(
            [Definition("umu")],
            launcher,
            probe: (_, _, _, _) => throw new InvalidOperationException("probe boom"));

        var result = await runtime.LaunchAsync(CreateRequest(), new GameRuntimeOptions(), preferredRunnerId: null);

        Assert.False(result.Success);
        Assert.Equal(GameRuntimeLaunchFailure.AvailabilityCheckFailed, result.Failure);
        Assert.IsType<InvalidOperationException>(result.FailureException);
        Assert.Null(result.Process);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public async Task LaunchAsync_AutoMode_StartsFirstAvailableSupportedRunner()
    {
        var launcher = new RecordingProcessLauncher();
        var runtime = CreateRuntime(
            [
                Definition("native", false, null, GameRuntimeEnvironmentStyle.Native, "Native execution"),
                Definition("umu"),
                Definition("wine", true, "wine", GameRuntimeEnvironmentStyle.Wine, "Wine")
            ],
            launcher);

        var result = await runtime.LaunchAsync(CreateRequest(), new GameRuntimeOptions(), preferredRunnerId: null);

        Assert.True(result.Success);
        Assert.Equal("umu", result.RunnerId);
        var startInfo = Assert.Single(launcher.StartInfos);
        Assert.Equal("/usr/bin/umu-run", startInfo.FileName);
        Assert.Equal("blue-archive-jp", startInfo.Environment["GAMEID"]);
    }

    [Fact]
    public async Task LaunchAsync_AutoMode_DoesNotProbeFallbackAfterFirstAvailableRunner()
    {
        var launcher = new RecordingProcessLauncher();
        var runtime = CreateRuntime(
            [Definition("umu"), Definition("wine", true, "wine", GameRuntimeEnvironmentStyle.Wine, "Wine")],
            launcher,
            probe: (executablePath, _, _, _) => executablePath.EndsWith("wine", StringComparison.Ordinal)
                ? throw new InvalidOperationException("Wine probe should not run after UMU is selected.")
                : Task.FromResult(RuntimeProbeResult.Success("9.0", 0, "", "")));

        var result = await runtime.LaunchAsync(CreateRequest(), new GameRuntimeOptions(), preferredRunnerId: null);

        Assert.True(result.Success);
        Assert.Equal("umu", result.RunnerId);
        Assert.Single(launcher.StartInfos);
    }

    [Fact]
    public async Task LaunchAsync_AutoMode_NoAvailableRunner_ReturnsNoRunnerSelectedWithCandidates()
    {
        var launcher = new RecordingProcessLauncher();
        var runtime = CreateRuntime(
            [Definition("umu"), Definition("wine", true, "wine", GameRuntimeEnvironmentStyle.Wine, "Wine")],
            launcher,
            probe: (_, _, _, _) => Task.FromResult(new RuntimeProbeResult(
                RuntimeProbeFailureKind.NonZeroExit,
                ExitCode: 1)));

        var result = await runtime.LaunchAsync(CreateRequest(), new GameRuntimeOptions(), preferredRunnerId: null);

        Assert.False(result.Success);
        Assert.Equal(GameRuntimeLaunchFailure.NoRunnerSelected, result.Failure);
        Assert.Null(result.Process);
        Assert.Equal(2, result.Candidates.Count);
        Assert.All(result.Candidates, candidate => Assert.Equal(GameRunnerAvailabilityStatus.Broken, candidate.Availability.Status));
    }

    [Fact]
    public async Task LaunchAsync_WithPreferredRunner_StartsOnlyThatRunner()
    {
        var launcher = new RecordingProcessLauncher();
        var runtime = CreateRuntime(
            [Definition("umu"), Definition("wine", true, "wine", GameRuntimeEnvironmentStyle.Wine, "Wine")],
            launcher);

        var result = await runtime.LaunchAsync(CreateRequest(), new GameRuntimeOptions(), preferredRunnerId: "wine");

        Assert.True(result.Success);
        Assert.Equal("wine", result.RunnerId);
        Assert.Equal("/usr/bin/wine", Assert.Single(launcher.StartInfos).FileName);
    }

    [Fact]
    public async Task LaunchAsync_WithUnavailablePreferredRunner_ReturnsNoRunnerSelected()
    {
        var launcher = new RecordingProcessLauncher();
        var runtime = CreateRuntime(
            [Definition("wine", true, "wine", GameRuntimeEnvironmentStyle.Wine, "Wine")],
            launcher,
            probe: (_, _, _, _) => Task.FromResult(new RuntimeProbeResult(RuntimeProbeFailureKind.TimedOut)));

        var result = await runtime.LaunchAsync(CreateRequest(), new GameRuntimeOptions(), preferredRunnerId: "wine");

        Assert.False(result.Success);
        Assert.Equal(GameRuntimeLaunchFailure.NoRunnerSelected, result.Failure);
        Assert.Single(result.Candidates);
        Assert.Equal(GameRunnerAvailabilityStatus.Broken, result.Candidates[0].Availability.Status);
        Assert.Empty(launcher.StartInfos);
    }

    [Fact]
    public async Task LaunchAsync_WithUnknownPreferredRunner_ReturnsEmptyCandidates()
    {
        var launcher = new RecordingProcessLauncher();
        var runtime = CreateRuntime([Definition("umu")], launcher);

        var result = await runtime.LaunchAsync(CreateRequest(), new GameRuntimeOptions(), preferredRunnerId: "crossover");

        Assert.False(result.Success);
        Assert.Equal(GameRuntimeLaunchFailure.NoRunnerSelected, result.Failure);
        Assert.Empty(result.Candidates);
        Assert.Empty(launcher.StartInfos);
    }

    [Fact]
    public async Task LaunchAsync_AutoMode_IgnoresSharedCustomRunnerPathForEveryRunner()
    {
        var launcher = new RecordingProcessLauncher();
        var locateCalls = new List<(string, string?)>();
        var runtime = CreateRuntime(
            [Definition("umu"), Definition("wine", true, "wine", GameRuntimeEnvironmentStyle.Wine, "Wine")],
            launcher,
            locateCalls: locateCalls);
        var options = new GameRuntimeOptions(RunnerPath: "/opt/wine/bin/wine");

        await runtime.LaunchAsync(CreateRequest(), options, preferredRunnerId: null);

        // Auto mode clears the shared custom path for the selected runner's
        // availability check and start; fallback runners are not probed after
        // the first usable runner is selected.
        Assert.Equal(2, locateCalls.Count);
        Assert.All(locateCalls, call => Assert.Null(call.Item2));
    }

    [Fact]
    public async Task LaunchAsync_WithPreferredRunner_AppliesCustomPathOnlyToThatRunner()
    {
        var launcher = new RecordingProcessLauncher();
        var locateCalls = new List<(string, string?)>();
        var runtime = CreateRuntime(
            [Definition("umu"), Definition("wine", true, "wine", GameRuntimeEnvironmentStyle.Wine, "Wine")],
            launcher,
            locateCalls: locateCalls);
        var options = new GameRuntimeOptions(RunnerPath: "/opt/umu/bin/umu-run");

        await runtime.LaunchAsync(CreateRequest(), options, preferredRunnerId: "umu");

        Assert.True(launcher.StartInfos.Count == 1);
        Assert.Equal("/opt/umu/bin/umu-run", Assert.Single(launcher.StartInfos).FileName);
        // Availability check and start both use the pinned runner's custom path.
        Assert.Equal(2, locateCalls.Count);
        Assert.All(locateCalls, call =>
        {
            Assert.Equal("umu-run", call.Item1);
            Assert.Equal("/opt/umu/bin/umu-run", call.Item2);
        });
    }

    [Fact]
    public async Task GetStatusesAsync_ReturnsEntriesInRegistrationOrder()
    {
        var launcher = new RecordingProcessLauncher();
        var runtime = CreateRuntime(
            [Definition("umu"), Definition("wine", true, "wine", GameRuntimeEnvironmentStyle.Wine, "Wine")],
            launcher);

        var entries = await runtime.GetStatusesAsync(preferredRunnerId: null, new GameRuntimeOptions());

        Assert.Collection(
            entries,
            entry => Assert.Equal("umu", entry.RunnerId),
            entry => Assert.Equal("wine", entry.RunnerId));
        Assert.All(entries, entry => Assert.True(entry.Availability.Available));
    }

    [Fact]
    public async Task GetStatusesAsync_WithPreferredRunner_ChecksEveryRunnerAndScopesCustomPath()
    {
        var launcher = new RecordingProcessLauncher();
        var locateCalls = new List<(string, string?)>();
        var runtime = CreateRuntime(
            [Definition("umu"), Definition("wine", true, "wine", GameRuntimeEnvironmentStyle.Wine, "Wine")],
            launcher,
            locateCalls: locateCalls);

        var entries = await runtime.GetStatusesAsync(
            "wine",
            new GameRuntimeOptions(RunnerPath: "/opt/wine/bin/wine"));

        Assert.Collection(
            entries,
            entry => Assert.Equal("umu", entry.RunnerId),
            entry => Assert.Equal("wine", entry.RunnerId));
        Assert.Collection(
            locateCalls,
            call =>
            {
                Assert.Equal("umu-run", call.Item1);
                Assert.Null(call.Item2);
            },
            call =>
            {
                Assert.Equal("wine", call.Item1);
                Assert.Equal("/opt/wine/bin/wine", call.Item2);
            });
    }

    [Fact]
    public async Task GetStatusesAsync_WhenExecutableMissing_ReportsNotFoundWithConfiguredPath()
    {
        var launcher = new RecordingProcessLauncher();
        var runtime = CreateRuntime(
            [Definition("wine", true, "wine", GameRuntimeEnvironmentStyle.Wine, "Wine")],
            launcher,
            locate: (_, _) => null);

        var entries = await runtime.GetStatusesAsync(preferredRunnerId: null, new GameRuntimeOptions());

        var entry = Assert.Single(entries);
        Assert.Equal(GameRunnerAvailabilityStatus.NotFound, entry.Availability.Status);
        Assert.Contains("wine was not found on PATH.", entry.Availability.Message);
    }

    [Fact]
    public async Task LaunchAsync_WineStyle_AppliesEffectivePrefix()
    {
        var launcher = new RecordingProcessLauncher();
        var runtime = CreateRuntime([Definition("wine", true, "wine", GameRuntimeEnvironmentStyle.Wine, "Wine")], launcher);
        var request = CreateRequest();

        await runtime.LaunchAsync(request, new GameRuntimeOptions(), preferredRunnerId: null);

        var startInfo = Assert.Single(launcher.StartInfos);
        Assert.Equal(
            GameCompatibilityPaths.GetDefaultPrefixPath(request.GameId, "wine"),
            startInfo.Environment["WINEPREFIX"]);
    }

    [Fact]
    public async Task LaunchAsync_WineStyle_ConfiguredPrefixWins()
    {
        var launcher = new RecordingProcessLauncher();
        var runtime = CreateRuntime([Definition("wine", true, "wine", GameRuntimeEnvironmentStyle.Wine, "Wine")], launcher);

        await runtime.LaunchAsync(
            CreateRequest(),
            new GameRuntimeOptions(PrefixPath: "/home/user/prefix"),
            preferredRunnerId: null);

        Assert.Equal(
            "/home/user/prefix",
            Assert.Single(launcher.StartInfos).Environment["WINEPREFIX"]);
    }

    [Fact]
    public async Task LaunchAsync_UmuStyle_SetsGameIdAndOptionalProtonPath()
    {
        var launcher = new RecordingProcessLauncher();
        var runtime = CreateRuntime([Definition("umu")], launcher);

        await runtime.LaunchAsync(
            CreateRequest(),
            new GameRuntimeOptions(ProtonPath: "/usr/lib/proton"),
            preferredRunnerId: null);

        var startInfo = Assert.Single(launcher.StartInfos);
        Assert.Equal("blue-archive-jp", startInfo.Environment["GAMEID"]);
        Assert.Equal("/usr/lib/proton", startInfo.Environment["PROTONPATH"]);
    }

    [Fact]
    public async Task LaunchAsync_Native_StartsGameExecutableDirectly()
    {
        var launcher = new RecordingProcessLauncher();
        var runtime = CreateRuntime(
            [Definition("native", supported: true, executableName: null, GameRuntimeEnvironmentStyle.Native, "Native execution")],
            launcher);

        await runtime.LaunchAsync(CreateRequest(), new GameRuntimeOptions(), preferredRunnerId: null);

        var startInfo = Assert.Single(launcher.StartInfos);
        Assert.Equal(@"C:\Games\BlueArchive_JP\BlueArchive.exe", startInfo.FileName);
        Assert.False(startInfo.Environment.ContainsKey("WINEPREFIX"));
        Assert.Empty(startInfo.ArgumentList);
    }

    [Fact]
    public async Task LaunchAsync_WhenStartFails_ReturnsStartFailedWithDiagnostic()
    {
        var launcher = new RecordingProcessLauncher { FailStart = true };
        var runtime = CreateRuntime([Definition("umu")], launcher);

        var result = await runtime.LaunchAsync(CreateRequest(), new GameRuntimeOptions(), preferredRunnerId: null);

        Assert.False(result.Success);
        Assert.Equal(GameRuntimeLaunchFailure.StartFailed, result.Failure);
        Assert.NotNull(result.FailureException);
        Assert.Equal("umu", result.Diagnostic.RunnerId);
        Assert.Equal("auto", result.Diagnostic.ProtonPath);
        Assert.Equal(
            GameCompatibilityPaths.GetDefaultPrefixPath(GameRuntimeIds.BlueArchiveJapan, "umu"),
            result.Diagnostic.PrefixPath);
    }

    [Fact]
    public async Task LaunchAsync_AfterSuccessfulStart_RegistersProcessWithTracker()
    {
        var launcher = new RecordingProcessLauncher();
        var tracker = new RecordingProcessTracker();
        var runtime = CreateRuntime([Definition("umu")], launcher, tracker: tracker);

        var result = await runtime.LaunchAsync(CreateRequest(), new GameRuntimeOptions(), preferredRunnerId: null);

        Assert.True(result.Success);
        Assert.Equal(1, tracker.RegisterCount);
        Assert.Equal("umu", result.Process!.RunnerId);
        Assert.Equal("umu", tracker.LastRegisteredRunnerId);
    }

    private sealed class RecordingProcessLauncher : IProcessLauncher
    {
        public List<ProcessStartInfo> StartInfos { get; } = [];
        public bool FailStart { get; set; }

        public Process? Start(ProcessStartInfo startInfo)
        {
            StartInfos.Add(startInfo);
            return FailStart ? null : new Process { StartInfo = startInfo };
        }
    }

    private sealed class RecordingProcessTracker : IGameProcessTracker
    {
        public int RegisterCount { get; private set; }
        public string? LastRegisteredRunnerId { get; private set; }

        public void Register(GameProcess process)
        {
            RegisterCount++;
            LastRegisteredRunnerId = process.RunnerId;
        }

        public bool HasLiveTrackedProcess => false;
        public GameLaunchExitInfo? LastExit => null;

        public Task<bool> IsGameRunningAsync(
            string exeName,
            CancellationToken cancellationToken = default) => Task.FromResult(false);
    }
}
