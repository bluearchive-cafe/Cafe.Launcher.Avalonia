using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Services.Auth;

namespace Cafe.Launcher.Avalonia.Tests;

/// <summary>
/// Locks the authorization header to the official launcher's algorithm: the signed head is
/// <c>{ game_tag, time, version }</c> in that declaration order, and
/// <c>sign = hex(MD5(headJson + data + salt))</c>. The official launcher fills
/// <c>version</c> with its own running version; this project pins it to
/// <see cref="ApiConfig.YostarAuthorizationVersion"/>, so a version change is a wire change
/// rather than a cosmetic one.
/// </summary>
public sealed class AuthorizationHeaderFactoryTests
{
    /// <summary>Fixed clock so the signed <c>time</c> field is reproducible.</summary>
    private const long FixedUnixTimeSeconds = 1757600000;

    private static readonly DateTimeOffset FixedNow =
        DateTimeOffset.FromUnixTimeSeconds(FixedUnixTimeSeconds);

    [Fact]
    public void Create_WhenAnyRequestIsSigned_MatchIndependentlyComputedOfficialSignature()
    {
        // Expected values were computed outside this implementation (openssl/Python hashlib)
        // from the documented official inputs, so this pins the algorithm rather than restating it.
        var factory = new AuthorizationHeaderFactory(new FixedTimeProvider());

        var header = factory.Create("", ApiConfig.YostarAuthorizationVersion);

        Assert.Equal(
            "8a7d2fffaeb1799e29cc3401a6b7c9f0",
            ReadSign(header));
        Assert.Equal(
            """{"game_tag":"BlueArchive_JP","time":1757600000,"version":"1.7.2"}""",
            ReadHeadJson(header));
    }

    [Fact]
    public void Create_WhenABodyIsSigned_AppendsDataBetweenHeadAndSalt()
    {
        var factory = new AuthorizationHeaderFactory(new FixedTimeProvider());

        var header = factory.Create("""{"game_tag":"BlueArchive_JP"}""", ApiConfig.YostarAuthorizationVersion);

        Assert.Equal(
            "f01780eb5b92c010599d7e0ab8c3aed2",
            ReadSign(header));
    }

    [Fact]
    public void Create_WhenHeaderIsBuilt_WritesOfficialHeadKeyOrderAndGameTag()
    {
        // The official client serializes { game_tag, time, version } in this order and the
        // server recomputes the signature over that exact text, so property order is a contract.
        var factory = new AuthorizationHeaderFactory(new FixedTimeProvider());

        using var document = JsonDocument.Parse(factory.Create("", ApiConfig.YostarAuthorizationVersion));
        var head = document.RootElement.GetProperty("head");

        Assert.Equal(
            new[] { "game_tag", "time", "version" },
            head.EnumerateObject().Select(property => property.Name));
        Assert.Equal(GamePaths.GameTag, head.GetProperty("game_tag").GetString());
        Assert.Equal(FixedUnixTimeSeconds, head.GetProperty("time").GetInt64());
        Assert.Equal(
            new[] { "head", "sign" },
            document.RootElement.EnumerateObject().Select(property => property.Name));
    }

    [Fact]
    public void Create_WhenOnlyTheVersionChanges_ChangesTheSignedHeadAndSignature()
    {
        // The version participates in the signature, so bumping the pinned constant is a wire
        // change the server can see. The clock is fixed, so the version is the only input that
        // differs between these two calls.
        var factory = new AuthorizationHeaderFactory(new FixedTimeProvider());

        var pinned = factory.Create("", ApiConfig.YostarAuthorizationVersion);
        var bumped = factory.Create("", "1.7.3");

        Assert.Equal(ApiConfig.YostarAuthorizationVersion, ReadVersion(pinned));
        Assert.Equal("1.7.3", ReadVersion(bumped));
        Assert.NotEqual(ReadSign(pinned), ReadSign(bumped));
        Assert.Equal("e639271e4466c89a79de4d83895a4dd1", ReadSign(bumped));
    }

    [Fact]
    public void Create_WhenTheVersionFieldIsTamperedWith_InvalidatesTheSignature()
    {
        // Verification-side view of the same coupling: swapping the version inside an already
        // signed head makes the signature no longer reproduce.
        var factory = new AuthorizationHeaderFactory(new FixedTimeProvider());
        var header = factory.Create("", ApiConfig.YostarAuthorizationVersion);
        var headJson = ReadHeadJson(header);

        var tamperedHead = headJson.Replace(
            ApiConfig.YostarAuthorizationVersion,
            "1.7.3",
            StringComparison.Ordinal);
        var tamperedSign = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(
            $"{tamperedHead}{ApiConfig.AuthorizationSalt}"))).ToLowerInvariant();

        Assert.NotEqual(ReadSign(header), tamperedSign);
    }

    private static string ReadHeadJson(string header)
    {
        using var document = JsonDocument.Parse(header);
        return document.RootElement.GetProperty("head").GetRawText();
    }

    private static string ReadSign(string header)
    {
        using var document = JsonDocument.Parse(header);
        return document.RootElement.GetProperty("sign").GetString()!;
    }

    private static string? ReadVersion(string header)
    {
        using var document = JsonDocument.Parse(header);
        return document.RootElement.GetProperty("head").GetProperty("version").GetString();
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => FixedNow;
    }
}
