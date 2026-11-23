using System;
using Cafe.Launcher.Core.Constants;

namespace Cafe.Launcher.Services;

/// <summary>解析明确游戏目标；旧 bare 启动参数固定映射蔚蓝档案，不取当前显示条目。</summary>
internal static class GameLaunchArguments
{
    internal static CrossProcessGameLaunchRequest? Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var launch = false;
        string? gameId = null;
        for (var index = 0; index < args.Length; index++)
        {
            if (string.Equals(args[index], LauncherConstants.LaunchGameArgument, StringComparison.Ordinal))
            {
                if (launch)
                {
                    throw new ArgumentException("The launch-game argument must occur only once.", nameof(args));
                }

                launch = true;
            }
            else if (string.Equals(args[index], LauncherConstants.GameIdArgument, StringComparison.Ordinal))
            {
                if (gameId is not null || ++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
                {
                    throw new ArgumentException("The game-id argument requires exactly one supported game identity.", nameof(args));
                }

                gameId = LauncherProfiles.GetSupportedGame(args[index]).RuntimeId;
            }
            else if (args[index].StartsWith(LauncherConstants.GameIdArgument, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Use the exact game-id argument followed by a supported identity.", nameof(args));
            }
        }

        if (!launch && gameId is not null)
        {
            throw new ArgumentException("The game-id argument requires launch-game.", nameof(args));
        }

        return launch ? new CrossProcessGameLaunchRequest(gameId ?? LauncherProfiles.BlueArchiveJapan.RuntimeId) : null;
    }
}
