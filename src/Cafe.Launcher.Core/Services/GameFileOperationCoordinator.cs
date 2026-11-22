using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Core.Constants;
using Cafe.Launcher.Core.Models;

namespace Cafe.Launcher.Core.Services;

/// <summary>进程内所有游戏的文件任务准入与退出屏障；不排队、不取消业务，也不依赖当前显示条目。</summary>
public interface IGameFileOperationCoordinator
{
    /// <summary>Raised outside the ownership lock after admission or final release; observers must not throw.</summary>
    event EventHandler? ActiveOperationChanged;

    /// <summary>
    /// Atomically protects changed game settings through persistence and editor synchronization.
    /// Refuses changes to the active game; global-only writes and changes to other games remain allowed.
    /// </summary>
    bool TryBeginSettingsWrite(IReadOnlyCollection<string> gameIds, out IDisposable? lease);
    /// <summary>立即验证明确游戏身份并尝试取得全局唯一文件任务位；同一游戏也不允许重入。</summary>
    GameFileOperationAdmission TryAcquire(string gameId, GameFileOperationKind kind);

    /// <summary>当前仍持有令牌的任务快照；运行标志或暂停状态不会清除它。</summary>
    GameFileOperationInfo? ActiveOperation { get; }

    /// <summary>是否已原子关闭新任务准入以准备退出。</summary>
    bool IsShuttingDown { get; }

    /// <summary>
    /// 原子关闭新任务准入，并返回当前令牌释放后的完成屏障。活动卸载时返回 false 且不关闭准入；
    /// false 时返回的任务仅描述当前任务结束，不能作为退出许可。重复调用不重复发起业务操作。
    /// </summary>
    bool TryBeginShutdown(out Task whenIdle);

    /// <summary>取消退出准备并重新允许即时准入；不会释放或改变当前任务令牌。</summary>
    void CancelShutdown();
}

/// <summary>线程安全的全局独占任务位；令牌身份而非游戏身份决定谁能释放当前操作。</summary>
internal sealed class GameFileOperationCoordinator : IGameFileOperationCoordinator
{
    private readonly object gate = new();
    private OperationSlot? active;
    private bool isShuttingDown;
    private readonly Dictionary<string, int> settingsWrites = new(StringComparer.Ordinal);
    public event EventHandler? ActiveOperationChanged;

    public GameFileOperationInfo? ActiveOperation
    {
        get
        {
            lock (gate)
            {
                return active?.Operation;
            }
        }
    }

    public bool IsShuttingDown
    {
        get
        {
            lock (gate)
            {
                return isShuttingDown;
            }
        }
    }

    public GameFileOperationAdmission TryAcquire(string gameId, GameFileOperationKind kind)
    {
        if (!LauncherProfiles.TryGetSupportedGame(gameId, out _))
        {
            return new GameFileOperationAdmission(GameFileOperationAdmissionCode.UnknownGame);
        }

        if (kind is not GameFileOperationKind.Download
            and not GameFileOperationKind.Repair
            and not GameFileOperationKind.Uninstall)
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        GameFileOperationAdmission admitted;
        lock (gate)
        {
            if (isShuttingDown)
            {
                return new GameFileOperationAdmission(
                    GameFileOperationAdmissionCode.ShuttingDown,
                    blockingOperation: active?.Operation);
            }

            if (active is not null)
            {
                return new GameFileOperationAdmission(
                    GameFileOperationAdmissionCode.Busy,
                    blockingOperation: active.Operation);
            }

            if (settingsWrites.ContainsKey(gameId))
            {
                return new GameFileOperationAdmission(GameFileOperationAdmissionCode.SettingsChanging);
            }

            var slot = new OperationSlot(new GameFileOperationInfo(Guid.NewGuid(), gameId, kind));
            active = slot;
            admitted = new GameFileOperationAdmission(
                GameFileOperationAdmissionCode.Allowed,
                new OperationLease(slot.Operation, () => Release(slot)));
        }

        NotifyActiveOperationChanged();
        return admitted;
    }

    public bool TryBeginSettingsWrite(IReadOnlyCollection<string> gameIds, out IDisposable? lease)
    {
        ArgumentNullException.ThrowIfNull(gameIds);
        var changed = gameIds.Distinct(StringComparer.Ordinal).ToArray();
        foreach (var gameId in changed)
        {
            _ = LauncherProfiles.GetSupportedGame(gameId);
        }

        lock (gate)
        {
            if (active is not null && changed.Contains(active.Operation.GameId, StringComparer.Ordinal))
            {
                lease = null;
                return false;
            }

            foreach (var gameId in changed)
            {
                settingsWrites.TryGetValue(gameId, out var count);
                settingsWrites[gameId] = count + 1;
            }

            lease = new SettingsWriteLease(() => ReleaseSettingsWrite(changed));
            return true;
        }
    }

    private void ReleaseSettingsWrite(IReadOnlyList<string> gameIds)
    {
        lock (gate)
        {
            foreach (var gameId in gameIds)
            {
                if (settingsWrites[gameId] == 1)
                {
                    settingsWrites.Remove(gameId);
                }
                else
                {
                    settingsWrites[gameId]--;
                }
            }
        }
    }

    public bool TryBeginShutdown(out Task whenIdle)
    {
        lock (gate)
        {
            whenIdle = active?.Completion.Task ?? Task.CompletedTask;
            if (active?.Operation.Kind == GameFileOperationKind.Uninstall)
            {
                return false;
            }

            isShuttingDown = true;
            return true;
        }
    }

    public void CancelShutdown()
    {
        lock (gate)
        {
            isShuttingDown = false;
        }
    }

    private void Release(OperationSlot slot)
    {
        lock (gate)
        {
            if (!ReferenceEquals(active, slot))
            {
                return;
            }

            active = null;
            // Continuations are always queued; completing the barrier cannot run caller code
            // inside the ownership lock or imply that a cancellation request ended the journey.
            slot.Completion.TrySetResult();
        }

        NotifyActiveOperationChanged();
    }

    private void NotifyActiveOperationChanged()
    {
        if (ActiveOperationChanged is not { } observers) return;
        foreach (EventHandler observer in observers.GetInvocationList())
        {
            try
            {
                observer(this, EventArgs.Empty);
            }
            catch (Exception)
            {
                // Observers only refresh presentation. Their failure cannot lose a granted
                // lease or prevent the next owner from acquiring a fully released task slot.
            }
        }
    }

    private sealed class OperationSlot(GameFileOperationInfo operation)
    {
        public GameFileOperationInfo Operation { get; } = operation;
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class OperationLease(GameFileOperationInfo operation, Action release) : IGameFileOperationLease
    {
        private Action? release = release;

        public GameFileOperationInfo Operation { get; } = operation;

        public void Dispose() => Interlocked.Exchange(ref release, null)?.Invoke();
    }

    private sealed class SettingsWriteLease(Action release) : IDisposable
    {
        private Action? release = release;
        public void Dispose() => Interlocked.Exchange(ref release, null)?.Invoke();
    }
}
