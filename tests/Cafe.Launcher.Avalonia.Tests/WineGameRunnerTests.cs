using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.GameRuntime;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class WineGameRunnerTests : IDisposable
{
    private readonly string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    [Fact]
    public void BuildStartInfo_UsesWineExecutableAndInjectsPrefixEnvironment()
    {
        var runner = CreateRunner(isSupportedPlatform: true);
        var request = new GameLaunchRequest(
            "blue-archive",
            Path.Combine("games", "BlueArchive.exe"),
            Path.Combine("games"),
            ["-d", "clickCode=test"]);
        var options = new GameRuntimeOptions(
            RunnerPath: "/usr/bin/wine",
            PrefixPath: "/home/user/prefixes/ba");

        var startInfo = runner.BuildStartInfo(options.RunnerPath!, request, options);

        Assert.Equal("/usr/bin/wine", startInfo.FileName);
        Assert.Equal(request.WorkingDirectory, startInfo.WorkingDirectory);
        Assert.False(startInfo.UseShellExecute);
        Assert.Equal(
            [request.ExecutablePath, .. request.Arguments],
            startInfo.ArgumentList);
        Assert.Equal("/home/user/prefixes/ba", startInfo.Environment["WINEPREFIX"]);
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

        var startInfo = runner.BuildStartInfo("wine", request, new GameRuntimeOptions());

        Assert.Equal(
            GameCompatibilityPaths.GetDefaultPrefixPath("blue-archive", "wine"),
            startInfo.Environment["WINEPREFIX"]);
    }

    [Fact]
    public async Task CheckAvailabilityAsync_WhenWineFoundInSearchPath_ReportsAvailable()
    {
        Directory.CreateDirectory(tempDir);
        var winePath = Path.Combine(tempDir, "wine");
        File.WriteAllText(winePath, "#!/bin/sh\n");
        var runner = CreateRunner(isSupportedPlatform: true, pathVariable: tempDir);

        var availability = await runner.CheckAvailabilityAsync(new GameRuntimeOptions());

        Assert.Equal(GameRunnerAvailabilityStatus.Available, availability.Status);
        Assert.Equal("9.0", availability.Version);
        Assert.Equal(winePath, availability.ExecutablePath);
    }

    [Fact]
    public async Task CheckAvailabilityAsync_WhenWineMissing_ReportsUnavailableWithMessage()
    {
        var emptyDir = Path.Combine(tempDir, "empty");
        Directory.CreateDirectory(emptyDir);
        var runner = CreateRunner(isSupportedPlatform: true, pathVariable: emptyDir);

        var availability = await runner.CheckAvailabilityAsync(new GameRuntimeOptions());

        Assert.False(availability.Available);
        Assert.Contains("wine", availability.Message);
    }

    [Fact]
    public async Task CheckAvailabilityAsync_WhenConfiguredRunnerPathValid_ReportsAvailableEvenIfMissingFromPath()
    {
        Directory.CreateDirectory(tempDir);
        var winePath = Path.Combine(tempDir, "wine");
        File.WriteAllText(winePath, "#!/bin/sh\n");
        var emptyDir = Path.Combine(tempDir, "empty");
        Directory.CreateDirectory(emptyDir);
        var runner = CreateRunner(isSupportedPlatform: true, pathVariable: emptyDir);

        var availability = await runner.CheckAvailabilityAsync(
            new GameRuntimeOptions(RunnerPath: winePath));

        Assert.True(availability.Available);
        Assert.Equal(winePath, availability.ExecutablePath);
    }

    [Fact]
    public async Task CheckAvailabilityAsync_WhenConfiguredRunnerPathInvalid_DoesNotFallBackToPath()
    {
        Directory.CreateDirectory(tempDir);
        var winePath = Path.Combine(tempDir, "wine");
        File.WriteAllText(winePath, "#!/bin/sh\n");
        var missingPath = Path.Combine(tempDir, "missing", "wine");
        var runner = CreateRunner(isSupportedPlatform: true, pathVariable: tempDir);

        var availability = await runner.CheckAvailabilityAsync(
            new GameRuntimeOptions(RunnerPath: missingPath));

        Assert.Equal(GameRunnerAvailabilityStatus.NotFound, availability.Status);
        Assert.Contains(missingPath, availability.Message);
    }

    [Fact]
    public async Task CheckAvailabilityAsync_WhenVersionProbeFails_ReportsBrokenWithExecutableEvidence()
    {
        Directory.CreateDirectory(tempDir);
        var winePath = Path.Combine(tempDir, "wine");
        File.WriteAllText(winePath, "#!/bin/sh\n");
        var runner = CreateRunner(
            isSupportedPlatform: true,
            pathVariable: tempDir,
            probeVersion: (_, _) => Task.FromResult<string?>(null));

        var availability = await runner.CheckAvailabilityAsync(new GameRuntimeOptions());

        Assert.Equal(GameRunnerAvailabilityStatus.Broken, availability.Status);
        Assert.Equal(winePath, availability.ExecutablePath);
        Assert.NotNull(availability.TechnicalDetail);
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
    public async Task StartAsync_WhenProcessLauncherReturnsProcess_ReturnsGameProcessWithWineRunnerId()
    {
        using var host = StartTrivialProcess();
        Directory.CreateDirectory(tempDir);
        var winePath = Path.Combine(tempDir, "wine");
        File.WriteAllText(winePath, "#!/bin/sh\n");
        var runner = CreateRunner(isSupportedPlatform: true, processLauncher: new StubProcessLauncher(host));
        var request = new GameLaunchRequest(
            "blue-archive",
            "BlueArchive.exe",
            Path.GetTempPath(),
            []);

        var gameProcess = await runner.StartAsync(
            request,
            new GameRuntimeOptions(RunnerPath: winePath));

        Assert.Equal("wine", gameProcess.RunnerId);
        Assert.Equal(host.Id, gameProcess.ProcessId);
    }

    private WineGameRunner CreateRunner(
        bool isSupportedPlatform,
        string? pathVariable = null,
        IProcessLauncher? processLauncher = null,
        Func<string, CancellationToken, Task<string?>>? probeVersion = null) =>
        new(
            processLauncher ?? new StubProcessLauncher(null),
            () => isSupportedPlatform,
            explicitPath => ExecutableLocator.FindInPath("wine", explicitPath, pathVariable),
            probeVersion ?? ((_, _) => Task.FromResult<string?>("9.0")));

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
