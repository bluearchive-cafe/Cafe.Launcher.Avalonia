using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Services.GameRuntime;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class UmuGameRunnerTests : IDisposable
{
    private readonly string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    [Fact]
    public void BuildStartInfo_UsesUmuExecutableAndInjectsConfiguredEnvironment()
    {
        var runner = CreateRunner(isSupportedPlatform: true);
        var request = new GameLaunchRequest(
            "blue-archive",
            Path.Combine("games", "BlueArchive.exe"),
            Path.Combine("games"),
            ["-d", "clickCode=test"]);
        var options = new GameRuntimeOptions(
            RunnerPath: "/usr/bin/umu-run",
            PrefixPath: "/home/user/prefixes/ba",
            ProtonPath: "/home/user/.local/share/umu/UMU-Proton");

        var startInfo = runner.BuildStartInfo(options.RunnerPath!, request, options);

        Assert.Equal("/usr/bin/umu-run", startInfo.FileName);
        Assert.Equal(request.WorkingDirectory, startInfo.WorkingDirectory);
        Assert.False(startInfo.UseShellExecute);
        Assert.Equal(
            [request.ExecutablePath, .. request.Arguments],
            startInfo.ArgumentList);
        Assert.Equal("blue-archive", startInfo.Environment["GAMEID"]);
        Assert.Equal("/home/user/prefixes/ba", startInfo.Environment["WINEPREFIX"]);
        Assert.Equal(options.ProtonPath, startInfo.Environment["PROTONPATH"]);
    }

    [Fact]
    public void BuildStartInfo_WhenPrefixNotConfigured_UsesLauncherManagedDefaultPrefix()
    {
        var runner = CreateRunner(isSupportedPlatform: true);
        var request = new GameLaunchRequest(
            "blue-archive",
            "BlueArchive.exe",
            Path.GetTempPath(),
            []);

        var startInfo = runner.BuildStartInfo("umu-run", request, new GameRuntimeOptions());

        var expectedPrefix = GameCompatibilityPaths.GetDefaultPrefixPath("blue-archive", "umu");
        Assert.Equal(expectedPrefix, startInfo.Environment["WINEPREFIX"]);
        Assert.StartsWith(LauncherUserDataDirectory.Root, expectedPrefix);
        Assert.EndsWith(
            Path.Combine("compatibility", "blue-archive", "umu", "prefix"),
            expectedPrefix);
        Assert.False(startInfo.Environment.ContainsKey("PROTONPATH"));
    }

    [Fact]
    public async Task CheckAvailabilityAsync_WhenUmuRunFoundInSearchPath_ReportsAvailable()
    {
        Directory.CreateDirectory(tempDir);
        var umuPath = Path.Combine(tempDir, "umu-run");
        File.WriteAllText(umuPath, "#!/bin/sh\n");
        var runner = CreateRunner(
            isSupportedPlatform: true,
            pathVariable: tempDir);

        var availability = await runner.CheckAvailabilityAsync(new GameRuntimeOptions());

        Assert.Equal(GameRunnerAvailabilityStatus.Available, availability.Status);
        Assert.Equal("1.4.4", availability.Version);
        Assert.Equal(umuPath, availability.ExecutablePath);
    }

    [Fact]
    public async Task CheckAvailabilityAsync_WhenUmuRunMissing_ReportsUnavailableWithMessage()
    {
        var emptyDir = Path.Combine(tempDir, "empty");
        Directory.CreateDirectory(emptyDir);
        var runner = CreateRunner(
            isSupportedPlatform: true,
            pathVariable: emptyDir);

        var availability = await runner.CheckAvailabilityAsync(new GameRuntimeOptions());

        Assert.False(availability.Available);
        Assert.Contains("umu-run", availability.Message);
    }

    [Fact]
    public async Task CheckAvailabilityAsync_WhenConfiguredRunnerPathValid_ReportsAvailableEvenIfMissingFromPath()
    {
        Directory.CreateDirectory(tempDir);
        var umuPath = Path.Combine(tempDir, "umu-run");
        File.WriteAllText(umuPath, "#!/bin/sh\n");
        var emptyDir = Path.Combine(tempDir, "empty");
        Directory.CreateDirectory(emptyDir);
        var runner = CreateRunner(
            isSupportedPlatform: true,
            pathVariable: emptyDir);

        var availability = await runner.CheckAvailabilityAsync(
            new GameRuntimeOptions(RunnerPath: umuPath));

        Assert.True(availability.Available);
        Assert.Equal(umuPath, availability.ExecutablePath);
    }

    [Fact]
    public async Task CheckAvailabilityAsync_WhenConfiguredRunnerPathInvalid_DoesNotFallBackToPath()
    {
        Directory.CreateDirectory(tempDir);
        var umuPath = Path.Combine(tempDir, "umu-run");
        File.WriteAllText(umuPath, "#!/bin/sh\n");
        var missingPath = Path.Combine(tempDir, "missing", "umu-run");
        var runner = CreateRunner(
            isSupportedPlatform: true,
            pathVariable: tempDir);

        var availability = await runner.CheckAvailabilityAsync(
            new GameRuntimeOptions(RunnerPath: missingPath));

        Assert.Equal(GameRunnerAvailabilityStatus.NotFound, availability.Status);
        Assert.Contains(missingPath, availability.Message);
    }

    [Fact]
    public async Task CheckAvailabilityAsync_WhenVersionProbeFails_ReportsBrokenWithExecutableEvidence()
    {
        Directory.CreateDirectory(tempDir);
        var umuPath = Path.Combine(tempDir, "umu-run");
        File.WriteAllText(umuPath, "#!/bin/sh\n");
        var runner = CreateRunner(
            isSupportedPlatform: true,
            pathVariable: tempDir,
            probeVersion: (_, _) => Task.FromResult(new RuntimeProbeResult(
                RuntimeProbeFailureKind.NonZeroExit,
                ExitCode: 7,
                StandardError: "umu probe failed")));

        var availability = await runner.CheckAvailabilityAsync(new GameRuntimeOptions());

        Assert.Equal(GameRunnerAvailabilityStatus.Broken, availability.Status);
        Assert.Equal(umuPath, availability.ExecutablePath);
        Assert.Contains("ExitCode: 7", availability.TechnicalDetail, StringComparison.Ordinal);
        Assert.Contains("umu probe failed", availability.TechnicalDetail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckAvailabilityAsync_OnUnsupportedPlatform_ReportsUnsupportedStatus()
    {
        var runner = CreateRunner(isSupportedPlatform: false);

        var availability = await runner.CheckAvailabilityAsync(new GameRuntimeOptions());

        Assert.Equal(GameRunnerAvailabilityStatus.Unsupported, availability.Status);
    }

    [Fact]
    public async Task StartAsync_WhenExecutableMissing_ThrowsInvalidOperationException()
    {
        var emptyDir = Path.Combine(tempDir, "empty");
        Directory.CreateDirectory(emptyDir);
        var runner = CreateRunner(isSupportedPlatform: true, pathVariable: emptyDir);
        var request = new GameLaunchRequest(
            "blue-archive",
            "BlueArchive.exe",
            Path.GetTempPath(),
            []);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.StartAsync(request, new GameRuntimeOptions()));
    }

    [Fact]
    public async Task StartAsync_OnUnsupportedPlatform_ThrowsInvalidOperationException()
    {
        var runner = CreateRunner(isSupportedPlatform: false);
        var request = new GameLaunchRequest(
            "blue-archive",
            "BlueArchive.exe",
            Path.GetTempPath(),
            []);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.StartAsync(request, new GameRuntimeOptions()));
    }

    [Fact]
    public async Task StartAsync_WhenProcessLauncherReturnsProcess_ReturnsGameProcessWithUmuRunnerId()
    {
        using var host = StartTrivialProcess();
        Directory.CreateDirectory(tempDir);
        var umuPath = Path.Combine(tempDir, "umu-run");
        File.WriteAllText(umuPath, "#!/bin/sh\n");
        var runner = CreateRunner(isSupportedPlatform: true, processLauncher: new StubProcessLauncher(host));
        var request = new GameLaunchRequest(
            "blue-archive",
            "BlueArchive.exe",
            Path.GetTempPath(),
            []);

        var gameProcess = await runner.StartAsync(
            request,
            new GameRuntimeOptions(RunnerPath: umuPath));

        Assert.Equal("umu", gameProcess.RunnerId);
        Assert.Equal(host.Id, gameProcess.ProcessId);
    }

    private UmuGameRunner CreateRunner(
        bool isSupportedPlatform,
        string? pathVariable = null,
        IProcessLauncher? processLauncher = null,
        Func<string, CancellationToken, Task<RuntimeProbeResult>>? probeVersion = null) =>
        new(
            processLauncher ?? new StubProcessLauncher(null),
            () => isSupportedPlatform,
            explicitPath => ExecutableLocator.FindInPath("umu-run", explicitPath, pathVariable),
            probeVersion ?? ((_, _) => Task.FromResult(RuntimeProbeResult.Success(
                "1.4.4",
                0,
                "umu-launcher 1.4.4",
                ""))));

    private static Process StartTrivialProcess()
    {
        var startInfo = OperatingSystem.IsWindows()
            ? new ProcessStartInfo
            {
                FileName = "cmd.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
            }
            : new ProcessStartInfo
            {
                FileName = "/bin/sh",
                UseShellExecute = false,
            };

        startInfo.ArgumentList.Add(OperatingSystem.IsWindows() ? "/c" : "-c");
        startInfo.ArgumentList.Add("exit 0");
        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start trivial test process.");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(tempDir, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private sealed class StubProcessLauncher(Process? result) : IProcessLauncher
    {
        public Process? Start(ProcessStartInfo startInfo) => result;
    }
}
