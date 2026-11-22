using System.Text;
using System.Text.Json;
using Cafe.Launcher.Core.Constants;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Services;
using Cafe.Launcher.Testing;

namespace Cafe.Launcher.Tests;

public sealed class SettingsMigrationTests
{
    [Fact]
    public async Task SaveAsync_LegacyDocument_BacksUpOriginalBytesOnlyWhenSavingAndRetainsThemAfterLaterWrites()
    {
        using var directory = TestDirectory.Create();
        var root = directory.DataRoot;
        using var service = new LauncherSettingsService(root);
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        var original = encoding.GetPreamble().Concat(encoding.GetBytes("{\r\n  \"GamePath\": \"旧目录\",\r\n  \"language\": \"ja\"\r\n}\r\n")).ToArray();
        await File.WriteAllBytesAsync(root.SettingsPath, original);

        var read = await service.ReadAsync();

        Assert.Equal("旧目录", read.GamePath);
        Assert.Equal(original, await File.ReadAllBytesAsync(root.SettingsPath));
        Assert.False(File.Exists(root.SettingsMigrationBackupPath));
        read.Language = LauncherLanguages.English;
        await service.SaveAsync(read);

        Assert.Equal(original, await File.ReadAllBytesAsync(root.SettingsMigrationBackupPath));
        using (var current = JsonDocument.Parse(await File.ReadAllTextAsync(root.SettingsPath)))
        {
            Assert.Equal(SettingsFormatVersions.Current, current.RootElement.GetProperty("formatVersion").GetInt32());
            Assert.Equal("旧目录", current.RootElement.GetProperty("games").GetProperty("blue-archive-jp").GetProperty("gamePath").GetString());
        }

        read.Language = LauncherLanguages.Japanese;
        await service.SaveAsync(read);
        Assert.Equal(original, await File.ReadAllBytesAsync(root.SettingsMigrationBackupPath));
        Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
    }

    [Fact]
    public async Task SaveAsync_ExistingDifferentBackup_PreservesBothAndMigrates()
    {
        using var directory = TestDirectory.Create();
        var root = directory.DataRoot;
        using var service = new LauncherSettingsService(root);
        var original = Encoding.UTF8.GetBytes("""{"gamePath":"old-game","language":"ja"}""");
        var backup = Encoding.UTF8.GetBytes("""{"gamePath":"older-preserved-game"}""");
        await File.WriteAllBytesAsync(root.SettingsPath, original);
        await File.WriteAllBytesAsync(root.SettingsMigrationBackupPath, backup);
        var edited = await service.ReadAsync();
        edited.Language = LauncherLanguages.English;

        await service.SaveAsync(edited);

        Assert.Equal("old-game", (await service.ReadAsync()).GamePath);
        Assert.Equal(original, await File.ReadAllBytesAsync(root.SettingsMigrationBackupPath + ".1"));
        Assert.Equal(backup, await File.ReadAllBytesAsync(root.SettingsMigrationBackupPath));
        Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
    }

    [Fact]
    public async Task SaveAsync_InterruptedMigrationWithNumberedBackup_ReusesMatchingBackup()
    {
        using var directory = TestDirectory.Create();
        var root = directory.DataRoot;
        using var service = new LauncherSettingsService(root);
        const string original = "{\"gamePath\":\"current\"}";
        await File.WriteAllTextAsync(root.SettingsPath, original);
        await File.WriteAllTextAsync(root.SettingsMigrationBackupPath, "older");
        await File.WriteAllTextAsync(root.SettingsMigrationBackupPath + ".1", original);

        await service.SaveAsync(await service.ReadAsync());

        Assert.Equal(original, await File.ReadAllTextAsync(root.SettingsMigrationBackupPath + ".1"));
        Assert.False(File.Exists(root.SettingsMigrationBackupPath + ".2"));
        Assert.Equal("current", (await service.ReadAsync()).GamePath);
    }

