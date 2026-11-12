using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.Core.Services.Diagnostics;
using Cafe.Launcher.Core.Services.GameRuntime;
using Cafe.Launcher.Core.Models;

namespace Cafe.Launcher.UI.Features.GameOperations;

/// <summary>
/// Default <see cref="IGameSessionMonitor"/>: a small state machine over the process
/// tracker's exit signal plus the same family scan the destructive gates use.
/// 「运行器提前退出」与「游戏正常退出」在这里分开——启动报告的「成功」只覆盖
/// <c>Process.Start</c> 那一刻，兼容层内部失败要等看护来揭穿（ADR-035）。
/// </summary>
internal sealed class GameSessionMonitor : IGameSessionMonitor, IDisposable
{
    /// <summary>轮询节奏：一次全系统进程枚举远不到一秒，五秒看一眼足够细，也不构成
    /// 可感知的开销。「启动中」与「运行中」两态共用这个节奏。</summary>
    internal static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    /// <summary>「运行中」判定退出所需的连续未命中轮数：一轮缺席可能只是枚举抖动或
    /// 游戏短暂自重启（打补丁），两轮（约十秒）仍不见才认账。</summary>
    internal static readonly int MissedScansBeforeExited = 2;

    /// <summary>
    /// 单轮扫描的时限，与闸门共用同一个值（5 秒）：正常一次全系统进程枚举远不到一秒；
    /// 游戏运行时反作弊内核组件在场，快照查询可能长期不返回，而扫描正文是同步枚举、令牌
    /// 拦不住已经开始的那一次。首轮扫描直接 await 会在 UI 线程上等枚举，挂住时启动后流程
    /// 停在首个进度之前、状态行停在「启动中」再也不动；宿主退出后的核实扫描同理
    /// （AUD-PERF-011）。超时按「未知」处理——未知不是「没在跑」的证据。
    /// </summary>
    internal static readonly TimeSpan DefaultScanTimeout = RunningGameGate.DefaultScanTimeout;

    private readonly IGameProcessTracker gameProcessTracker;
    private readonly Func<IReadOnlyList<string>, CancellationToken, Task<IReadOnlyList<string>>> scanRunningExeNames;
    private readonly Func<TimeSpan, CancellationToken, Task> delayAsync;
    private readonly ILauncherDiagnostics? diagnostics;
    private readonly TimeSpan scanTimeout;
    private readonly object gate = new();

    private CancellationTokenSource? watchCancellation;
    private GameSessionState state = GameSessionState.Idle;
    private GameSessionState lastRaisedState = GameSessionState.Idle;
    private long sessionVersion;
    private GameLaunchExitInfo? lastSessionExit;
    private IReadOnlyList<string> knownExeNames = [];

    public GameSessionMonitor(IGameProcessTracker gameProcessTracker, ILauncherDiagnostics? diagnostics = null)
        : this(gameProcessTracker, ProcessService.FindRunningExeNamesAsync, diagnostics)
    {
    }

    /// <summary>Test seam: the family scan runs against an injected probe so state
    /// transitions can be driven without spawning real processes.</summary>
    internal GameSessionMonitor(
        IGameProcessTracker gameProcessTracker,
        Func<IReadOnlyList<string>, CancellationToken, Task<IReadOnlyList<string>>> scanRunningExeNames,
        ILauncherDiagnostics? diagnostics = null)
        : this(
            gameProcessTracker,
            scanRunningExeNames,
            static (delay, token) => Task.Delay(delay, token),
            diagnostics)
    {
    }

