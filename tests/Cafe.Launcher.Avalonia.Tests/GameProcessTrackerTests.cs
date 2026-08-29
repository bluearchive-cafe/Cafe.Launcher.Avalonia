using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Services.GameRuntime;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class GameProcessTrackerTests
{
    [Fact]
    public async Task IsGameRunningAsync_WhenTrackedProcessIsLive_ReturnsTrueBeforeNameScan()
    {
        using var host = StartLongRunningProcess();
        var tracker = new GameProcessTracker(StubProbe(returnValue: false));

        tracker.Register(new GameProcess(host, "native"));

        Assert.True(tracker.HasLiveTrackedProcess);
        Assert.True(await tracker.IsGameRunningAsync("missing-game.exe"));
        Assert.Null(tracker.LastExit);
    }

    [Fact]
    public async Task IsGameRunningAsync_WhenNoTrackedProcess_FallsBackToNameScan()
    {
        var tracker = new GameProcessTracker(StubProbe(returnValue: true));

        Assert.False(tracker.HasLiveTrackedProcess);
        Assert.True(await tracker.IsGameRunningAsync("BlueArchive.exe"));

        var falseTracker = new GameProcessTracker(StubProbe(returnValue: false));
        Assert.False(await falseTracker.IsGameRunningAsync("BlueArchive.exe"));
    }

    [Fact]
    public async Task Register_WhenTrackedProcessExits_RecordsExitInfoAndClearsTracking()
    {
        using var host = StartImmediatelyExitingProcess();
        var tracker = new GameProcessTracker(StubProbe(returnValue: false));

        tracker.Register(new GameProcess(host, "native"));

        await WaitForConditionAsync(() => tracker.LastExit is not null, TimeSpan.FromSeconds(10));

        Assert.Equal(0, tracker.LastExit!.ExitCode);
        Assert.Equal("native", tracker.LastExit.RunnerId);
        Assert.True(tracker.LastExit.Duration >= TimeSpan.Zero);

        await WaitForConditionAsync(() => !tracker.HasLiveTrackedProcess, TimeSpan.FromSeconds(5));
        Assert.False(await tracker.IsGameRunningAsync("BlueArchive.exe"));
    }

    private static Process StartLongRunningProcess()
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
            startInfo.ArgumentList.Add("ping -n 3 127.0.0.1 > nul");
        }
        else
        {
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add("sleep 1");
        }

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start long-running test process.");
    }

    private static Process StartImmediatelyExitingProcess()
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
            ?? throw new InvalidOperationException("Failed to start exiting test process.");
    }

    private static async Task WaitForConditionAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException("Condition was not met within the allotted time.");
    }

    private static Func<string, CancellationToken, Task<bool>> StubProbe(bool returnValue) =>
        (_, _) => Task.FromResult(returnValue);
}
