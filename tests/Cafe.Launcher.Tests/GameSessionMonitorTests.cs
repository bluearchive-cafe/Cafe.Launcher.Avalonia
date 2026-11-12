using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.UI.Features.GameOperations;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.Services.GameRuntime;
using Cafe.Launcher.Testing;
using Cafe.Launcher.Core.Services.Diagnostics;
using Cafe.Launcher.Core.Services.GameRuntime;
using Cafe.Launcher.Core.Models;

namespace Cafe.Launcher.Tests;

public sealed class GameSessionMonitorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WatchRunning_WhenRealScannerFails_KeepsRunningUntilSuccessfulScansConfirmAbsence(bool linux)
    {
        using var directory = TestDirectory.Create();
        var procRoot = directory.Sub("proc");
        var failing = true;
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delays = 0;
        using var monitor = CreateMonitor(
            new FakeTracker(),
            (names, token) => Task.FromResult(linux
                ? LinuxProcessScanner.Scan(new RunningGameQuery(names), procRoot, token)
                : ProcessService.FindRunningExeNamesBySnapshot(names,
                    () => failing ? throw new System.ComponentModel.Win32Exception(5) : [], token)),
            (_, _) => ++delays == 2 ? resume.Task : Task.CompletedTask);

        try
        {
            monitor.BeginSession(GameRuntimeRunners.Native, Family);
            Assert.Equal(GameSessionState.Running, monitor.State);
            Assert.Null(monitor.LastSessionExit);

            failing = false;
            Directory.CreateDirectory(procRoot);
            resume.SetResult();
            await TestWait.UntilAsync(() => monitor.State == GameSessionState.Exited,
                TimeSpan.FromSeconds(5), "成功扫描连续确认缺席后应结束会话");
        }
        finally
        {
            resume.TrySetResult();
        }
    }

    [Fact]
    public async Task TrackedProcessExited_WhenConfirmationScanFails_KeepsRunningAndResumesWatching()
    {
        var failing = false;
        var scans = 0;
        var tracker = new FakeTracker();
        using var monitor = CreateMonitor(tracker, Probe(() =>
        {
            scans++;
            return failing ? throw new InvalidOperationException("scan unavailable") : FamilyRuns();
        }), CountingDelay(0));
        monitor.BeginSession(GameRuntimeRunners.Native, Family);

        failing = true;
        tracker.RaiseExited();

        await TestWait.UntilAsync(() => scans >= 3, TimeSpan.FromSeconds(5),
            "核实失败后必须恢复看护，等待下一次成功扫描。");
        Assert.Equal(GameSessionState.Running, monitor.State);
        Assert.Null(monitor.LastSessionExit);
    }

    [Fact]
    public void WatchStarting_WhenScanFailsAndHostAlreadyExited_DoesNotTreatFailureAsAbsence()
    {
        using var monitor = CreateMonitor(new FakeTracker { HasLive = false },
            Probe(() => throw new InvalidOperationException("scan unavailable")), CountingDelay(0));

        monitor.BeginSession(GameRuntimeRunners.Wine, Family);

        Assert.Equal(GameSessionState.Starting, monitor.State);
    }

    [Fact]
    public async Task WatchStarting_WhenScanNeverAnswers_TimesOutAsUnknownAndKeepsWatching()
    {
        // AUD-PERF-011：首轮扫描是同步枚举，被反作弊挂住时 BeginSession 不能等它；
        // 限时到点按「未知」处理（不是「没在跑」），下一拍继续扫描。
        var diagnostics = new RecordingDiagnostics();
        var scans = 0;
        var hangingScan = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var monitor = new GameSessionMonitor(
            new FakeTracker(),
            (_, _) => Interlocked.Increment(ref scans) == 1
                ? hangingScan.Task
                : Task.FromResult(FamilyRuns()),
            CountingDelay(1),
            diagnostics,
            scanTimeout: TimeSpan.FromMilliseconds(50));

        monitor.BeginSession(GameRuntimeRunners.Wine, Family);

        // BeginSession 已返回（没有等待挂起的扫描），此刻尚无结论。
        Assert.Equal(GameSessionState.Starting, monitor.State);
        await TestWait.UntilAsync(
            () => monitor.State == GameSessionState.Running,
            TimeSpan.FromSeconds(5),
            "超时按未知处理后应继续轮询并认出家族");
        Assert.True(scans >= 2);
        Assert.Contains(diagnostics.Warnings, warning => warning.Contains("did not answer", StringComparison.Ordinal));
        hangingScan.TrySetResult(FamilyRuns());
    }

    [Fact]
    public async Task DelayNextPoll_WhenWatchIsSuperseded_ObservesCancellation()
    {
        // 令牌必须传进等待本身：否则 Dispose 或重开会话要等满一个轮询间隔才被看见。
        var delayStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delayCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var monitor = new GameSessionMonitor(
            new FakeTracker(),
            (_, _) => Task.FromResult(EmptyRuns()),
            async (_, token) =>
            {
                delayStarted.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }
                catch (OperationCanceledException)
                {
                    delayCancelled.TrySetResult();
                    throw;
                }
            });

        monitor.BeginSession(GameRuntimeRunners.Wine, Family);
        await delayStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        monitor.BeginSession(GameRuntimeRunners.Native, Family);

        await TestWait.UntilAsync(
            () => delayCancelled.Task.IsCompleted,
            TimeSpan.FromSeconds(5),
            "重开会话应立即取消上一轮的等待");
        Assert.Equal(GameSessionState.Running, monitor.State);
    }

    [Fact]
    public async Task TrackedProcessExited_WhenConfirmationScanTimesOut_KeepsRunningAndResumesWatching()
    {
        // 宿主退出后的最后一次核实同样受时限约束：没有答案不是「家族已退」的证据。
        var tracker = new FakeTracker();
        var initialScanSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var hangingConfirmation = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var hostExited = false;
        var postExitScans = 0;
        using var monitor = new GameSessionMonitor(
            tracker,
            (_, _) =>
            {
                if (!hostExited)
                {
                    initialScanSeen.TrySetResult();
                    return Task.FromResult(FamilyRuns());
                }

                return Interlocked.Increment(ref postExitScans) == 1
                    ? hangingConfirmation.Task
                    : Task.FromResult(FamilyRuns());
            },
            CountingDelay(0),
            scanTimeout: TimeSpan.FromMilliseconds(50));

        monitor.BeginSession(GameRuntimeRunners.Native, Family);
        await initialScanSeen.Task.WaitAsync(TimeSpan.FromSeconds(5));

        hostExited = true;
        tracker.RaiseExited();

        await TestWait.UntilAsync(
            () => postExitScans >= 2,
            TimeSpan.FromSeconds(5),
            "核实超时后应维持运行中并恢复看护");
        Assert.Equal(GameSessionState.Running, monitor.State);
        Assert.Null(monitor.LastSessionExit);
        hangingConfirmation.TrySetResult(FamilyRuns());
    }

    private static readonly IReadOnlyList<string> Family =
        GameProcessNames.FromLaunchConfiguration("xldr_BlueArchiveOnline_JP_loader_x64", ["BlueArchive.exe"]);

    [Fact]
    public void BeginSession_WhenNative_SessionIsRunningWithoutWaiting()
    {
        // 原生启动时被跟踪的宿主就是游戏本身：无需等扫描确认。
        var tracker = new FakeTracker();
        using var monitor = CreateMonitor(tracker, NeverRunning(), CountingDelay(0));

        monitor.BeginSession(GameRuntimeRunners.Native, Family);

        Assert.Equal(GameSessionState.Running, monitor.State);
        Assert.Null(monitor.LastSessionExit);
    }

    [Fact]
    public void BeginSession_WhenRunner_SessionStartsBeforeTheFamilyIsSeen()
    {
        var tracker = new FakeTracker();
        using var monitor = CreateMonitor(tracker, NeverRunning(), CountingDelay(0));

        monitor.BeginSession(GameRuntimeRunners.Wine, Family);

        Assert.Equal(GameSessionState.Starting, monitor.State);
    }

    [Fact]
    public async Task WatchStarting_WhenFamilyAppears_ClassifiesRunning()
    {
        var tracker = new FakeTracker();
        var states = new List<GameSessionState>();
        using var monitor = CreateMonitor(tracker, Scripted([], FamilyRuns()), CountingDelay(1));
        monitor.StateChanged += () => states.Add(monitor.State);

        monitor.BeginSession(GameRuntimeRunners.Wine, Family);

        // 扫描移出调用线程后，Starting 与 Running 谁先被通知取决于看护线程与
        // BeginSession 收尾的先后；终态通知必须是 Running，且不重复。
        await WaitForStateAsync(monitor, GameSessionState.Running, "家族出现后应判定运行中");
        await TestWait.UntilAsync(
            () => states.Count > 0 && states[^1] == GameSessionState.Running,
            TimeSpan.FromSeconds(5),
            "进入运行中后应通知订阅者");
        Assert.DoesNotContain(GameSessionState.Exited, states);
        Assert.DoesNotContain(GameSessionState.StartFailed, states);
    }

    [Fact]
    public async Task WatchStarting_WhileHostLivesAndFamilyAbsent_KeepsPollingUntilItAppears()
    {
        // 启动中的兼容层（建 Prefix 等）可以让家族迟迟不现身：宿主活着就不能下结论。
        var tracker = new FakeTracker();
        var polls = 0;
        using var monitor = CreateMonitor(
            tracker,
            Probe(() =>
            {
                polls++;
                return polls < 4 ? EmptyRuns() : FamilyRuns();
            }),
            CountingDelay(3));

        monitor.BeginSession(GameRuntimeRunners.Wine, Family);

        await WaitForStateAsync(monitor, GameSessionState.Running, "家族出现后应判定运行中");
        Assert.True(polls >= 4, "家族出现前必须持续轮询，而不是一轮就定论。");
    }

    [Fact]
    public async Task WatchStarting_WhenScanThrows_KeepsWatchingAndRecovers()
    {
        // 枚举没有答案与「没在跑」无法区分：继续等，下一轮再定（与闸门的 fail-open 同一取舍）。
        var tracker = new FakeTracker();
        var calls = 0;
        using var monitor = CreateMonitor(
            tracker,
            Probe(() =>
            {
                calls++;
                if (calls == 1)
                {
                    throw new InvalidOperationException("process enumeration failed");
                }

                return FamilyRuns();
            }),
            CountingDelay(1));

        monitor.BeginSession(GameRuntimeRunners.Wine, Family);

        await WaitForStateAsync(monitor, GameSessionState.Running, "枚举失败后应继续轮询并恢复");
    }

    [Fact]
    public async Task WatchStarting_WhenHostDiedBeforeWatching_ClassifiesStartFailed()
    {
        // Register→BeginSession 之间宿主已退（事件无人接住）：看护循环以 LastExit 收口。
        var tracker = new FakeTracker
        {
            HasLive = false,
            Exit = new GameLaunchExitInfo(53, TimeSpan.FromSeconds(1), DateTimeOffset.Now, "wine")
        };
        using var monitor = CreateMonitor(tracker, NeverRunning(), CountingDelay(0));

        monitor.BeginSession(GameRuntimeRunners.Wine, Family);

        await WaitForStateAsync(monitor, GameSessionState.StartFailed, "宿主已亡且家族缺席应判定启动失败");
        Assert.Equal(53, monitor.LastSessionExit!.ExitCode);
    }

    [Fact]
    public void TrackedProcessExited_WhileStarting_ClassifiesStartFailed()
    {
        // 运行器提前退出、家族从未现身：这就是「游戏未能启动」。
        var tracker = new FakeTracker();
        using var monitor = CreateMonitor(tracker, NeverRunning(), CountingDelay(0));
        monitor.BeginSession(GameRuntimeRunners.Wine, Family);
        Assert.Equal(GameSessionState.Starting, monitor.State);

        tracker.Exit = new GameLaunchExitInfo(-1073741515, TimeSpan.FromSeconds(3), DateTimeOffset.Now, "wine");
        tracker.RaiseExited();

        Assert.Equal(GameSessionState.StartFailed, monitor.State);
        Assert.Equal(-1073741515, monitor.LastSessionExit!.ExitCode);
    }

    [Fact]
    public async Task TrackedProcessExited_WhileRunningAndFamilyGone_ClassifiesExited()
    {
        var familyVisible = true;
        var tracker = new FakeTracker();
        using var monitor = CreateMonitor(tracker, Probe(() => familyVisible ? FamilyRuns() : EmptyRuns()), CountingDelay(0));
        monitor.BeginSession(GameRuntimeRunners.Wine, Family);
        await WaitForStateAsync(monitor, GameSessionState.Running, "家族现身应判定运行中");

        familyVisible = false;
        tracker.Exit = new GameLaunchExitInfo(0, TimeSpan.FromHours(2), DateTimeOffset.Now, "umu");
        tracker.RaiseExited();

        await WaitForStateAsync(monitor, GameSessionState.Exited, "宿主退出且家族缺席应判定已退出");
        Assert.Equal(0, monitor.LastSessionExit!.ExitCode);
    }

    [Fact]
    public async Task TrackedProcessExited_WhileRunningButFamilyStillVisible_StaysRunningAndKeepsWatching()
    {
        // 母进程退出、真身（启动参数里的另一个可执行文件）还在跑：不报已退出（ADR-032 的家族判据）；
        // 宿主已亡，此后的退出只剩运行期轮询这一条信号可走。
        var familyVisible = true;
        var scans = 0;
        var tracker = new FakeTracker();
        var probe = Probe(() =>
        {
            scans++;
            return familyVisible ? FamilyRuns() : EmptyRuns();
        });
        using var monitor = CreateMonitor(tracker, probe, CountingDelay(0));
        monitor.BeginSession(GameRuntimeRunners.Wine, Family);
        await WaitForStateAsync(monitor, GameSessionState.Running, "家族现身应判定运行中");

        tracker.Exit = new GameLaunchExitInfo(0, TimeSpan.FromMinutes(1), DateTimeOffset.Now, "wine");
        tracker.RaiseExited();

        // 核实扫描看见家族即维持运行中；后续退出由重启的看护继续确认
        // （无退出码的收口由 WhenNativeHostDiedEarlyAndGameQuitsLater 覆盖）。
        await TestWait.UntilAsync(() => scans >= 2, TimeSpan.FromSeconds(5),
            "宿主退出后应完成家族核实");
        Assert.Equal(GameSessionState.Running, monitor.State);
        Assert.Null(monitor.LastSessionExit);
    }

    [Fact]
    public async Task WatchRunning_WhenFamilyDisappearsConsecutively_ClassifiesExitedWithoutExitInfo()
    {
        // 用户报告的回归：宿主退出后家族是唯一的信号，游戏退出必须把「运行中」收掉。
        // 退出码无从归属（宿主的退出码属于宿主），按无退出码呈现。
        var tracker = new FakeTracker();
        using var monitor = CreateMonitor(tracker, Scripted([], FamilyRuns(), FamilyRuns(), EmptyRuns(), EmptyRuns()), CountingDelay(3));

        monitor.BeginSession(GameRuntimeRunners.Wine, Family);

        await WaitForStateAsync(monitor, GameSessionState.Exited, "家族连续缺席应判定退出");
        Assert.Null(monitor.LastSessionExit);
    }

    [Fact]
    public async Task WatchRunning_WhenFamilyDisappearsForOneScan_KeepsRunning()
    {
        // 单个缺席轮可能是枚举抖动或游戏短暂自重启（打补丁）：不认账，连续两轮才定论。
        var tracker = new FakeTracker();
        var scans = 0;
        using var monitor = CreateMonitor(
            tracker,
            Probe(() =>
            {
                scans++;
                return scans switch
                {
                    1 => EmptyRuns(),
                    4 => EmptyRuns(),
                    _ => FamilyRuns()
                };
            }),
            CountingDelay(3));

        monitor.BeginSession(GameRuntimeRunners.Wine, Family);

        await TestWait.UntilAsync(() => scans >= 5, TimeSpan.FromSeconds(5),
            "缺席一轮后应继续轮询");
        Assert.Equal(GameSessionState.Running, monitor.State);
    }

    [Fact]
    public async Task WatchRunning_WhenScanFails_MissIsNotCounted()
    {
        // 枚举没有答案不是「没在跑」的证据（与闸门同一取舍）：这一拍不计入缺席。
        var tracker = new FakeTracker();
        var calls = 0;
        using var monitor = CreateMonitor(
            tracker,
            Probe(() =>
            {
                calls++;
                return calls switch
                {
                    1 => EmptyRuns(),
                    2 => FamilyRuns(),
                    3 => throw new InvalidOperationException("process enumeration failed"),
                    4 => EmptyRuns(),
                    _ => FamilyRuns()
                };
            }),
            CountingDelay(3));

        monitor.BeginSession(GameRuntimeRunners.Wine, Family);

        await TestWait.UntilAsync(() => calls >= 5, TimeSpan.FromSeconds(5),
            "枚举失败的一拍不应计入缺席");
        Assert.Equal(GameSessionState.Running, monitor.State);
    }

    [Fact]
    public async Task WatchRunning_WhenNativeHostDiedEarlyAndGameQuitsLater_ClassifiesExited()
    {
        // 实测（ADR-032）：原生加载器宿主在游戏起来后就退出。此后游戏退出只有轮询能看见——
        // 这正是「游戏已退出仍显示运行中」的根因与本用例要钉住的行为。
        var familyVisible = true;
        var hostExited = false;
        var postExitScans = 0;
        var tracker = new FakeTracker();
        var gate = new Gate();
        using var monitor = CreateMonitor(tracker, Probe(() =>
        {
            if (hostExited)
            {
                postExitScans++;
            }

            return familyVisible ? FamilyRuns() : EmptyRuns();
        }), gate.Delay());

        monitor.BeginSession(GameRuntimeRunners.Native, Family);
        Assert.Equal(GameSessionState.Running, monitor.State);

        tracker.Exit = new GameLaunchExitInfo(0, TimeSpan.FromSeconds(4), DateTimeOffset.Now, "native");
        hostExited = true;
        tracker.RaiseExited();

        // 家族仍在（真身还在跑）：核实与重启后的看护都先看见家族，维持运行中。
        await TestWait.UntilAsync(() => postExitScans >= 2, TimeSpan.FromSeconds(5),
            "宿主退出后的核实应看见仍在跑的家族");
        Assert.Equal(GameSessionState.Running, monitor.State);
        Assert.Null(monitor.LastSessionExit);

        familyVisible = false;
        // 放行直到终态：宿主死前的旧循环与重启后的新循环共用同一个队列，谁的票被放走
        // 不影响终局——任一循环数满连续两轮缺席都会定论。
        await TestWait.UntilAsync(
            () =>
            {
                gate.TryReleaseNext();
                return monitor.State == GameSessionState.Exited;
            },
            TimeSpan.FromSeconds(10),
            "游戏家族连续缺席后应判定退出");

        Assert.Equal(GameSessionState.Exited, monitor.State);
        Assert.Null(monitor.LastSessionExit);
    }

    [Fact]
    public void TrackedProcessExited_AfterTerminalState_DoesNothing()
    {
        var tracker = new FakeTracker();
        using var monitor = CreateMonitor(tracker, NeverRunning(), CountingDelay(0));
        monitor.BeginSession(GameRuntimeRunners.Wine, Family);
        tracker.Exit = new GameLaunchExitInfo(1, TimeSpan.FromSeconds(1), DateTimeOffset.Now, "wine");
        tracker.RaiseExited();
        Assert.Equal(GameSessionState.StartFailed, monitor.State);

        tracker.Exit = new GameLaunchExitInfo(2, TimeSpan.FromSeconds(2), DateTimeOffset.Now, "wine");
        tracker.RaiseExited();

        // 终态只落一次：退出码留在第一次退出上。
        Assert.Equal(GameSessionState.StartFailed, monitor.State);
        Assert.Equal(1, monitor.LastSessionExit!.ExitCode);
    }

    [Fact]
    public async Task BeginSession_WhenPreviousSessionEnded_StartsFreshWithoutOldExit()
    {
        var familyVisible = true;
        var tracker = new FakeTracker();
        using var monitor = CreateMonitor(tracker, Probe(() => familyVisible ? FamilyRuns() : EmptyRuns()), CountingDelay(0));
        monitor.BeginSession(GameRuntimeRunners.Wine, Family);
        await WaitForStateAsync(monitor, GameSessionState.Running, "家族现身应判定运行中");

        familyVisible = false;
        tracker.RaiseExited();
        await WaitForStateAsync(monitor, GameSessionState.Exited, "家族缺席时宿主退出应判定已退出");

        familyVisible = true;
        monitor.BeginSession(GameRuntimeRunners.Wine, Family);

        // 重开即清零：上一局的「已退出」不再冒充本局事实。
        Assert.Null(monitor.LastSessionExit);
        Assert.NotEqual(GameSessionState.Exited, monitor.State);
    }

    [Fact]
    public void State_InitiallyIdle()
    {
        using var monitor = CreateMonitor(new FakeTracker(), NeverRunning(), CountingDelay(0));

        Assert.Equal(GameSessionState.Idle, monitor.State);
    }

    private static GameSessionMonitor CreateMonitor(
        FakeTracker tracker,
        Func<IReadOnlyList<string>, CancellationToken, Task<IReadOnlyList<string>>> scan,
        Func<TimeSpan, CancellationToken, Task> delayAsync) =>
        new(tracker, scan, delayAsync);

    /// <summary>扫描离开调用线程后状态异步落定：断言前先等到目标态。</summary>
    private static Task WaitForStateAsync(
        GameSessionMonitor monitor,
        GameSessionState expected,
        string reason) =>
        TestWait.UntilAsync(() => monitor.State == expected, TimeSpan.FromSeconds(5), reason);

    private static Func<IReadOnlyList<string>, CancellationToken, Task<IReadOnlyList<string>>> NeverRunning() =>
        Probe(() => EmptyRuns());

    private static Func<IReadOnlyList<string>, CancellationToken, Task<IReadOnlyList<string>>> Probe(
        Func<IReadOnlyList<string>> next) =>
        (_, _) => Task.FromResult(next());

    /// <summary>按脚本逐次返回的扫描替身：脚本用尽后重复最后一项。</summary>
    private static Func<IReadOnlyList<string>, CancellationToken, Task<IReadOnlyList<string>>> Scripted(
        params IReadOnlyList<string>[] results)
    {
        var index = -1;
        return Probe(() =>
        {
            index = Math.Min(index + 1, results.Length - 1);
            return results[index];
        });
    }

    /// <summary>前 <paramref name="completedCalls"/> 次等待立即返回，之后永久停驻：
    /// 看护循环据此推进既定步数后停住，既不热转也不会悬在半路。</summary>
    private static Func<TimeSpan, CancellationToken, Task> CountingDelay(int completedCalls)
    {
        var remaining = completedCalls;
        return (_, _) =>
        {
            if (remaining-- > 0)
            {
                return Task.CompletedTask;
            }

            var parked = new TaskCompletionSource();
            return parked.Task;
        };
    }

    /// <summary>测试手工放行的等待闸门：每放行一次，看护循环恰好推进一拍。</summary>
    private sealed class Gate
    {
        private readonly Queue<TaskCompletionSource> pending = [];

        public Func<TimeSpan, CancellationToken, Task> Delay()
        {
            var parked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            pending.Enqueue(parked);
            return (_, _) => parked.Task;
        }

        /// <summary>队列空时静默：放行节奏不必与循环的入队节奏精确对齐。</summary>
        public void TryReleaseNext()
        {
            if (pending.Count > 0)
            {
                pending.Dequeue().TrySetResult();
            }
        }
    }

    private static IReadOnlyList<string> EmptyRuns() => [];

    private static IReadOnlyList<string> FamilyRuns() => ["BlueArchive"];

    /// <summary>进程跟踪替身：退出事件与存活状态由测试手工驱动。</summary>
    private sealed class FakeTracker : IGameProcessTracker
    {
        public bool HasLive { get; set; } = true;

        public GameLaunchExitInfo? Exit { get; set; }

        public bool HasLiveTrackedProcess => HasLive;

        public GameLaunchExitInfo? LastExit => Exit;

        public event Action? TrackedProcessExited;

        public void Register(GameProcess process) => throw new NotSupportedException();

        public Task<IReadOnlyList<string>> FindRunningGameProcessesAsync(
            RunningGameQuery query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public void RaiseExited() => TrackedProcessExited?.Invoke();
    }
}
