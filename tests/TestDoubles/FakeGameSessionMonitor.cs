using System;
using System.Collections.Generic;
using Cafe.Launcher.Avalonia.Features.GameOperations;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services.GameRuntime;

namespace Cafe.Launcher.Avalonia.Testing;

/// <summary>
/// <see cref="IGameSessionMonitor"/> 的共享测试替身，由两个测试工程通过 csproj Link 共用。
/// <see cref="BeginSession"/> 只记录调用并按真实监视器同一规则落初始状态（原生即运行中、
/// 运行器为启动中）；状态推进由测试通过 <see cref="Transition"/> 手工驱动——同一状态不
/// 重复引发 <see cref="StateChanged"/>，与真实实现「离开原值才通知」一致。
/// </summary>
internal sealed class FakeGameSessionMonitor : IGameSessionMonitor
{
    private readonly List<(string RunnerId, IReadOnlyList<string> KnownExeNames)> sessions = [];

    public GameSessionState State { get; private set; } = GameSessionState.Idle;

    public GameLaunchExitInfo? LastSessionExit { get; private set; }

    public int BeginSessionCallCount => sessions.Count;

    /// <summary>最近一次 <see cref="BeginSession"/> 收到的 (运行器, 进程家族)，无则 null。</summary>
    public (string RunnerId, IReadOnlyList<string> KnownExeNames)? LastBeginSession =>
        sessions.Count == 0 ? null : sessions[^1];

    public event Action? StateChanged;

    public void BeginSession(string runnerId, IReadOnlyList<string> knownExeNames)
    {
        sessions.Add((runnerId, knownExeNames));
        State = string.Equals(runnerId, GameRuntimeRunners.Native, StringComparison.OrdinalIgnoreCase)
            ? GameSessionState.Running
            : GameSessionState.Starting;
        LastSessionExit = null;
        StateChanged?.Invoke();
    }

    public void Transition(GameSessionState state, GameLaunchExitInfo? exit = null)
    {
        if (State == state)
        {
            return;
        }

        State = state;
        LastSessionExit = exit;
        StateChanged?.Invoke();
    }
}
