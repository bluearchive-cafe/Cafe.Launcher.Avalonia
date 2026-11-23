using Cafe.Launcher.Core.Constants;
using Cafe.Launcher.Core.Helpers;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Services;
using Cafe.Launcher.Testing;

namespace Cafe.Launcher.Tests;

public sealed class GameInstallationPathSafetyTests
{
    [Theory]
    [InlineData("blue-archive-jp", "stella-sora-cn", "same")]
    [InlineData("blue-archive-jp", "stella-sora-cn", "parent")]
    [InlineData("blue-archive-jp", "stella-sora-cn", "child")]
    [InlineData("stella-sora-cn", "blue-archive-jp", "same")]
    [InlineData("stella-sora-cn", "blue-archive-jp", "parent")]
    [InlineData("stella-sora-cn", "blue-archive-jp", "child")]
    public void FindConflict_EqualOrNestedDirectories_ReportsBothFixedOwners(string targetId, string otherId, string relation)
    {
        using var directory = TestDirectory.Create();
        var targetPaths = new GameInstallationPath(LauncherProfiles.GetSupportedGame(targetId));
        var otherPaths = new GameInstallationPath(LauncherProfiles.GetSupportedGame(otherId));
        var outer = otherPaths.NormalizeGamePath(directory.Sub("outer"));
        var path = targetPaths.NormalizeGamePath(relation == "parent" ? outer : directory.Sub("game"));
        var other = relation switch
        {
            "parent" => outer,
            "child" => otherPaths.NormalizeGamePath(path),
            _ => path + Path.DirectorySeparatorChar
        };
        var settings = new LauncherSettings();
        settings.GetGameSettings(otherId).GamePath = other;

        var conflict = GameInstallationPathSafety.FindConflict(targetId, path, settings);

        Assert.NotNull(conflict);
        Assert.Equal(targetId, conflict.GameId);
        Assert.Equal(otherId, conflict.OtherGameId);
        Assert.Equal(path, conflict.GamePath);
        Assert.Equal(otherPaths.NormalizeGamePath(other), conflict.OtherGamePath);
    }

    [Fact]
    public void FindConflict_LegacyBaseDirectory_ReservesOnlyItsActualGameRoot()
    {
        using var directory = TestDirectory.Create();
        var settings = new LauncherSettings { GamePath = directory.Path };
        var stella = new GameInstallationPath(LauncherProfiles.StellaSoraChina).NormalizeGamePath(directory.Path);
        Assert.Null(GameInstallationPathSafety.FindConflict(LauncherProfiles.StellaSoraChina.RuntimeId, stella, settings));
    }

    [Fact]
    public void FindConflict_EmptyOtherPathAndOwnPath_AreNotDefaultDirectoriesOrConflicts()
    {
        using var directory = TestDirectory.Create();
        var settings = new LauncherSettings { GamePath = directory.Path };
        Assert.Null(GameInstallationPathSafety.FindConflict(LauncherProfiles.BlueArchiveJapan.RuntimeId, directory.Path, settings));
        settings.GetGameSettings(LauncherProfiles.StellaSoraChina.RuntimeId).GamePath = "  ";
        Assert.Null(GameInstallationPathSafety.FindConflict(LauncherProfiles.BlueArchiveJapan.RuntimeId, directory.Path, settings));
    }

    [Fact]
    public void FindConflict_SiblingWithSharedTextPrefix_IsNotNested()
    {
        using var directory = TestDirectory.Create();
        var settings = new LauncherSettings { GamePath = directory.Sub("game-other") };
        Assert.Null(GameInstallationPathSafety.FindConflict(LauncherProfiles.StellaSoraChina.RuntimeId, directory.Sub("game"), settings));
    }
}
