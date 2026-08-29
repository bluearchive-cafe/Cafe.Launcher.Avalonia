using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Services.GameRuntime;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class NativeGameRunnerTests
{
    [Fact]
    public void BuildStartInfo_UsesRequestFileNameWorkingDirectoryAndArguments()
    {
        var request = new GameLaunchRequest(
            "blue-archive",
            Path.Combine("games", "BlueArchive.exe"),
            Path.Combine("games"),
            ["-d", "clickCode=test"]);

        var runner = new NativeGameRunner(new DefaultProcessLauncher());

        var startInfo = runner.BuildStartInfo(request);

        Assert.Equal(request.ExecutablePath, startInfo.FileName);
        Assert.Equal(request.WorkingDirectory, startInfo.WorkingDirectory);
        Assert.False(startInfo.UseShellExecute);
        Assert.Equal(request.Arguments, startInfo.ArgumentList);
    }

    [Fact]
    public async Task CheckAvailabilityAsync_ReportsWindowsPlatformAvailability()
    {
        var runner = new NativeGameRunner(new DefaultProcessLauncher());

        var availability = await runner.CheckAvailabilityAsync();

        Assert.Equal(OperatingSystem.IsWindows(), availability.Available);
    }

    [Fact]
    public async Task StartAsync_WhenProcessLauncherReturnsProcess_ReturnsGameProcessWithRunnerId()
    {
        using var gameProcessHost = StartTrivialProcess();
        var launcher = new StubProcessLauncher(gameProcessHost);
        var runner = new NativeGameRunner(launcher);
        var request = new GameLaunchRequest(
            "blue-archive",
            "BlueArchive.exe",
            Path.GetTempPath(),
            []);

        var gameProcess = await runner.StartAsync(request, new GameRuntimeOptions());

        Assert.Equal("native", gameProcess.RunnerId);
        Assert.Equal(gameProcessHost.Id, gameProcess.ProcessId);
        Assert.Same(gameProcessHost, gameProcess.HostProcess);
        Assert.Equal(request.ExecutablePath, launcher.LastStartInfo!.FileName);
    }

    [Fact]
    public async Task StartAsync_WhenProcessLauncherReturnsNull_ThrowsInvalidOperationException()
    {
        var runner = new NativeGameRunner(new StubProcessLauncher(null));
        var request = new GameLaunchRequest(
            "blue-archive",
            "BlueArchive.exe",
            Path.GetTempPath(),
            []);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.StartAsync(request, new GameRuntimeOptions()));
    }

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

        if (OperatingSystem.IsWindows())
        {
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add("exit 0");
        }
        else
        {
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add("exit 0");
        }

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start trivial test process.");
    }

    private sealed class StubProcessLauncher(Process? result) : IProcessLauncher
    {
        public ProcessStartInfo? LastStartInfo { get; private set; }

        public Process? Start(ProcessStartInfo startInfo)
        {
            LastStartInfo = startInfo;
            return result;
        }
    }
}
