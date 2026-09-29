using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cafe.Launcher.Core.Models;

namespace Cafe.Launcher.Core.Services.Auth;

/// <summary>
/// Builds the MD5-signed authorization header required by the official launcher API.
/// MD5 is mandated by the wire protocol, not a design choice. For request-integrity
/// signing (not password storage or certificate verification) this is acceptable.
/// The server should additionally enforce timeliness via the `time` field.
/// </summary>
/// <remarks>
/// 被签名的两个游戏值（<c>game_tag</c> 与 salt）来自注入的 <see cref="YostarGameProfile"/>：
/// 它们换游戏就换，且写错不会在本地报错、只会让服务端拒绝，因此不由静态常量兜底。
/// </remarks>
internal sealed class AuthorizationHeaderFactory
{
    private readonly YostarGameProfile gameProfile;
    private readonly TimeProvider timeProvider;

    /// <summary>Creates the factory against the system clock (production path).</summary>
    public AuthorizationHeaderFactory(YostarGameProfile gameProfile) : this(gameProfile, TimeProvider.System)
    {
    }

    /// <summary>
    /// Creates the factory against an explicit clock. The `time` field is signed, so a
    /// fixed clock is what makes the signature reproducible in tests.
    /// </summary>
    public AuthorizationHeaderFactory(YostarGameProfile gameProfile, TimeProvider timeProvider)
    {
        this.gameProfile = gameProfile;
        this.timeProvider = timeProvider;
    }

    public string Create(string data, string version)
    {
        var head = new AuthorizationHead
        {
            GameTag = gameProfile.Tag,
            Time = timeProvider.GetUtcNow().ToUnixTimeSeconds(),
            Version = version
        };

        var headJson = JsonSerializer.Serialize(head);
        var signSource = $"{headJson}{data ?? ""}{gameProfile.AuthorizationSalt}";
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
