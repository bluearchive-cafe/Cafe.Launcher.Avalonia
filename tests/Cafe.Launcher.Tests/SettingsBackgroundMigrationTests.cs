using System.Text;
using System.Text.Json;
using Cafe.Launcher.Core.Constants;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Services;
using Cafe.Launcher.Testing;

namespace Cafe.Launcher.Tests;

public sealed class SettingsBackgroundMigrationTests
{
    [Theory]
    [InlineData("wallpaper.png")]
    [InlineData("background.webm")]
    [InlineData("wallpapers")]
    public async Task ReadAndSaveAsync_VersionTwoCustomMedia_MigratesBothGamesAndPreservesBothBackups(string path)
    {
        using var directory = TestDirectory.Create();
        var root = directory.DataRoot;
        using var service = new LauncherSettingsService(root);
        var original = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            formatVersion = 2,
            lastViewedGameId = "stella-sora-cn",
            backgroundSource = "custom",
            customBackgroundPath = path,
            backgroundFit = "uniform",
            backgroundVideoVolume = 35,
            backgroundVideoMuted = true,
            games = new Dictionary<string, object>
            {
                ["blue-archive-jp"] = new { },
                ["stella-sora-cn"] = new { }
            }
        }));
        await File.WriteAllBytesAsync(root.SettingsPath, original);
        const string multigameBackup = "original single-game backup";
        await File.WriteAllTextAsync(root.SettingsMigrationBackupPath, multigameBackup);

        var migrated = await service.ReadAsync();

        Assert.Equal(SettingsFormatVersions.Current, migrated.FormatVersion);
        Assert.Equal(original, await File.ReadAllBytesAsync(root.SettingsPath));
        Assert.False(File.Exists(root.SettingsBackgroundMigrationBackupPath));
        foreach (var game in migrated.Games.Values)
        {
            // 迁移副本只作为继承值；独立背景开关保持关闭，全局值仍是唯一默认值。
            Assert.False(game.UseBackgroundOverride);
            Assert.Equal(BackgroundSources.Custom, game.BackgroundSource);
            Assert.Equal(path, game.CustomBackgroundPath);
            Assert.Equal(35, game.BackgroundVideoVolume);
            Assert.True(game.BackgroundVideoMuted);
        }

        await service.SaveAsync(migrated);
        await service.SaveAsync(await service.ReadAsync());

        Assert.Equal(original, await File.ReadAllBytesAsync(root.SettingsBackgroundMigrationBackupPath));
        Assert.Equal(multigameBackup, await File.ReadAllTextAsync(root.SettingsMigrationBackupPath));
        Assert.True(migrated.HasSameSettingsState(await service.ReadAsync()));
        Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
    }

    [Fact]
    public async Task SaveAsync_ConflictingVersionTwoBackup_PreservesBothAndMigrates()
    {
        using var directory = TestDirectory.Create();
        var root = directory.DataRoot;
        using var service = new LauncherSettingsService(root);
        const string original = """{"formatVersion":2,"games":{"blue-archive-jp":{},"stella-sora-cn":{}}}""";
        await File.WriteAllTextAsync(root.SettingsPath, original);
        await File.WriteAllTextAsync(root.SettingsBackgroundMigrationBackupPath, "preserved-backup");
        var settings = await service.ReadAsync();

        await service.SaveAsync(settings);

        Assert.Equal(SettingsFormatVersions.Current, (await service.ReadAsync()).FormatVersion);
        Assert.Equal(original, await File.ReadAllTextAsync(root.SettingsBackgroundMigrationBackupPath + ".1"));
        Assert.Equal("preserved-backup", await File.ReadAllTextAsync(root.SettingsBackgroundMigrationBackupPath));
    }

    [Fact]
    public void Read_VersionTwoRemoteDefault_DoesNotEnableGameOverrides()
    {
        var result = SettingsDocumentCodec.Read("""{"formatVersion":2,"backgroundSource":"remote","games":{"blue-archive-jp":{},"stella-sora-cn":{}}}""");

        Assert.True(result.RequiresMigration);
        Assert.Equal(SettingsFormatVersions.GamesWithBackgrounds, result.SourceFormatVersion);
        Assert.Equal(BackgroundSources.Remote, result.Settings.BackgroundSource);
        Assert.All(result.Settings.Games.Values, game => Assert.False(game.UseBackgroundOverride));
    }

    /// <summary>
    /// 与 Next 的差异：迁移不把全局背景清零，而是保留为「未开启独立背景」时继承的默认值；
    /// 因此两份值不会同时成为真值，现有背景界面无需在此阶段改为按游戏存储。
    /// </summary>
    [Fact]
    public void Read_VersionTwoCustomBackground_KeepsGlobalInheritedDefault()
    {
        var result = SettingsDocumentCodec.Read("""
            {"formatVersion":2,"backgroundSource":"custom","customBackgroundPath":"wallpaper.png","backgroundFit":"uniform","games":{"blue-archive-jp":{},"stella-sora-cn":{}}}
            """);

        Assert.Equal(BackgroundSources.Custom, result.Settings.BackgroundSource);
        Assert.Equal("wallpaper.png", result.Settings.CustomBackgroundPath);
        Assert.Equal(BackgroundFits.Uniform, result.Settings.BackgroundFit);
        Assert.All(result.Settings.Games.Values, game =>
        {
            Assert.False(game.UseBackgroundOverride);
            Assert.Equal(BackgroundSources.Custom, game.BackgroundSource);
        });
    }
}
