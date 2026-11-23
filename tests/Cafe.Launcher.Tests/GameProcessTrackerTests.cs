using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.UI.Services.GameRuntime;
using Cafe.Launcher.Core.Services.GameRuntime;

namespace Cafe.Launcher.Tests;

public sealed class GameProcessTrackerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FindRunningGameProcessesAsync_WhenEnumerationFails_PreservesGatePolicyAndLiveHost(bool liveHost)
    {
        var fake = new FakeTrackedProcess();
        var tracker = new GameProcessTracker((_, _) => throw new System.ComponentModel.Win32Exception(5), _ => fake);
        using var process = new Process();
        if (liveHost)
        {
            tracker.Register(new GameProcess(process, "native"));
        }

        var running = await tracker.FindRunningGameProcessesAsync(Query);

        Assert.Equal(liveHost ? [Query.KnownExeNames[0]] : Array.Empty<string>(), running);
        fake.RaiseExited();
    }

    [Fact]
    public async Task FindRunningGameProcessesAsync_WhenScanIsCanceled_PropagatesCancellation()
    {
        var tracker = new GameProcessTracker((_, _) => throw new OperationCanceledException());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tracker.FindRunningGameProcessesAsync(Query));
    }

    [Fact]
    public async Task FindRunningGameProcessesAsync_WhenProbeHasAProgrammingError_PropagatesIt()
    {
        var tracker = new GameProcessTracker((_, _) => throw new ArgumentException("invalid probe state"));

        await Assert.ThrowsAsync<ArgumentException>(() => tracker.FindRunningGameProcessesAsync(Query));
    }

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

    [Fact]
    public async Task Dispose_WhenTrackingLiveGame_ReleasesObservationOnceWithoutReportingExit()
    {
        var fake = new FakeTrackedProcess();
        var tracker = new GameProcessTracker(StubProbe(), _ => fake);
        using var process = new Process();
        var exitEvents = 0;
        tracker.TrackedProcessExited += () => exitEvents++;
        tracker.Register(new GameProcess(process, "wine"));
        var lateExit = fake.CaptureExitCallback();

        tracker.Dispose();
        tracker.Dispose();
        lateExit();

        Assert.Equal(1, fake.DisposeCount);
        Assert.Equal(0, fake.SubscriberCount);
        Assert.Equal(0, fake.ExitCodeReadCount);
        Assert.Equal(0, exitEvents);
        Assert.False(fake.HasExited);
        Assert.False(tracker.HasLiveTrackedProcess);
        Assert.Null(tracker.LastExit);
        Assert.Empty(await tracker.FindRunningGameProcessesAsync(Query));
    }

    [Fact]
    public async Task Dispose_WhenGameIsStillRunning_KeepsNameQueriesAvailable()
    {
        using var tracker = new GameProcessTracker(StubProbe("BlueArchive"));

        tracker.Dispose();

        Assert.Equal(["BlueArchive"], await tracker.FindRunningGameProcessesAsync(Query));
        Assert.False(tracker.HasLiveTrackedProcess);
    }

    [Fact]
    public void Dispose_AfterCapturedExit_PreservesLastExitAndIgnoresQueuedCallback()
    {
        var fake = new FakeTrackedProcess { ExitCode = 42 };
        using var tracker = new GameProcessTracker(StubProbe(), _ => fake);
        using var process = new Process();
        var exitEvents = 0;
        tracker.TrackedProcessExited += () => exitEvents++;
        tracker.Register(new GameProcess(process, "umu"));
        var lateExit = fake.CaptureExitCallback();
        fake.RaiseExited();
        var lastExit = tracker.LastExit;

        tracker.Dispose();
        lateExit();

        Assert.NotNull(lastExit);
        Assert.Same(lastExit, tracker.LastExit);
        Assert.Equal(42, tracker.LastExit!.ExitCode);
        Assert.Equal(1, fake.DisposeCount);
        Assert.Equal(1, fake.ExitCodeReadCount);
        Assert.Equal(0, fake.SubscriberCount);
        Assert.Equal(1, exitEvents);
    }

    [Fact]
    public void Register_AfterDispose_RejectsBeforeAdaptingTheProcess()
    {
        var adapterCalls = 0;
        using var tracker = new GameProcessTracker(StubProbe(), _ =>
        {
            adapterCalls++;
            return new FakeTrackedProcess();
        });
        using var process = new Process();
        tracker.Dispose();

        Assert.Throws<ObjectDisposedException>(() => tracker.Register(new GameProcess(process, "native")));

        Assert.Equal(0, adapterCalls);
        Assert.False(tracker.HasLiveTrackedProcess);
        Assert.Null(tracker.LastExit);
    }

    [Fact]
    public async Task Register_WhenDisposeWinsDuringAdaptation_ReleasesUnadoptedWrapper()
    {
        var fake = new FakeTrackedProcess();
        var adapterEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseAdapter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var tracker = new GameProcessTracker(StubProbe(), _ =>
        {
            adapterEntered.SetResult();
            // Adapter is synchronous; block only this background test worker to fix the race order.
            releaseAdapter.Task.GetAwaiter().GetResult();
            return fake;
        });
        using var process = new Process();
        var registration = Task.Run(() => tracker.Register(new GameProcess(process, "wine")));
        await adapterEntered.Task;
        try
        {
            tracker.Dispose();
        }
        finally
        {
            releaseAdapter.SetResult();
        }

        await Assert.ThrowsAsync<ObjectDisposedException>(() => registration);

        Assert.Equal(1, fake.DisposeCount);
        Assert.Equal(0, fake.SubscriberCount);
        Assert.Equal(0, fake.StartObservingCount);
        Assert.False(tracker.HasLiveTrackedProcess);
        Assert.Null(tracker.LastExit);
    }

    [Fact]
    public async Task Dispose_WhenExitCallbackRaces_ReleasesWrapperOnlyOnceAndRejectsLateCallbacks()
    {
        var fake = new FakeTrackedProcess { ExitCode = 7 };
        using var tracker = new GameProcessTracker(StubProbe(), _ => fake);
        using var process = new Process();
        var exitEvents = 0;
        tracker.TrackedProcessExited += () => Interlocked.Increment(ref exitEvents);
        tracker.Register(new GameProcess(process, "native"));
        var exit = fake.CaptureExitCallback();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exitTask = Task.Run(async () =>
        {
            await start.Task;
            exit();
        });
        var disposeTask = Task.Run(async () =>
        {
            await start.Task;
            tracker.Dispose();
        });
        start.SetResult();
        await Task.WhenAll(exitTask, disposeTask);
        var confirmedExitEvents = exitEvents;
        var exitCodeReads = fake.ExitCodeReadCount;

        exit();
        tracker.Dispose();

        Assert.InRange(confirmedExitEvents, 0, 1);
        Assert.Equal(confirmedExitEvents, exitEvents);
        Assert.Equal(exitCodeReads, fake.ExitCodeReadCount);
        Assert.Equal(1, fake.DisposeCount);
        Assert.Equal(0, fake.SubscriberCount);
        Assert.False(tracker.HasLiveTrackedProcess);
        Assert.Equal(confirmedExitEvents == 1, tracker.LastExit is not null);
    }

    [Fact]
    public void Register_WhenObservationReportsExitSynchronously_CapturesAndPublishesItOnce()
    {
        var fake = new FakeTrackedProcess { ExitCode = 3 };
        fake.OnStartObserving = fake.RaiseExited;
        using var tracker = new GameProcessTracker(StubProbe(), _ => fake);
        using var process = new Process();
        var exitEvents = 0;
        tracker.TrackedProcessExited += () => exitEvents++;

        tracker.Register(new GameProcess(process, "native"));

        Assert.Equal(1, exitEvents);
        Assert.Equal(1, fake.ExitCodeReadCount);
        Assert.Equal(1, fake.DisposeCount);
        Assert.Equal(3, tracker.LastExit!.ExitCode);
        Assert.False(tracker.HasLiveTrackedProcess);
    }

    private sealed class FakeTrackedProcess : ITrackedProcess
    {
        private Action? exited;
        private int exitCode = -1;
        public bool HasExited { get; set; }
        public int ExitCode
        {
            get
            {
                ObjectDisposedException.ThrowIf(DisposeCount > 0, this);
                ExitCodeReadCount++;
                return exitCode;
            }
            set => exitCode = value;
        }
        public bool DisposeSucceeded => DisposeCount > 0;
        public int DisposeCount { get; private set; }
        public int ExitCodeReadCount { get; private set; }
        public int StartObservingCount { get; private set; }
        public int SubscriberCount => exited?.GetInvocationList().Length ?? 0;
        public Action? OnStartObserving { get; set; }
        public event Action? Exited
        {
            add => exited += value;
            remove => exited -= value;
        }

        public void RaiseExited() => exited?.Invoke();

        public Action CaptureExitCallback() => exited ?? throw new InvalidOperationException("No exit observer.");

        public void StartObserving()
        {
            StartObservingCount++;
            OnStartObserving?.Invoke();
        }

        public void Dispose() => DisposeCount++;
    }
}
