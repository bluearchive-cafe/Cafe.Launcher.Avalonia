using System.IO;
using Cafe.Launcher.Core.Constants;
using Cafe.Launcher.Core.Services.GameRuntime;

namespace Cafe.Launcher.Tests;

public sealed class GameCompatibilityPathsTests
{
    private static GameCompatibilityPaths CreatePaths() =>
        new(TestDataRoot.ForCurrentProcess());

    [Fact]
    public void GetDefaultPrefixPath_WithDifferentRunners_IsolatesPrefixes()
    {
        var paths = CreatePaths();

        var umuPrefix = paths.GetDefaultPrefixPath(LauncherProfiles.BlueArchiveJapan.RuntimeId, "umu");
        var winePrefix = paths.GetDefaultPrefixPath(LauncherProfiles.BlueArchiveJapan.RuntimeId, "wine");

        Assert.EndsWith(
            Path.Combine("compatibility", LauncherProfiles.BlueArchiveJapan.RuntimeId, "umu", "prefix"),
            umuPrefix);
        Assert.EndsWith(
            Path.Combine("compatibility", LauncherProfiles.BlueArchiveJapan.RuntimeId, "wine", "prefix"),
            winePrefix);
        Assert.NotEqual(umuPrefix, winePrefix);
    }

    [Fact]
    public void GetDefaultPrefixPath_WithDifferentGames_KeepsGamesSeparate()
    {
        var paths = CreatePaths();

        var japanPrefix = paths.GetDefaultPrefixPath(LauncherProfiles.BlueArchiveJapan.RuntimeId, "umu");
        var globalPrefix = paths.GetDefaultPrefixPath("blue-archive-global", "umu");

        Assert.NotEqual(japanPrefix, globalPrefix);
        Assert.EndsWith(
            Path.Combine("compatibility", "blue-archive-global", "umu", "prefix"),
            globalPrefix);
    }

    [Fact]
    public void GetDefaultCompatibilityRoot_UsesTheInjectedDataRoot()
    {
        var dataRoot = TestDataRoot.ForDirectory(Path.Combine(Path.GetTempPath(), "cl-compat-test"));
        var paths = new GameCompatibilityPaths(dataRoot);

        Assert.Equal(
            Path.Combine(dataRoot.Root, "compatibility"),
            paths.GetDefaultCompatibilityRoot());
    }
}