    internal GameSessionMonitor(
        IGameProcessTracker gameProcessTracker,
        Func<IReadOnlyList<string>, CancellationToken, Task<IReadOnlyList<string>>> scanRunningExeNames,
        Func<TimeSpan, CancellationToken, Task> delayAsync,
        ILauncherDiagnostics? diagnostics = null,
        TimeSpan? scanTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(gameProcessTracker);
        ArgumentNullException.ThrowIfNull(scanRunningExeNames);
        ArgumentNullException.ThrowIfNull(delayAsync);
        this.gameProcessTracker = gameProcessTracker;
        this.scanRunningExeNames = scanRunningExeNames;
        this.delayAsync = delayAsync;
        this.diagnostics = diagnostics;
        this.scanTimeout = scanTimeout ?? DefaultScanTimeout;
        // BeginSession 晚于 Register（启动结果先走完旅程的诊断与提示）。Register 与
        // 订阅之间发生的退出由 LastExit 携带——跟踪器在 Register 时把它清零，语义是
        // 「最近一次登记的进程的退出」，所以这里非空即当前会话的宿主已亡。
        gameProcessTracker.TrackedProcessExited += OnTrackedProcessExited;
    }

    public GameSessionState State
    {
        get
        {
            lock (gate)
            {
                return state;
            }
        }
    }

    public GameLaunchExitInfo? LastSessionExit
    {
        get
        {
            lock (gate)
            {
                return lastSessionExit;
            }
        }
    }

    public event Action? StateChanged;

    public void BeginSession(string runnerId, IReadOnlyList<string> knownExeNames)
    {
        ArgumentNullException.ThrowIfNull(knownExeNames);

        lock (gate)
        {
            CancelWatchLocked();
            // 通知基准回到「本会话开始前」：上一局的终态不算本局已通知。
            lastRaisedState = state;
            sessionVersion++;
            // 原生启动时被跟踪的宿主就是游戏本身：句柄活着即运行中，无需等扫描确认；
            // 运行器启动时宿主只是 wine/umu-run，「游戏起来了」要等家族现身。
            state = string.Equals(runnerId, GameRuntimeRunners.Native, StringComparison.OrdinalIgnoreCase)
                ? GameSessionState.Running
                : GameSessionState.Starting;
            lastSessionExit = null;
            this.knownExeNames = knownExeNames;
            if (state == GameSessionState.Starting)
            {
                StartStartingWatchLocked();
            }
            else
            {
                StartRunningWatchLocked();
            }
        }

        RaiseStateChanged();
    }

    public void Dispose()
    {
        lock (gate)
        {
            CancelWatchLocked();
        }

        gameProcessTracker.TrackedProcessExited -= OnTrackedProcessExited;
    }

