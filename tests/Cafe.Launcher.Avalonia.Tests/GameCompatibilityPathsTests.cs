using System;
using System.IO;
using Cafe.Launcher.Avalonia.Services.GameRuntime;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class GameCompatibilityPathsTests
{
    [Fact]
    public void GetDefaultPrefixPath_IsolatesPrefixesPerRunner()
    {
        var umuPrefix = GameCompatibilityPaths.GetDefaultPrefixPath(GameRuntimeIds.BlueArchiveJapan, "umu");
        var winePrefix = GameCompatibilityPaths.GetDefaultPrefixPath(GameRuntimeIds.BlueArchiveJapan, "wine");

        Assert.EndsWith(
            Path.Combine("compatibility", GameRuntimeIds.BlueArchiveJapan, "umu", "prefix"),
            umuPrefix);
        Assert.EndsWith(
            Path.Combine("compatibility", GameRuntimeIds.BlueArchiveJapan, "wine", "prefix"),
            winePrefix);
        Assert.NotEqual(umuPrefix, winePrefix);
    }

    [Fact]
    public void GetDefaultPrefixPath_KeepsGamesSeparate()
    {
        var japanPrefix = GameCompatibilityPaths.GetDefaultPrefixPath(GameRuntimeIds.BlueArchiveJapan, "umu");
        var globalPrefix = GameCompatibilityPaths.GetDefaultPrefixPath("blue-archive-global", "umu");

        Assert.NotEqual(japanPrefix, globalPrefix);
        Assert.EndsWith(
            Path.Combine("compatibility", "blue-archive-global", "umu", "prefix"),
            globalPrefix);
    }
}
