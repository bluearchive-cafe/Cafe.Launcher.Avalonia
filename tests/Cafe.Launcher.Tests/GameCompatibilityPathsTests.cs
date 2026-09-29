using System;
using System.IO;
using Cafe.Launcher.UI.Services.GameRuntime;
using Cafe.Launcher.Core.Services.GameRuntime;
using Cafe.Launcher.Core.Constants;

namespace Cafe.Launcher.Tests;

public sealed class GameCompatibilityPathsTests
{
    [Fact]
    public void GetDefaultPrefixPath_WithDifferentRunners_IsolatesPrefixes()
    {
        var umuPrefix = GameCompatibilityPaths.GetDefaultPrefixPath(LauncherProfiles.BlueArchiveJapan.RuntimeId, "umu");
        var winePrefix = GameCompatibilityPaths.GetDefaultPrefixPath(LauncherProfiles.BlueArchiveJapan.RuntimeId, "wine");

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
        var japanPrefix = GameCompatibilityPaths.GetDefaultPrefixPath(LauncherProfiles.BlueArchiveJapan.RuntimeId, "umu");
        var globalPrefix = GameCompatibilityPaths.GetDefaultPrefixPath("blue-archive-global", "umu");

        Assert.NotEqual(japanPrefix, globalPrefix);
        Assert.EndsWith(
            Path.Combine("compatibility", "blue-archive-global", "umu", "prefix"),
            globalPrefix);
    }
}
