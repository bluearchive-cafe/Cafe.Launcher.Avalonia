using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Features.GameOperations;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services.GameRuntime;
using Cafe.Launcher.Avalonia.Testing;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class GameSessionMonitorTests
{
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
    public void WatchStarting_WhenFamilyAppears_ClassifiesRunning()
    {
        var tracker = new FakeTracker();
        var states = new List<GameSessionState>();
        using var monitor = CreateMonitor(tracker, Scripted([], FamilyRuns()), CountingDelay(1));
        monitor.StateChanged += () => states.Add(monitor.State);

        monitor.BeginSession(GameRuntimeRunners.Wine, Family);

        // 同步完成的一轮被合并成一次通知：订阅者只见最终态，不见中间的 Starting。
        Assert.Equal(GameSessionState.Running, monitor.State);
        Assert.Equal([GameSessionState.Running], states);
    }

    [Fact]
    public void WatchStarting_WhileHostLivesAndFamilyAbsent_KeepsPollingUntilItAppears()
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

        Assert.Equal(GameSessionState.Running, monitor.State);
        Assert.True(polls >= 4, "家族出现前必须持续轮询，而不是一轮就定论。");
    }

    [Fact]
    public void WatchStarting_WhenScanThrows_KeepsWatchingAndRecovers()
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

        Assert.Equal(GameSessionState.Running, monitor.State);
    }

    [Fact]
    public void WatchStarting_WhenHostDiedBeforeWatching_ClassifiesStartFailed()
    {
        // Register→BeginSession 之间宿主已退（事件无人接住）：看护循环以 LastExit 收口。
        var tracker = new FakeTracker
        {
            HasLive = false,
            Exit = new GameLaunchExitInfo(53, TimeSpan.FromSeconds(1), DateTimeOffset.Now, "wine")
        };
        using var monitor = CreateMonitor(tracker, NeverRunning(), CountingDelay(0));

        monitor.BeginSession(GameRuntimeRunners.Wine, Family);

        Assert.Equal(GameSessionState.StartFailed, monitor.State);
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
    public void TrackedProcessExited_WhileRunningAndFamilyGone_ClassifiesExited()
    {
        var familyVisible = true;
        var tracker = new FakeTracker();
        using var monitor = CreateMonitor(tracker, Probe(() => familyVisible ? FamilyRuns() : EmptyRuns()), CountingDelay(0));
        monitor.BeginSession(GameRuntimeRunners.Wine, Family);
        Assert.Equal(GameSessionState.Running, monitor.State);

        familyVisible = false;
        tracker.Exit = new GameLaunchExitInfo(0, TimeSpan.FromHours(2), DateTimeOffset.Now, "umu");
        tracker.RaiseExited();

        Assert.Equal(GameSessionState.Exited, monitor.State);
        Assert.Equal(0, monitor.LastSessionExit!.ExitCode);
    }

    [Fact]
    public void TrackedProcessExited_WhileRunningButFamilyStillVisible_StaysRunningAndKeepsWatching()
    {
        // 母进程退出、真身（启动参数里的另一个可执行文件）还在跑：不报已退出（ADR-032 的家族判据）；
        // 宿主已亡，此后的退出只剩运行期轮询这一条信号可走。
        var familyVisible = true;
        var tracker = new FakeTracker();
        var probe = Probe(() => familyVisible ? FamilyRuns() : EmptyRuns());
        using var monitor = CreateMonitor(tracker, probe, CountingDelay(0));
        monitor.BeginSession(GameRuntimeRunners.Wine, Family);
        Assert.Equal(GameSessionState.Running, monitor.State);

        tracker.Exit = new GameLaunchExitInfo(0, TimeSpan.FromMinutes(1), DateTimeOffset.Now, "wine");
        tracker.RaiseExited();

        Assert.Equal(GameSessionState.Running, monitor.State);
        Assert.Null(monitor.LastSessionExit);
    }

    [Fact]
    public void WatchRunning_WhenFamilyDisappearsConsecutively_ClassifiesExitedWithoutExitInfo()
    {
        // 用户报告的回归：宿主退出后家族是唯一的信号，游戏退出必须把「运行中」收掉。
        // 退出码无从归属（宿主的退出码属于宿主），按无退出码呈现。
        var tracker = new FakeTracker();
        using var monitor = CreateMonitor(tracker, Scripted([], FamilyRuns(), FamilyRuns(), EmptyRuns(), EmptyRuns()), CountingDelay(3));

        monitor.BeginSession(GameRuntimeRunners.Wine, Family);

        Assert.Equal(GameSessionState.Exited, monitor.State);
        Assert.Null(monitor.LastSessionExit);
    }

    [Fact]
    public void WatchRunning_WhenFamilyDisappearsForOneScan_KeepsRunning()
    {
        // 单个缺席轮可能是枚举抖动或游戏短暂自重启（打补丁）：不认账，连续两轮才定论。
        var tracker = new FakeTracker();
        using var monitor = CreateMonitor(
            tracker,
            Scripted([], FamilyRuns(), FamilyRuns(), EmptyRuns(), FamilyRuns()),
            CountingDelay(3));

        monitor.BeginSession(GameRuntimeRunners.Wine, Family);

        Assert.Equal(GameSessionState.Running, monitor.State);
    }

    [Fact]
    public void WatchRunning_WhenScanFails_MissIsNotCounted()
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

        Assert.Equal(GameSessionState.Running, monitor.State);
    }

    [Fact]
    public async Task WatchRunning_WhenNativeHostDiedEarlyAndGameQuitsLater_ClassifiesExited()
    {
        // 实测（ADR-032）：原生加载器宿主在游戏起来后就退出。此后游戏退出只有轮询能看见——
        // 这正是「游戏已退出仍显示运行中」的根因与本用例要钉住的行为。
        var familyVisible = true;
        var tracker = new FakeTracker();
        var gate = new Gate();
        using var monitor = CreateMonitor(tracker, Probe(() => familyVisible ? FamilyRuns() : EmptyRuns()), gate.Delay());

        monitor.BeginSession(GameRuntimeRunners.Native, Family);
        Assert.Equal(GameSessionState.Running, monitor.State);

        tracker.Exit = new GameLaunchExitInfo(0, TimeSpan.FromSeconds(4), DateTimeOffset.Now, "native");
        tracker.RaiseExited();
        // 家族仍在（真身还在跑）：维持运行中，运行期看护已重启。
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
    public void BeginSession_WhenPreviousSessionEnded_StartsFreshWithoutOldExit()
    {
        var familyVisible = true;
        var tracker = new FakeTracker();
        using var monitor = CreateMonitor(tracker, Probe(() => familyVisible ? FamilyRuns() : EmptyRuns()), CountingDelay(0));
        monitor.BeginSession(GameRuntimeRunners.Wine, Family);
        familyVisible = false;
        tracker.RaiseExited();
        Assert.Equal(GameSessionState.Exited, monitor.State);

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
        Func<TimeSpan, Task> delayAsync) =>
        new(tracker, scan, delayAsync);

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
    private static Func<TimeSpan, Task> CountingDelay(int completedCalls)
    {
        var remaining = completedCalls;
        return _ =>
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

        public Func<TimeSpan, Task> Delay()
        {
            var parked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            pending.Enqueue(parked);
            return _ => parked.Task;
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
            IReadOnlyList<string> knownExeNames,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public void RaiseExited() => TrackedProcessExited?.Invoke();
    }
}
