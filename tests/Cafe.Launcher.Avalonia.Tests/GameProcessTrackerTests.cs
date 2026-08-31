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
        var fake = new FakeTrackedProcess { HasExited = false };
        var tracker = new GameProcessTracker(StubProbe(returnValue: false), _ => fake);

        tracker.Register(new GameProcess(new Process(), "native"));

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
        var fake = new FakeTrackedProcess { ExitCode = 0 };
        var tracker = new GameProcessTracker(StubProbe(returnValue: false), _ => fake);

        tracker.Register(new GameProcess(new Process(), "native"));
        fake.RaiseExited();

        Assert.NotNull(tracker.LastExit);
        Assert.Equal(0, tracker.LastExit!.ExitCode);
        Assert.Equal("native", tracker.LastExit.RunnerId);
        Assert.True(tracker.LastExit.Duration >= TimeSpan.Zero);

        Assert.False(tracker.HasLiveTrackedProcess);
        Assert.False(await tracker.IsGameRunningAsync("BlueArchive.exe"));
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