    [Fact]
    public async Task SaveAsync_IdenticalBackupFromInterruptedMigration_AllowsRetryWithoutReplacingBackup()
    {
        using var directory = TestDirectory.Create();
        var root = directory.DataRoot;
        using var service = new LauncherSettingsService(root);
        var original = Encoding.UTF8.GetBytes("{ \"gamePath\": \"recoverable\", \"language\": \"ja\" }\r\n");
        await File.WriteAllBytesAsync(root.SettingsPath, original);
        await File.WriteAllBytesAsync(root.SettingsMigrationBackupPath, original);

        var settings = await service.ReadAsync();
        settings.Language = LauncherLanguages.English;
        await service.SaveAsync(settings);

        Assert.Equal(original, await File.ReadAllBytesAsync(root.SettingsMigrationBackupPath));
        Assert.Equal(LauncherLanguages.English, (await service.ReadAsync()).Language);
        using var current = JsonDocument.Parse(await File.ReadAllTextAsync(root.SettingsPath));
        Assert.Equal(SettingsFormatVersions.Current, current.RootElement.GetProperty("formatVersion").GetInt32());
        Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
    }

    [Fact]
    public async Task SaveAsync_FirstSettingsDocument_WritesCurrentFormatWithoutMigrationBackup()
    {
        using var directory = TestDirectory.Create();
        var root = directory.DataRoot;
        using var service = new LauncherSettingsService(root);

        await service.SaveAsync(new LauncherSettings { Language = LauncherLanguages.Japanese });

        Assert.False(File.Exists(root.SettingsMigrationBackupPath));
        using var current = JsonDocument.Parse(await File.ReadAllTextAsync(root.SettingsPath));
        Assert.Equal(SettingsFormatVersions.Current, current.RootElement.GetProperty("formatVersion").GetInt32());
        Assert.Equal(2, current.RootElement.GetProperty("games").EnumerateObject().Count());
    }

    /// <summary>
    /// 读取契约要求入口不抛异常：无法安全解释的文档读出默认值，原文件只在读时留一份诊断副本。
    /// 但写入必须拒绝覆盖——否则编辑器里的默认值会把未知文档整体冲掉。
    /// </summary>
    [Theory]
    [InlineData("""{"formatVersion":99,"opaqueFutureData":{"keep":[1,2,3]}}""")]
    [InlineData("""{"formatVersion":"2","games":{}}""")]
    [InlineData("""{"formatVersion":2,"games":{"blue-archive-jp":{},"stella-sora-cn":null}}""")]
    [InlineData("""{"formatVersion":2,"games":{"blue-archive-jp":{},"stella-sora-cn":{},"future-game":{"keep":true}}}""")]
    public async Task ReadAndSave_UnsupportedDocument_ReadsDefaultsButRefusesOverwrite(string json)
    {
        using var directory = TestDirectory.Create();
        var root = directory.DataRoot;
        using var service = new LauncherSettingsService(root);
        var original = Encoding.UTF8.GetBytes(json);
        await File.WriteAllBytesAsync(root.SettingsPath, original);

        var defaults = await service.ReadAsync();

        Assert.Equal(LauncherLanguages.Auto, defaults.Language);
        Assert.Equal(original, await File.ReadAllBytesAsync(root.SettingsPath));
        Assert.Equal(original, await File.ReadAllBytesAsync(root.SettingsBackupPath));

        await Assert.ThrowsAsync<SettingsFormatException>(() => service.SaveAsync(new LauncherSettings()));

        Assert.Equal(original, await File.ReadAllBytesAsync(root.SettingsPath));
        Assert.False(File.Exists(root.SettingsMigrationBackupPath));
        Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
    }

    [Fact]
    public async Task SaveAsync_DiskVersionChangedSinceRead_RechecksBeforeWritingAnOlderSnapshot()
    {
        using var directory = TestDirectory.Create();
        var root = directory.DataRoot;
        using var service = new LauncherSettingsService(root);
        await service.SaveAsync(new LauncherSettings { GamePath = "known-path" });
        var snapshot = await service.ReadAsync();
        var future = Encoding.UTF8.GetBytes("""{"formatVersion":100,"futureState":"must-survive"}""");
        await File.WriteAllBytesAsync(root.SettingsPath, future);
        snapshot.WindowPositionX = 200;

        await Assert.ThrowsAsync<SettingsFormatException>(() => service.SaveAsync(snapshot));

        Assert.Equal(future, await File.ReadAllBytesAsync(root.SettingsPath));
        Assert.False(File.Exists(root.SettingsMigrationBackupPath));
    }

