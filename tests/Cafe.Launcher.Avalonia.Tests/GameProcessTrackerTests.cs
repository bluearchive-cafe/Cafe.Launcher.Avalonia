using System;
using System.Collections.Generic;
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

    [Fact]
    public async Task Register_WhenFakeProcessExits_RecordsExitDetailsAndClearsTracking()
    {
        var fake = new FakeTrackedProcess();
        var tracker = new GameProcessTracker(StubProbe(returnValue: false), _ => fake);

        tracker.Register(new GameProcess(new Process(), "umu"));
        fake.ExitCode = 42;
        fake.RaiseExited();

        Assert.NotNull(tracker.LastExit);
        Assert.Equal(42, tracker.LastExit!.ExitCode);
        Assert.Equal("umu", tracker.LastExit.RunnerId);
        Assert.False(tracker.HasLiveTrackedProcess);
        Assert.False(await tracker.IsGameRunningAsync("BlueArchive.exe"));
        Assert.True(fake.DisposeSucceeded);
    }

    [Fact]
    public async Task Register_WhenFakeProcessAlreadyExited_RecordsImmediatelyWithoutEvent()
    {
        var fake = new FakeTrackedProcess { HasExited = true, ExitCode = 7 };
        var tracker = new GameProcessTracker(StubProbe(returnValue: false), _ => fake);

        tracker.Register(new GameProcess(new Process(), "wine"));

        Assert.NotNull(tracker.LastExit);
        Assert.Equal(7, tracker.LastExit!.ExitCode);
        Assert.Equal("wine", tracker.LastExit.RunnerId);
        Assert.False(tracker.HasLiveTrackedProcess);
    }

    [Fact]
    public async Task Register_WhenNewProcessReplacesLiveOne_OnlyLatestExitsRecordsExitInfo()
    {
        var first = new FakeTrackedProcess();
        var second = new FakeTrackedProcess();
        var processes = new List<ITrackedProcess>();
        var tracker = new GameProcessTracker(
            StubProbe(returnValue: false),
            _ =>
            {
                var current = processes.Count == 0 ? (ITrackedProcess)first : second;
                processes.Add(current);
                return current;
            });

        tracker.Register(new GameProcess(new Process(), "wine"));
        tracker.Register(new GameProcess(new Process(), "umu"));
        Assert.Null(tracker.LastExit);

        second.ExitCode = 9;
        second.RaiseExited();
        Assert.Equal("umu", tracker.LastExit!.RunnerId);
        first.RaiseExited();
        Assert.Equal("umu", tracker.LastExit!.RunnerId);
    }

    private sealed class FakeTrackedProcess : ITrackedProcess
    {
        public bool HasExited { get; set; }
        public int ExitCode { get; set; } = -1;
        public bool DisposeSucceeded { get; private set; }
        public event Action? Exited;

        public void RaiseExited() => Exited?.Invoke();

        public void StartObserving()
        {
        }

        public void Dispose() => DisposeSucceeded = true;
    }
}
