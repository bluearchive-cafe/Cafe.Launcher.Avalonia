using System;
using System.Text.Json;
using Cafe.Launcher.Core.Constants;

namespace Cafe.Launcher.Services;

/// <summary>有界本机启动请求；只携带已登记的稳定游戏身份，不接受命令、路径或任意参数。</summary>
internal sealed record CrossProcessGameLaunchRequest
{
    internal const int MaxPayloadBytes = 256;

    internal CrossProcessGameLaunchRequest(string gameId)
    {
        GameId = LauncherProfiles.GetSupportedGame(gameId).RuntimeId;
    }

    internal string GameId { get; }

    internal byte[] Encode() => JsonSerializer.SerializeToUtf8Bytes(new { gameId = GameId });

    internal static bool TryDecode(ReadOnlyMemory<byte> payload, out CrossProcessGameLaunchRequest? request)
    {
        request = null;
        if (payload.IsEmpty || payload.Length > MaxPayloadBytes)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 2 });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            string? gameId = null;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (gameId is not null || property.Name != "gameId" || property.Value.ValueKind != JsonValueKind.String)
                {
                    return false;
                }

                gameId = property.Value.GetString();
            }

            if (!LauncherProfiles.TryGetSupportedGame(gameId, out var profile))
            {
                return false;
            }

            request = new CrossProcessGameLaunchRequest(profile.RuntimeId);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
