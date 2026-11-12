using Cafe.Launcher.Core.Helpers;
using Cafe.Launcher.Core.Models;

namespace Cafe.Launcher.Core.Services;

/// <summary>
/// 运行时状态判定：把本地安装状态与远程版本门槛折算成驱动壳层分支的单一分类。
/// </summary>
/// <remarks>
/// 判定次序是契约的一部分：本地非 Valid 时保留本地分类（不去猜远端），远程不可用单独成一档，
/// 本地版本缺失视为损坏，最后才按最低/最新版本比较。放在独立类型里，是为了让这条次序
/// 能被快照装配之外的调用方复用，也让测试直接钉住它而不是穿过整个远程加载。
/// </remarks>
internal static class RuntimeStatePolicy
{
    internal static LauncherRuntimeState Resolve(
        LocalInstallationState localGame,
        GameConfigResponse? gameConfig)
    {
        if (localGame.Kind != LocalInstallationStateKind.Valid)
        {
            return localGame.Kind switch
            {
                LocalInstallationStateKind.NotInstalled => LauncherRuntimeState.NotInstalled,
                LocalInstallationStateKind.Corrupted => LauncherRuntimeState.Corrupted,
                LocalInstallationStateKind.IoFailure => LauncherRuntimeState.IoFailure,
                _ => LauncherRuntimeState.IoFailure
            };
        }

        if (gameConfig is null
            || string.IsNullOrWhiteSpace(gameConfig.GameLatestVersion)
            || string.IsNullOrWhiteSpace(gameConfig.GameLowestVersion))
        {
            return LauncherRuntimeState.RemoteUnavailable;
        }

        var localVersion = localGame.GameConfig?.Version;
        if (string.IsNullOrWhiteSpace(localVersion))
        {
            return LauncherRuntimeState.Corrupted;
        }

        if (VersionComparer.Compare(localVersion, gameConfig.GameLowestVersion) == -1)
        {
            return LauncherRuntimeState.BelowLowestVersion;
        }

        return VersionComparer.Compare(localVersion, gameConfig.GameLatestVersion) == -1
            ? LauncherRuntimeState.UpdateAvailable
            : LauncherRuntimeState.Ready;
    }
}
