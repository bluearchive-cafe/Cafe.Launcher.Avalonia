using System.Text;
using Cafe.Launcher.Services;

namespace Cafe.Launcher.Tests;

public sealed class GameLaunchArgumentsTests
{
    [Fact]
    public void Parse_BareLegacyArgument_AlwaysTargetsBlueArchive()
    {
        var request = GameLaunchArguments.Parse(["--launch-game"]);

        Assert.Equal("blue-archive-jp", request?.GameId);
    }

    [Theory]
    [InlineData("blue-archive-jp")]
    [InlineData("stella-sora-cn")]
    public void Parse_ExplicitGameId_PreservesTheTarget(string gameId)
    {
        var request = GameLaunchArguments.Parse(["--launch-game", "--game-id", gameId]);

        Assert.Equal(gameId, request?.GameId);
    }

    [Theory]
    [InlineData("unknown-game")]
    [InlineData("BlueArchive_JP")]
    [InlineData("../stella-sora-cn")]
    [InlineData("")]
    public void Parse_UnknownTarget_RejectsInsteadOfUsingTheCurrentOrLegacyGame(string gameId)
    {
        Assert.ThrowsAny<ArgumentException>(() => GameLaunchArguments.Parse(["--launch-game", "--game-id", gameId]));
    }

    [Fact]
    public void Parse_MissingOrAmbiguousTargetArguments_Rejects()
    {
        Assert.Throws<ArgumentException>(() => GameLaunchArguments.Parse(["--launch-game", "--game-id"]));
        Assert.Throws<ArgumentException>(() => GameLaunchArguments.Parse(["--game-id", "stella-sora-cn"]));
        Assert.Throws<ArgumentException>(() => GameLaunchArguments.Parse(["--launch-game", "--launch-game"]));
        Assert.Throws<ArgumentException>(() => GameLaunchArguments.Parse(
            ["--launch-game", "--game-id", "blue-archive-jp", "--game-id", "stella-sora-cn"]));
        Assert.Throws<ArgumentException>(() => GameLaunchArguments.Parse(["--launch-game", "--game-id=stella-sora-cn"]));
        Assert.Throws<ArgumentException>(() => GameLaunchArguments.Parse(["--launch-game", "--GAME-ID", "stella-sora-cn"]));
    }

    [Fact]
    public void Parse_WithoutLaunchRequest_ReturnsNoTarget()
    {
        Assert.Null(GameLaunchArguments.Parse([]));
        Assert.Null(GameLaunchArguments.Parse(["--show-hidden-settings"]));
    }

    [Theory]
    [InlineData("blue-archive-jp")]
    [InlineData("stella-sora-cn")]
    public void Request_RoundTrip_UsesOnlyTheExplicitIdentity(string gameId)
    {
        var request = new CrossProcessGameLaunchRequest(gameId);

        Assert.True(CrossProcessGameLaunchRequest.TryDecode(request.Encode(), out var decoded));

        Assert.Equal(gameId, decoded?.GameId);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"gameId\":null}")]
    [InlineData("{\"gameId\":\"unknown\"}")]
    [InlineData("{\"gameId\":\"blue-archive-jp\",\"gameId\":\"stella-sora-cn\"}")]
    [InlineData("{\"gameId\":\"blue-archive-jp\",\"command\":\"anything\"}")]
    [InlineData("{\"GameId\":\"blue-archive-jp\"}")]
    public void Request_MalformedOrExtendedPayload_Rejects(string payload)
    {
        Assert.False(CrossProcessGameLaunchRequest.TryDecode(Encoding.UTF8.GetBytes(payload), out var request));
        Assert.Null(request);
    }

    [Fact]
    public void Request_OversizedPayload_RejectsBeforeParsing()
    {
        var payload = Encoding.UTF8.GetBytes(new string(' ', CrossProcessGameLaunchRequest.MaxPayloadBytes + 1));

        Assert.False(CrossProcessGameLaunchRequest.TryDecode(payload, out var request));
        Assert.Null(request);
    }
}
