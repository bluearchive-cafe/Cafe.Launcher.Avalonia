using Cafe.Launcher.Core.Constants;

namespace Cafe.Launcher.Tests;

public sealed class LauncherProfilesTests
{
    [Theory]
    [InlineData("blue-archive-jp", "BlueArchive_JP", true)]
    [InlineData("stella-sora-cn", "StellaSora_CN", false)]
    public void GetSupportedGame_KnownIdentity_UsesDeclaredProfileAndCapabilities(string gameId, string tag, bool cafeFeatures)
    {
        var game = LauncherProfiles.GetSupportedGame(gameId);
        var definition = LauncherProfiles.GetGameDefinition(gameId);

        Assert.Equal(tag, game.Tag);
        Assert.Same(game, definition.Profile);
        Assert.Equal(cafeFeatures, definition.SupportsCafeDownloadSource);
        Assert.Equal(cafeFeatures, definition.SupportsResourcePanel);
        Assert.Equal(gameId, definition.GameId);
    }

    [Theory]
    [InlineData("BlueArchive_JP")]
    [InlineData("BLUE-ARCHIVE-JP")]
    [InlineData("unknown")]
    [InlineData("../blue-archive-jp")]
    public void GetSupportedGame_UnknownIdentity_RejectsWithoutFallback(string gameId)
    {
        Assert.False(LauncherProfiles.TryGetSupportedGame(gameId, out var profile));
        Assert.Null(profile);
        Assert.Throws<ArgumentException>(() => LauncherProfiles.GetSupportedGame(gameId));
    }

    [Fact]
    public void DeviceOptionSegments_BlueArchivePointsAtTheGameProfile()
    {
        Assert.Equal(
            new[] { "AppData", "LocalLow", "YostarJP", "BlueArchive", "DeviceOption" },
            LauncherProfiles.BlueArchiveJapan.DeviceOptionRelativeSegments);
        Assert.Empty(LauncherProfiles.StellaSoraChina.DeviceOptionRelativeSegments);
    }
}
