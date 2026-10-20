using System;
using System.IO;
using Cafe.Launcher.Core.Services;

namespace Cafe.Launcher.Core.Services.GameRuntime;

/// <summary>
/// Filesystem layout for launcher-managed compatibility environments. Prefixes
/// live outside the game directory so game manifests, repairs, and compatibility
/// state stay decoupled (see the cross-platform launch design, §10).
/// The root is the injected <see cref="LauncherDataRoot"/> on every platform, so
/// this type no longer resolves a process-level location of its own (ADR-025).
/// </summary>
public sealed class GameCompatibilityPaths
{
    private readonly LauncherDataRoot dataRoot;

    public GameCompatibilityPaths(LauncherDataRoot dataRoot)
    {
        ArgumentNullException.ThrowIfNull(dataRoot);
        this.dataRoot = dataRoot;
    }

    /// <summary>Root of launcher-managed compatibility data.</summary>
    public string GetDefaultCompatibilityRoot() =>
        Path.Combine(dataRoot.Root, LauncherDataRoot.CompatibilityFolderName);

    /// <summary>
    /// 某个游戏在受管兼容根下的整棵子树：&lt;compatibilityRoot&gt;/&lt;gameId&gt;，
    /// 覆盖该游戏所有运行器的默认前缀。彻底清除就是删这一棵（ADR-030）；
    /// 用户自定义到别处的前缀不在其中，因此不会被连带删除。
    /// </summary>
    public string GetDefaultGameCompatibilityRoot(string gameId) =>
        Path.Combine(GetDefaultCompatibilityRoot(), gameId);

    /// <summary>
    /// Default Wine prefix for a game, isolated per runner, e.g.
    /// &lt;dataRoot&gt;/compatibility/&lt;gameId&gt;/&lt;runnerId&gt;/prefix.
    /// UMU and Wine keep separate prefixes so switching runners cannot corrupt the
    /// other environment. Prefixes from the earlier shared layout
    /// (compatibility/&lt;gameId&gt;/prefix) are NOT migrated automatically — moving
    /// compatibility state requires explicit user confirmation, so existing
    /// prefixes stay in place and the new default applies going forward.
    /// </summary>
    public string GetDefaultPrefixPath(string gameId, string runnerId) =>
        Path.Combine(GetDefaultCompatibilityRoot(), gameId, runnerId, "prefix");
}