    [Fact]
    public async Task ReadAndSave_TruncatedJson_BacksUpOriginalAtReadAndKeepsCorruptCopyOnSave()
    {
        using var directory = TestDirectory.Create();
        var root = directory.DataRoot;
        using var service = new LauncherSettingsService(root);
        var original = Encoding.UTF8.GetBytes("{\"language\":\"ja\",");
        await File.WriteAllBytesAsync(root.SettingsPath, original);

        var defaults = await service.ReadAsync();
        Assert.Equal(LauncherLanguages.Auto, defaults.Language);
        Assert.Equal(original, await File.ReadAllBytesAsync(root.SettingsBackupPath));
        Assert.Equal(original, await File.ReadAllBytesAsync(root.SettingsPath));

        await service.SaveAsync(defaults);

        Assert.Equal(original, await File.ReadAllBytesAsync(root.CorruptSettingsBackupPath));
        Assert.Equal(SettingsDocumentCodec.CurrentFormatVersion, (await service.ReadAsync()).FormatVersion);
        Assert.False(File.Exists(root.SettingsMigrationBackupPath));
    }

    [Fact]
    public async Task SaveAsync_TwoGames_NormalizesEachByItsCapabilitiesWithoutMutatingTheDraft()
    {
        using var directory = TestDirectory.Create();
        using var service = new LauncherSettingsService(directory.DataRoot, isLinuxPlatform: () => true);
        var draft = new LauncherSettings
        {
            LastViewedGameId = LauncherProfiles.StellaSoraChina.RuntimeId,
            GamePath = "ba-path",
            PatchUrlGroup = PatchUrlGroups.Cafe,
            ResourcePanelUid = "  ba-uid  ",
            CustomThemeColor = "#ff123456"
        };
        draft.GameRuntime.Runner = GameRuntimeRunners.Native;
        draft.GameRuntime.PrefixPath = " /prefix/ba ";
        var stella = draft.GetGameSettings(LauncherProfiles.StellaSoraChina.RuntimeId);
        stella.GamePath = "stella-path";
        stella.PatchUrlGroup = PatchUrlGroups.Cafe;
        stella.ResourcePanelUid = "stale-ba-uid";
        stella.ResourcePanelUidSource = ResourcePanelUidSources.Custom;
        stella.LaunchCheckMode = "RemoteManifest";
        stella.AfterLaunchBehavior = "invalid";
        stella.GameRuntime.Runner = GameRuntimeRunners.Wine;
        stella.GameRuntime.RunnerPath = " /usr/bin/wine ";
        stella.GameRuntime.PrefixPath = " /prefix/stella ";
        var before = draft.DeepClone();

        var normalized = await service.SaveAsync(draft);
        var saved = await service.ReadAsync();

        Assert.True(before.HasSameSettingsState(draft));
        Assert.True(normalized.HasSameSettingsState(saved));
        Assert.Equal(LauncherProfiles.StellaSoraChina.RuntimeId, saved.LastViewedGameId);
        Assert.Equal("#FF123456", saved.CustomThemeColor);
        Assert.Equal("ba-path", saved.GamePath);
        Assert.Equal(PatchUrlGroups.Cafe, saved.PatchUrlGroup);
        Assert.Equal("ba-uid", saved.ResourcePanelUid);
        Assert.Equal(GameRuntimeRunners.Auto, saved.GameRuntime.Runner);
        Assert.Equal("/prefix/ba", saved.GameRuntime.PrefixPath);
        var savedStella = saved.GetGameSettings(LauncherProfiles.StellaSoraChina.RuntimeId);
        Assert.Equal("stella-path", savedStella.GamePath);
        Assert.Equal(PatchUrlGroups.Official, savedStella.PatchUrlGroup);
        Assert.Empty(savedStella.ResourcePanelUid);
        Assert.Equal(ResourcePanelUidSources.Auto, savedStella.ResourcePanelUidSource);
        Assert.Equal(LaunchCheckModes.RemoteManifest, savedStella.LaunchCheckMode);
        Assert.Equal(AfterLaunchBehaviors.Minimize, savedStella.AfterLaunchBehavior);
        Assert.Equal(GameRuntimeRunners.Wine, savedStella.GameRuntime.Runner);
        Assert.Equal("/usr/bin/wine", savedStella.GameRuntime.RunnerPath);
        Assert.Equal("/prefix/stella", savedStella.GameRuntime.PrefixPath);
        Assert.NotSame(saved.GameRuntime, savedStella.GameRuntime);
    }
}
