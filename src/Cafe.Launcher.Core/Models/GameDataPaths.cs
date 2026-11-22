using System;
using System.IO;
using Cafe.Launcher.Core.Constants;
using Cafe.Launcher.Core.Services;

namespace Cafe.Launcher.Core.Models;

/// <summary>
/// 同一个启动器数据根下，内置游戏的私有状态路径。只描述 <c>games/&lt;gameId&gt;</c>，
/// 不创建目录、不迁移旧状态，也不改变 <c>compatibility/&lt;gameId&gt;</c> 中的兼容前缀。
/// </summary>
public sealed class GameDataPaths
{
    /// <summary>使用已登记游戏的稳定运行时 ID 派生私有状态路径；未知 ID 不作为目录名接受。</summary>
    public GameDataPaths(LauncherDataRoot dataRoot, string gameId)
    {
        ArgumentNullException.ThrowIfNull(dataRoot);
        var game = LauncherProfiles.GetSupportedGame(gameId);
        DataRoot = dataRoot;
        GameId = game.RuntimeId;
        Root = Path.Combine(dataRoot.Root, "games", GameId);
    }

    /// <summary>所有游戏共用的原始启动器数据根实例。</summary>
    public LauncherDataRoot DataRoot { get; }

    /// <summary>内置游戏的稳定运行时 ID。</summary>
    public string GameId { get; }

    /// <summary>此游戏的私有状态目录；不是另一个启动器数据根。</summary>
    public string Root { get; }

    /// <summary>此游戏可续传下载的检查点。</summary>
    public string DownloadStatePath => Path.Combine(Root, LauncherPaths.DownloadStateFileName);

    /// <summary>此游戏旧单游戏检查点的导入完成标记；清除活动检查点时保留。</summary>
    public string DownloadMigrationMarkerPath => Path.Combine(Root, LauncherPaths.DownloadMigrationMarkerFileName);

    /// <summary>此游戏已读公告的指纹集合。</summary>
    public string NoticeStatePath => Path.Combine(Root, LauncherPaths.NoticeStateFileName);

    /// <summary>此游戏最近一次运行器的有界 stdout/stderr 捕获。</summary>
    public string RunnerOutputPath => Path.Combine(Root, LauncherPaths.RunnerOutputFileName);

    /// <summary>此游戏最近一次兼容前缀环境预检报告。</summary>
    public string CompatibilityEnvironmentPath => Path.Combine(Root, LauncherPaths.CompatibilityEnvironmentFileName);

    /// <summary>此游戏最近一次启动所用兼容前缀的元数据。</summary>
    public string PrefixMetadataPath => Path.Combine(Root, LauncherPaths.PrefixMetadataFileName);
}