    /// <summary>
    /// Starting 态的看护循环：看到家族即运行中并转入运行期看护；宿主先亡而退出事件
    /// 无人接住（Register→BeginSession 之间）时由本循环收尾定论。宿主还活着就再等
    /// 一拍——它是唯一能宣布终局的另一方。
    /// </summary>
    private async Task WatchStartingAsync(CancellationToken cancellationToken)
    {
        var names = knownExeNames;
        while (true)
        {
            IReadOnlyList<string>? running;
            try
            {
                running = await ScanAsync(names, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception)
            {
                // 没有成功扫描就没有答案，不能据此判定家族缺席，即使宿主已在订阅前退出。
                running = null;
            }

            if (running is null)
            {
                // 未知（超时或枚举失败）不是「没在跑」的证据：这一拍不参与任何判定。
                if (!await DelayNextPollAsync(cancellationToken).ConfigureAwait(false))
                {
                    return;
                }

                continue;
            }

            if (ContainsFamilyMember(running, names))
            {
                EnterRunning();
                return;
            }

            if (State != GameSessionState.Starting)
            {
                // 退出事件已把会话带入终态；轮询让位。
                return;
            }

            if (!gameProcessTracker.HasLiveTrackedProcess)
            {
                // 宿主已亡且退出事件发生在订阅之前：刚刚这轮扫描仍不见家族，
                // 即定论启动失败（退出信息可能缺，VM 报「退出码 -1」）。
                Transition(GameSessionState.StartFailed, gameProcessTracker.LastExit);
                return;
            }

            if (!await DelayNextPollAsync(cancellationToken).ConfigureAwait(false))
            {
                return;
            }
        }
    }

    /// <summary>
    /// 运行中态的看护循环（ADR-035 决策 7）：「运行中」的退役信号不能只压在被跟踪
    /// 宿主的退出事件上——宿主可能在游戏起来后就退出了（实测加载器正是如此），此后
    /// 既无句柄可订阅也无别的信号，游戏退出就会永远停在「运行中」。家族连续
    /// <see cref="MissedScansBeforeExited"/> 轮不可见即判定退出；单个缺席轮（枚举抖动、
    /// 游戏短暂自重启）不认账。
    /// </summary>
    private async Task WatchRunningAsync(CancellationToken cancellationToken)
    {
        var names = knownExeNames;
        var missedScans = 0;
        while (State == GameSessionState.Running)
        {
            IReadOnlyList<string>? running;
            try
            {
                running = await ScanAsync(names, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception)
            {
                // 枚举没有答案不是「没在跑」的证据：这一拍不计入缺席。
                running = null;
            }

            if (running is null)
            {
                if (!await DelayNextPollAsync(cancellationToken).ConfigureAwait(false))
                {
                    return;
                }

                continue;
            }

            if (ContainsFamilyMember(running, names))
            {
                missedScans = 0;
            }
            else if (++missedScans >= MissedScansBeforeExited)
            {
                // 家族确实不见了。退出信息无从归属（宿主的退出码属于宿主，不属于这一刻），
                // 按无退出码的「游戏已退出」呈现。
                Transition(GameSessionState.Exited, exit: null);
                return;
            }

            if (!await DelayNextPollAsync(cancellationToken).ConfigureAwait(false))
            {
                return;
            }
        }
    }

    private void OnTrackedProcessExited()
    {
        long version;
        var exit = gameProcessTracker.LastExit;
        GameSessionState current;
        lock (gate)
        {
            current = state;
            version = sessionVersion;
            if (current is not (GameSessionState.Starting or GameSessionState.Running))
            {
                return;
            }

            CancelWatchLocked();
            if (current == GameSessionState.Starting)
            {
                // 宿主已亡、家族在本会话从未出现：启动失败，就地定论。
                state = GameSessionState.StartFailed;
                lastSessionExit = exit;
            }
            // Running：不在这里落终态——家族可能还在跑，最后一次核实说了算。
        }

        if (current == GameSessionState.Running)
        {
            _ = ConfirmExitAfterHostExitAsync(version, exit);
            return;
        }

        RaiseStateChanged();
    }

    /// <summary>
    /// 运行中宿主退出时的最后一次核实：游戏家族可能还在跑——母进程退出、真身是启动
    /// 参数里的另一个可执行文件，家族判据正是为它存在的（ADR-032）。还看得见就维持
    /// 运行中，看不见才报已退出；此后无句柄可订阅，「游戏是否在跑」由各闸门回答。
    /// </summary>
    private async Task ConfirmExitAfterHostExitAsync(long version, GameLaunchExitInfo? exit)
    {
        IReadOnlyList<string> running = [];
        var scanFailed = false;
        try
        {
            var scan = await ScanAsync(knownExeNames, CancellationToken.None).ConfigureAwait(false);
            if (scan is null)
            {
                // 限时到了仍没有答案：与枚举失败同一取舍——不能据此结束会话。
                scanFailed = true;
            }
            else
            {
                running = scan;
            }
        }
        catch (Exception)
        {
            // 宿主已退出不意味着游戏家族退出；未知状态留给后续成功扫描确认。
            scanFailed = true;
        }

        var familyStillVisible = ContainsFamilyMember(running, knownExeNames);
        lock (gate)
        {
            // 期间重新 BeginSession（version 变了）或已被带离 Running：这次核实过时，弃用。
            if (version != sessionVersion || state != GameSessionState.Running)
            {
                return;
            }

            if (scanFailed || familyStillVisible)
            {
                // 家族仍在，或本轮没有答案：维持运行中并重启看护。
                // 宿主已亡，此后的退出只能由成功的家族扫描确认。
                StartRunningWatchLocked();
                return;
            }

            state = GameSessionState.Exited;
            lastSessionExit = exit;
        }

        RaiseStateChanged();
    }

    /// <summary>
    /// 本轮扫描的限时外壳（与闸门共用 <see cref="BoundedProcessScan"/>）。超时返回 null
    /// 表示这一拍没有答案（未知），调用方不得据此判定家族缺席；超时另写一条诊断——
    /// 枚举被挂住没有别的可见症状，日志是唯一能解释「状态为什么不动」的凭据。
    /// </summary>
    private async Task<IReadOnlyList<string>?> ScanAsync(
        IReadOnlyList<string> names,
        CancellationToken cancellationToken)
    {
        var running = await BoundedProcessScan.RunAsync(
            token => scanRunningExeNames(names, token),
            scanTimeout,
            cancellationToken).ConfigureAwait(false);
        if (running is null)
        {
            await LogScanTimeoutAsync().ConfigureAwait(false);
        }

        return running;
    }

    /// <summary>
    /// 等下一拍；取消时返回 false，循环据此收尾。令牌必须传进等待本身——不传时取消只能
    /// 等满一个 <see cref="PollInterval"/> 才被看见，Dispose 与重开会话都要拖住看护。
    /// </summary>
    private async Task<bool> DelayNextPollAsync(CancellationToken cancellationToken)
    {
        try
        {
            await delayAsync(PollInterval, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// 超时写一条诊断：枚举被挂住没有别的可见症状，日志是唯一能解释「状态为什么不动」的
    /// 凭据。诊断写失败不改变看护结论，异常在这里吞掉。
    /// </summary>
    private async Task LogScanTimeoutAsync()
    {
        if (diagnostics is null)
        {
            return;
        }

        try
        {
            await diagnostics.WarningAsync(
                "GameSession",
                $"Process enumeration did not answer within {scanTimeout.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture)} s; "
                + "this poll is treated as unknown, not as the game being absent.").ConfigureAwait(false);
        }
        catch (Exception)
        {
            // 诊断不可用不能反噬看护循环。
        }
    }

    /// <summary>家族首次现身（或宿主退出核实后仍在跑）时进入运行期看护。</summary>
    private void EnterRunning()
    {
        Transition(GameSessionState.Running, exit: null);
        lock (gate)
        {
            if (state == GameSessionState.Running)
            {
                StartRunningWatchLocked();
            }
        }
    }

    private void StartStartingWatchLocked()
    {
        CancelWatchLocked();
        watchCancellation = new CancellationTokenSource();
        _ = WatchStartingAsync(watchCancellation.Token);
    }

    /// <summary>只在已持有 <see cref="gate"/> 时调用；替换前先取消旧看护（两态共用一个槽位）。</summary>
    private void StartRunningWatchLocked()
    {
        CancelWatchLocked();
        watchCancellation = new CancellationTokenSource();
        _ = WatchRunningAsync(watchCancellation.Token);
    }

    private void Transition(GameSessionState nextState, GameLaunchExitInfo? exit)
    {
        lock (gate)
        {
            if (state is not (GameSessionState.Starting or GameSessionState.Running))
            {
                return;
            }

            CancelWatchLocked();
            state = nextState;
            lastSessionExit = exit;
        }

        RaiseStateChanged();
    }

    /// <summary>只在状态离开上一次已通知的值时引发，通知不重复；新会话以
    /// <see cref="BeginSession"/> 之前的状态为基准，上一局的终态不算本局已通知。</summary>
    private void RaiseStateChanged()
    {
        lock (gate)
        {
            if (state == lastRaisedState)
            {
                return;
            }

            lastRaisedState = state;
        }

        StateChanged?.Invoke();
    }

    /// <summary>取消当前看护循环。只在已持有 <see cref="gate"/> 时调用；不 Dispose：
    /// 看 <see cref="Task.Delay"/> 的令牌注册还挂在上面，立即释放可能把在途回调打断。</summary>
    private void CancelWatchLocked()
    {
        watchCancellation?.Cancel();
        watchCancellation = null;
    }

    private static bool ContainsFamilyMember(IReadOnlyList<string> runningNames, IReadOnlyList<string> names)
    {
        foreach (var name in runningNames)
        {
            if (GameProcessNames.BelongsToFamily(name, names))
            {
                return true;
            }
        }

        return false;
    }
}
