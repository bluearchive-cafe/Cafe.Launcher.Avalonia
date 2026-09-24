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
    public async Task FindRunningGameProcessesAsync_WhenTrackedProcessIsLive_ReportsItWithoutScanning()
    {
        var fake = new FakeTrackedProcess { HasExited = false };
        var tracker = new GameProcessTracker(StubProbe(), _ => fake);

        tracker.Register(new GameProcess(new Process(), "native"));

        Assert.True(tracker.HasLiveTrackedProcess);
        Assert.NotEmpty(await tracker.FindRunningGameProcessesAsync(Query));
        Assert.Null(tracker.LastExit);
    }

    [Fact]
    public async Task FindRunningGameProcessesAsync_WhenNoTrackedProcess_FallsBackToNameScan()
    {
        var tracker = new GameProcessTracker(StubProbe("BlueArchive"));

        Assert.False(tracker.HasLiveTrackedProcess);
        Assert.NotEmpty(await tracker.FindRunningGameProcessesAsync(Query));

        var falseTracker = new GameProcessTracker(StubProbe());
        Assert.Empty(await falseTracker.FindRunningGameProcessesAsync(Query));
    }

    [Fact]
    public async Task Register_WhenTrackedProcessExits_RecordsExitInfoAndClearsTracking()
    {
        var fake = new FakeTrackedProcess { ExitCode = 0 };
        var tracker = new GameProcessTracker(StubProbe(), _ => fake);

        tracker.Register(new GameProcess(new Process(), "native"));
        fake.RaiseExited();

        Assert.NotNull(tracker.LastExit);
        Assert.Equal(0, tracker.LastExit!.ExitCode);
        Assert.Equal("native", tracker.LastExit.RunnerId);
        Assert.True(tracker.LastExit.Duration >= TimeSpan.Zero);

        Assert.False(tracker.HasLiveTrackedProcess);
        Assert.Empty(await tracker.FindRunningGameProcessesAsync(Query));
    }

    [Fact]
    public async Task Register_WhenTrackedProcessExits_RaisesTrackedProcessExitedOnce()
    {
        var fake = new FakeTrackedProcess { ExitCode = 3 };
        var tracker = new GameProcessTracker(StubProbe(), _ => fake);
        var exitEvents = 0;
        tracker.TrackedProcessExited += () => exitEvents++;

        tracker.Register(new GameProcess(new Process(), "native"));
        fake.RaiseExited();

        Assert.Equal(1, exitEvents);
        Assert.Equal(3, tracker.LastExit!.ExitCode);
    }

    [Fact]
    public async Task Register_WhenProcessExitedBeforeSubscribing_TheExitIsCarriedByLastExit()
    {
        // Register→订阅之间发生的退出不会作为事件补发：订阅方以 LastExit 非空即当前
        // 会话宿主已亡的约定收口（会话看护的 BeginSession 正是这样接住它的）。
        var fake = new FakeTrackedProcess { HasExited = true, ExitCode = 5 };
        var tracker = new GameProcessTracker(StubProbe(), _ => fake);

        tracker.Register(new GameProcess(new Process(), "wine"));

        var exitEvents = 0;
        tracker.TrackedProcessExited += () => exitEvents++;

        Assert.Equal(0, exitEvents);
        Assert.NotNull(tracker.LastExit);
        Assert.Equal(5, tracker.LastExit!.ExitCode);
    }

    [Fact]
    public async Task Register_WhenNewSessionBegins_LastExitBelongsToTheNewProcessOnly()
    {
        var first = new FakeTrackedProcess { ExitCode = 1 };
        var second = new FakeTrackedProcess();
        var processes = new List<ITrackedProcess>();
        var tracker = new GameProcessTracker(
            StubProbe(),
            _ =>
            {
                var current = processes.Count == 0 ? (ITrackedProcess)first : second;
                processes.Add(current);
                return current;
            });

        tracker.Register(new GameProcess(new Process(), "wine"));
        first.RaiseExited();
        Assert.NotNull(tracker.LastExit);

        tracker.Register(new GameProcess(new Process(), "umu"));

        // 重开即清零：LastExit 归属当前被跟踪的进程，上一局的旧账不再冒充本局事实。
        Assert.Null(tracker.LastExit);
    }

    private static readonly IReadOnlyList<string> KnownNames =
        GameProcessNames.FromLaunchConfiguration("xldr_BlueArchiveOnline_JP_loader_x64", ["BlueArchive.exe"]);

    private static readonly RunningGameQuery Query = new(KnownNames);

    /// <summary>名字扫描的替身：返回给定的命中列表，空表示没在跑。</summary>
    private static Func<RunningGameQuery, CancellationToken, Task<IReadOnlyList<string>>> StubProbe(
        params string[] matches) =>
        (_, _) => Task.FromResult<IReadOnlyList<string>>(matches);

    [Fact]
    public async Task Register_WhenFakeProcessExits_RecordsExitDetailsAndClearsTracking()
    {
        var fake = new FakeTrackedProcess();
        var tracker = new GameProcessTracker(StubProbe(), _ => fake);

        tracker.Register(new GameProcess(new Process(), "umu"));
        fake.ExitCode = 42;
        fake.RaiseExited();

        Assert.NotNull(tracker.LastExit);
        Assert.Equal(42, tracker.LastExit!.ExitCode);
        Assert.Equal("umu", tracker.LastExit.RunnerId);
        Assert.False(tracker.HasLiveTrackedProcess);
        Assert.Empty(await tracker.FindRunningGameProcessesAsync(Query));
        Assert.True(fake.DisposeSucceeded);
    }

    [Fact]
    public async Task Register_WhenFakeProcessAlreadyExited_RecordsImmediatelyWithoutEvent()
    {
        var fake = new FakeTrackedProcess { HasExited = true, ExitCode = 7 };
        var tracker = new GameProcessTracker(StubProbe(), _ => fake);

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
            StubProbe(),
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

    [Fact]
    public void Register_WhenReplacedProcessExits_KeepsLatestProcessTracked()
    {
        var first = new FakeTrackedProcess { ExitCode = 7 };
        var second = new FakeTrackedProcess();
        var processes = new List<ITrackedProcess> { first, second };
        var tracker = new GameProcessTracker(
            StubProbe(),
            _ => processes[0]);

        tracker.Register(new GameProcess(new Process(), "wine"));
        processes.RemoveAt(0);
        tracker.Register(new GameProcess(new Process(), "umu"));
        first.RaiseExited();

        Assert.True(first.DisposeSucceeded);
        Assert.True(tracker.HasLiveTrackedProcess);
        Assert.Null(tracker.LastExit);
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
