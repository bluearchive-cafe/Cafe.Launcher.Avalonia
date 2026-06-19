using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cafe.Launcher.Avalonia.Constants;

namespace Cafe.Launcher.Avalonia.Services.Auth;

public sealed class AuthorizationHeaderFactory
{
    public string Create(string data, string version)
    {
        var head = new AuthorizationHead
        {
            GameTag = GamePaths.GameTag,
            Time = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Version = version
        };

        var headJson = JsonSerializer.Serialize(head);
        var signSource = $"{headJson}{data ?? ""}{ApiConfig.AuthorizationSalt}";
        var sign = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(signSource))).ToLowerInvariant();

        return JsonSerializer.Serialize(new AuthorizationHeader
        {
            Head = head,
            Sign = sign
        });
    }

    private sealed class AuthorizationHeader
    {
        [JsonPropertyName("head")]
        public AuthorizationHead Head { get; set; } = new();

        [JsonPropertyName("sign")]
        public string Sign { get; set; } = "";
    }

    private sealed class AuthorizationHead
    {
        [JsonPropertyName("game_tag")]
        public string GameTag { get; set; } = "";

        [JsonPropertyName("time")]
        public long Time { get; set; }

        [JsonPropertyName("version")]
        public string Version { get; set; } = "";
    }
}
