using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

/// <summary>
/// Guards byte-for-byte vc compatibility with the official launcher. The expected values
/// below are taken verbatim from a manifest.json / game-launcher-config.json produced by the
/// official BlueArchive_JP launcher (v1.7.2). If these break, the official and rewritten
/// launchers will reject each other's local state as corrupted when sharing a game directory.
/// </summary>
public sealed class OfficialHashServiceTests
{
    [Fact]
    public void GetManifestFileHash_MatchesOfficialLauncherValue()
    {
        // Official key order is path, hash, size -> vc = MD5("path;hash;size").Base64
        var file = new ManifestFile
        {
            Path = "/BlueArchive.exe",
            Hash = "3728022668935248752",
            Size = "653824"
        };

        Assert.Equal("Y9wcFnJEDjSOmyEb+MZbVg==", OfficialHashService.GetManifestFileHash(file));

        file.Vc = OfficialHashService.GetManifestFileHash(file);
        Assert.True(OfficialHashService.IsManifestFileHashValid(file));
    }

    [Fact]
    public void GetManifestInfoHash_MatchesOfficialLauncherValue()
    {
        var hash = OfficialHashService.GetManifestInfoHash(
            "BlueArchive_JP",
            "1.70.0",
            "prod/ZIP_TEMP/BlueArchive_JP_TEMP/BlueArchive_JP-1.70.436321-game.zip");

        Assert.Equal("Atlr5dlO+GQmTpjmHGGGLQ==", hash);
    }

    [Fact]
    public void GetGameConfigHash_MatchesOfficialLauncherValue()
    {
        var config = new GameLauncherConfig
        {
            Tag = "BlueArchive_JP",
            Name = "xldr_BlueArchiveOnline_JP_loader_x64",
            Params = ["BlueArchive.exe"],
            Version = "1.70.0"
        };

        Assert.Equal("jeQcbtiEIHEKA2k6s2fw5A==", OfficialHashService.GetGameConfigHash(config));
        Assert.True(OfficialHashService.IsGameConfigHashValid(
            new GameLauncherConfig
            {
                Tag = config.Tag,
                Name = config.Name,
                Params = config.Params,
                Version = config.Version,
                Vc = "jeQcbtiEIHEKA2k6s2fw5A=="
            }));
    }
}
