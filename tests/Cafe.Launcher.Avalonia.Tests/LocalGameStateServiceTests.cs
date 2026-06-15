using System.Text.Json;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class LocalGameStateServiceTests : IDisposable
{
    private readonly string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private readonly string gamePath;

    public LocalGameStateServiceTests()
    {
        gamePath = Path.Combine(tempDir, "YostarGames", "BlueArchive_JP");
        Directory.CreateDirectory(gamePath);
    }

    [Fact]
    public async Task ReadAsync_WhenManifestFileVcIsInvalid_FiltersThatFile()
    {
        var validFile = new ManifestFile { Path = "valid.bin", Size = "4", Hash = "1234" };
        validFile.Vc = OfficialHashService.GetManifestFileHash(validFile);
        var invalidFile = new ManifestFile { Path = "invalid.bin", Size = "5", Hash = "5678", Vc = "invalid" };
        var manifest = CreateValidManifest([validFile, invalidFile]);
        await File.WriteAllTextAsync(Path.Combine(gamePath, "manifest.json"), JsonSerializer.Serialize(manifest));
        var service = new LocalGameStateService();

        var state = await service.ReadAsync(gamePath);

        Assert.NotNull(state.Manifest);
        var file = Assert.Single(state.Manifest.Files);
        Assert.Equal("valid.bin", file.Path);
    }

    [Fact]
    public async Task ReadAsync_WhenManifestInfoVcIsInvalid_ClearsManifestInfo()
    {
        var file = new ManifestFile { Path = "valid.bin", Size = "4", Hash = "1234" };
        file.Vc = OfficialHashService.GetManifestFileHash(file);
        var manifest = CreateValidManifest([file]);
        manifest.Vc = "invalid";
        await File.WriteAllTextAsync(Path.Combine(gamePath, "manifest.json"), JsonSerializer.Serialize(manifest));
        var service = new LocalGameStateService();

        var state = await service.ReadAsync(gamePath);

        Assert.NotNull(state.Manifest);
        Assert.Equal("", state.Manifest.Name);
        Assert.Equal("", state.Manifest.Version);
        Assert.Equal("", state.Manifest.Basis);
        Assert.Single(state.Manifest.Files);
    }

    [Fact]
    public async Task ReadAsync_WhenGameConfigVcIsInvalid_ClearsGameConfig()
    {
        var config = new GameLauncherConfig
        {
            Tag = "BlueArchive_JP",
            Name = "BlueArchive",
            Params = ["--test"],
            Version = "1.0.0",
            Vc = "invalid"
        };
        await File.WriteAllTextAsync(Path.Combine(gamePath, "game-launcher-config.json"), JsonSerializer.Serialize(config));
        var service = new LocalGameStateService();

        var state = await service.ReadAsync(gamePath);

        Assert.Null(state.GameConfig);
    }

    private static LocalManifest CreateValidManifest(List<ManifestFile> files)
    {
        var manifest = new LocalManifest
        {
            Name = "BlueArchive_JP",
            Version = "1.0.0",
            Basis = "manifest.json",
            Files = files
        };
        manifest.Vc = OfficialHashService.GetManifestInfoHash(
            manifest.Name,
            manifest.Version,
            manifest.Basis);
        return manifest;
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
