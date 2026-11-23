using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Testing;

namespace Cafe.Launcher.Tests;

public sealed class GameDataPathsTests
{
    [Theory]
    [InlineData("blue-archive-jp")]
    [InlineData("stella-sora-cn")]
    public void Constructor_BuiltInGame_SharesGlobalRootAndLeavesExistingPrefixInPlace(string gameId)
    {
        using var directory = TestDirectory.Create();
        var prefix = Path.Combine(directory, "compatibility", gameId, "prefix");
        Directory.CreateDirectory(prefix);
        var marker = Path.Combine(prefix, "user.reg");
        File.WriteAllText(marker, "existing prefix");

        var paths = new GameDataPaths(directory.DataRoot, gameId);

        Assert.Same(directory.DataRoot, paths.DataRoot);
        Assert.Equal(gameId, paths.GameId);
        Assert.Equal(Path.Combine(directory, "games", gameId), paths.Root);
        Assert.False(Directory.Exists(paths.Root));
        Assert.Equal("existing prefix", File.ReadAllText(marker));
        Assert.Equal(Path.Combine(directory, "settings.json"), paths.DataRoot.SettingsPath);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("unknown-game")]
    [InlineData("BlueArchive_JP")]
    [InlineData("BLUE-ARCHIVE-JP")]
    [InlineData("../blue-archive-jp")]
    [InlineData("blue-archive-jp/../stella-sora-cn")]
    [InlineData("blue-archive-jp\\..\\stella-sora-cn")]
    [InlineData("/blue-archive-jp")]
    [InlineData("C:\\blue-archive-jp")]
    public void Constructor_UnknownOrUnsafeGameId_RejectsWithoutCreatingDirectories(string? gameId)
    {
        using var directory = TestDirectory.Create();

        Assert.ThrowsAny<ArgumentException>(() => new GameDataPaths(directory.DataRoot, gameId!));

        Assert.Empty(Directory.EnumerateFileSystemEntries(directory));
    }
}
