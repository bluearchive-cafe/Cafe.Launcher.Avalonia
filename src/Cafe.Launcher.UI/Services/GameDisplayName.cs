using System;
using Cafe.Launcher.Core.Constants;
using Cafe.Launcher.UI.Constants;

namespace Cafe.Launcher.UI.Services;

/// <summary>稳定游戏身份对应的本地化名称；不以名称解析任务目标。</summary>
internal static class GameDisplayName
{
    /// <summary>取得支持条目的名称；未知身份明确拒绝。</summary>
    public static string Resolve(string gameId, LocalizationService localizer)
    {
        if (string.Equals(gameId, LauncherProfiles.BlueArchiveJapan.RuntimeId, StringComparison.Ordinal))
        {
            return localizer.T(LocalizationKeys.BlueArchiveJapan);
        }

        if (string.Equals(gameId, LauncherProfiles.StellaSoraChina.RuntimeId, StringComparison.Ordinal))
        {
            return localizer.T(LocalizationKeys.StellaSoraChina);
        }

        throw new ArgumentException("Unsupported game identity.", nameof(gameId));
    }
}
