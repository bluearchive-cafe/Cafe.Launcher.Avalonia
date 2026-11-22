using System;
using System.Collections.Generic;
using Cafe.Launcher.Core.Constants;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Services.GameRuntime;

namespace Cafe.Launcher.Core.Services;

/// <summary>在整个启动器会话中按固定游戏身份复用上下文；查看条目切换不改变上下文所有权。</summary>
public interface IGameContextRegistry
{
    /// <summary>取得内置游戏的长期上下文；首次请求才创建，未知身份拒绝且不回退其他游戏。</summary>
    LauncherGameContext GetOrCreate(string gameId);
}

/// <summary>拥有单一数据根和显式工厂的游戏上下文登记表，不创建嵌套依赖容器。</summary>
internal sealed class GameContextRegistry : IGameContextRegistry, IDisposable
{
    private readonly object gate = new();
    private readonly LauncherDataRoot dataRoot;
    private readonly Func<YostarGameProfile, GameDataPaths, LauncherGameContext> factory;
    private readonly Dictionary<string, LauncherGameContext> contexts = new(StringComparer.Ordinal);
    private readonly List<LauncherGameContext> creationOrder = [];
    private readonly HashSet<IGameProcessTracker> processTrackers = new(ReferenceEqualityComparer.Instance);
    private bool disposed;

    /// <summary>工厂只接收固定档案和同一根派生的游戏路径；成功的创建结果由本登记表拥有。</summary>
    public GameContextRegistry(
        LauncherDataRoot dataRoot,
        Func<YostarGameProfile, GameDataPaths, LauncherGameContext> factory)
    {
        ArgumentNullException.ThrowIfNull(dataRoot);
        ArgumentNullException.ThrowIfNull(factory);
        this.dataRoot = dataRoot;
        this.factory = factory;
    }

    public LauncherGameContext GetOrCreate(string gameId)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var profile = LauncherProfiles.GetSupportedGame(gameId);
            if (contexts.TryGetValue(gameId, out var existing))
            {
                return existing;
            }

            // 在同一锁内完成构造与登记：并发同 ID 不会各自产生带订阅或诊断文件的临时上下文。
            // 工厂异常时没有缓存条目，之后可以重试。
            var paths = new GameDataPaths(dataRoot, gameId);
            var context = factory(profile, paths)
                ?? throw new InvalidOperationException("The game context factory returned no context.");
            if (creationOrder.Contains(context))
            {
                throw new InvalidOperationException("The factory reused another game's existing context.");
            }

            if (!string.Equals(context.GameId, gameId, StringComparison.Ordinal)
                || !ReferenceEquals(context.Paths.DataRoot, dataRoot))
            {
                context.Dispose();
                throw new InvalidOperationException("A game context must keep its identity and data root.");
            }

            if (processTrackers.Contains(context.ProcessTracker))
            {
                context.DisposePreservingProcessTracker();
                throw new InvalidOperationException("Each game context must have an exclusive process tracker.");
            }

            processTrackers.Add(context.ProcessTracker);
            contexts.Add(gameId, context);
            creationOrder.Add(context);
            return context;
        }
    }

    /// <summary>按创建的逆序释放全部上下文，并拒绝后续请求；单个失败不跳过其余上下文。</summary>
    public void Dispose()
    {
        LauncherGameContext[] owned;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            owned = creationOrder.ToArray();
            creationOrder.Clear();
            contexts.Clear();
            processTrackers.Clear();
        }

        List<Exception>? failures = null;
        for (var index = owned.Length - 1; index >= 0; index--)
        {
            try
            {
                owned[index].Dispose();
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        if (failures is not null)
        {
            throw new AggregateException("Game context registry disposal failed.", failures);
        }
    }
}
