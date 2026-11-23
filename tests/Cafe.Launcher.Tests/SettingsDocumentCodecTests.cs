using System.Globalization;
using System.Text.Json;
using Cafe.Launcher.Core.Constants;
using Cafe.Launcher.Core.Helpers;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Services;

namespace Cafe.Launcher.Tests;

public sealed class SettingsDocumentCodecTests
{
    [Fact]
    public void Read_LegacyFlatDocument_MigratesAllGameFieldsAndRetainsGlobalPreferences()
    {
        const string json = """
            {
              "gamePath": "D:\\Games\\BA",
              "patchUrlGroup": "cafe",
              "launchCheckMode": "remoteManifest",
              "gameRuntime": { "runner": "wine", "prefixPath": "/prefix/ba", "enableMangoHud": true },
              "afterLaunchBehavior": "keepOpen",
              "resourcePanelUid": "legacy-uid",
              "resourcePanelUidSource": "custom",
              "language": "ja",
              "proxyMode": "direct",
              "themeMode": "dark",
              "enableHttp2": false,
              "backgroundVideoVolume": 35,
              "windowPositionX": -20,
              "themeColorPalette": ["#FF123456"]
            }
            """;

        var result = SettingsDocumentCodec.Read(json);
        var settings = result.Settings;
        var ba = settings.GetGameSettings(LauncherProfiles.BlueArchiveJapan.RuntimeId);
        var stella = settings.GetGameSettings(LauncherProfiles.StellaSoraChina.RuntimeId);

        Assert.True(result.RequiresMigration);
        Assert.Null(result.SourceFormatVersion);
        Assert.Empty(result.DegradedFields);
        Assert.Equal(SettingsFormatVersions.Current, settings.FormatVersion);
        Assert.Equal(LauncherProfiles.BlueArchiveJapan.RuntimeId, settings.LastViewedGameId);
        Assert.Equal(@"D:\Games\BA", ba.GamePath);
        Assert.Equal(PatchUrlGroups.Cafe, ba.PatchUrlGroup);
        Assert.Equal(LaunchCheckModes.RemoteManifest, ba.LaunchCheckMode);
        Assert.Equal(GameRuntimeRunners.Wine, ba.GameRuntime.Runner);
        Assert.Equal("/prefix/ba", ba.GameRuntime.PrefixPath);
        Assert.True(ba.GameRuntime.EnableMangoHud);
        Assert.Equal(AfterLaunchBehaviors.KeepOpen, ba.AfterLaunchBehavior);
        Assert.Equal("legacy-uid", ba.ResourcePanelUid);
        Assert.Equal(ResourcePanelUidSources.Custom, ba.ResourcePanelUidSource);
        Assert.Equal(LauncherLanguages.Japanese, settings.Language);
        Assert.Equal(ProxyModes.Direct, settings.ProxyMode);
        Assert.Equal(ThemeModes.Dark, settings.ThemeMode);
        Assert.False(settings.EnableHttp2);
        Assert.Equal(35, ba.BackgroundVideoVolume);
        Assert.Equal(-20, settings.WindowPositionX);
        Assert.Equal(["#FF123456"], settings.ThemeColorPalette);
        Assert.Equal(PatchUrlGroups.Official, stella.PatchUrlGroup);
        Assert.Empty(stella.GamePath);
        Assert.Empty(stella.ResourcePanelUid);
        Assert.Equal(GameRuntimeRunners.Auto, stella.GameRuntime.Runner);
        Assert.NotSame(ba.GameRuntime, stella.GameRuntime);
    }

    [Fact]
    public void Read_LegacyPascalCaseAliases_UsesAliasesOnlyWhenCamelCaseIsMissing()
    {
        var legacy = SettingsDocumentCodec.Read("""{"GamePath":"legacy","LaunchCheckMode":"RemoteManifest"}""").Settings;
        Assert.Equal("legacy", legacy.GamePath);
        Assert.Equal("RemoteManifest", legacy.LaunchCheckMode);

        var mixed = SettingsDocumentCodec.Read("""
            {"gamePath":"current","GamePath":"legacy","launchCheckMode":"none","LaunchCheckMode":"RemoteManifest"}
            """).Settings;
        Assert.Equal("current", mixed.GamePath);
        Assert.Equal(LaunchCheckModes.None, mixed.LaunchCheckMode);

        var explicitNull = SettingsDocumentCodec.Read("""
            {"gamePath":null,"GamePath":"legacy","launchCheckMode":null,"LaunchCheckMode":"RemoteManifest"}
            """).Settings;
        Assert.Null(explicitNull.GamePath);
        Assert.Null(explicitNull.LaunchCheckMode);
    }

    [Fact]
    public void Read_LegacyChinesePreference_DoesNotPopulateStellaWithCafeOrBlueArchiveSettings()
    {
        var originalCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
            var settings = SettingsDocumentCodec.Read("""{"patchUrlGroup":"cafe","resourcePanelUid":"ba-only"}""").Settings;
            Assert.Equal(PatchUrlGroups.Cafe, settings.PatchUrlGroup);
            var stella = settings.GetGameSettings(LauncherProfiles.StellaSoraChina.RuntimeId);
            Assert.Equal(PatchUrlGroups.Official, stella.PatchUrlGroup);
            Assert.Empty(stella.ResourcePanelUid);
            Assert.Equal(ResourcePanelUidSources.Auto, stella.ResourcePanelUidSource);
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }

    [Fact]
    public void Read_CurrentDocument_KeepsBothGameStatesAndViewedIdentityAcrossRoundTrip()
    {
        const string json = """
            {
              "formatVersion": 3,
              "lastViewedGameId": "stella-sora-cn",
              "language": "zh-Hans",
              "games": {
                "blue-archive-jp": {
                  "gamePath": "D:\\Games\\BA", "patchUrlGroup": "cafe", "resourcePanelUid": "ba-uid",
                  "gameRuntime": {"runner":"wine","prefixPath":"/ba"}
                },
                "stella-sora-cn": {
                  "gamePath": "E:\\Games\\Stella", "patchUrlGroup": "official", "launchCheckMode": "none",
                  "afterLaunchBehavior": "keepOpen", "gameRuntime": {"runner":"umu","prefixPath":"/stella"}
                }
              }
            }
            """;

        var read = SettingsDocumentCodec.Read(json);
        var serialized = SettingsDocumentCodec.Serialize(read.Settings);
        var reread = SettingsDocumentCodec.Read(serialized);

        Assert.False(read.RequiresMigration);
        Assert.False(reread.RequiresMigration);
        Assert.Equal(LauncherProfiles.StellaSoraChina.RuntimeId, reread.Settings.LastViewedGameId);
        Assert.Equal(LauncherLanguages.SimplifiedChinese, reread.Settings.Language);
        Assert.True(read.Settings.HasSameSettingsState(reread.Settings));
        var ba = reread.Settings.GetGameSettings(LauncherProfiles.BlueArchiveJapan.RuntimeId);
        var stella = reread.Settings.GetGameSettings(LauncherProfiles.StellaSoraChina.RuntimeId);
        Assert.Equal(@"D:\Games\BA", ba.GamePath);
        Assert.Equal("ba-uid", ba.ResourcePanelUid);
        Assert.Equal(@"E:\Games\Stella", stella.GamePath);
        Assert.Equal("/stella", stella.GameRuntime.PrefixPath);
        Assert.Equal(AfterLaunchBehaviors.KeepOpen, stella.AfterLaunchBehavior);
        Assert.Equal(LaunchCheckModes.None, stella.LaunchCheckMode);
        stella.GameRuntime.PrefixPath = "/changed";
        Assert.Equal("/ba", ba.GameRuntime.PrefixPath);
        Assert.Equal("/stella", read.Settings.GetGameSettings(LauncherProfiles.StellaSoraChina.RuntimeId).GameRuntime.PrefixPath);
    }

    [Theory]
    [InlineData("4", "unsupported-version", 4)]
    [InlineData("1", "unsupported-version", 1)]
    [InlineData("-1", "unsupported-version", -1)]
    [InlineData("\"2\"", "invalid-version", null)]
    [InlineData("null", "invalid-version", null)]
    [InlineData("true", "invalid-version", null)]
    [InlineData("2.5", "invalid-version", null)]
    [InlineData("2147483648", "invalid-version", null)]
    public void Read_InvalidOrUnsupportedVersion_IsNotRecoverableAsInvalidJson(string version, string code, int? expectedVersion)
    {
        var exception = Assert.Throws<SettingsFormatException>(() => SettingsDocumentCodec.Read("{\"formatVersion\":" + version + "}"));

        Assert.Equal(code, exception.FailureCode);
        Assert.Equal(expectedVersion, exception.FormatVersion);
        Assert.False(StorageFailure.IsRecoverableOrInvalidJson(exception));
    }

    [Theory]
    [InlineData("""{"formatVersion":2}""")]
    [InlineData("""{"formatVersion":2,"games":null}""")]
    [InlineData("""{"formatVersion":2,"games":[]}""")]
    [InlineData("""{"formatVersion":2,"games":{}}""")]
    [InlineData("""{"formatVersion":2,"games":{"blue-archive-jp":{}}}""")]
    [InlineData("""{"formatVersion":2,"games":{"blue-archive-jp":{},"stella-sora-cn":null}}""")]
    [InlineData("""{"formatVersion":2,"games":{"blue-archive-jp":[],"stella-sora-cn":{}}}""")]
    [InlineData("""{"formatVersion":2,"Games":{"blue-archive-jp":{},"stella-sora-cn":{}}}""")]
    public void Read_CurrentDocumentWithIncompleteGameStructure_RejectsInsteadOfAddingDefaults(string json)
    {
        var exception = Assert.Throws<SettingsFormatException>(() => SettingsDocumentCodec.Read(json));
        Assert.Equal("invalid-structure", exception.FailureCode);
        Assert.False(StorageFailure.IsRecoverableOrInvalidJson(exception));
    }

    /// <summary>
    /// 单字段类型错误是既有契约的一部分：只丢掉那一个字段并记入降级列表，其余设置照常读出。
    /// </summary>
    [Theory]
    [InlineData("""{"formatVersion":2,"games":{"blue-archive-jp":{"gamePath":42},"stella-sora-cn":{}}}""", "games.blue-archive-jp.gamePath")]
    [InlineData("""{"formatVersion":2,"games":{"blue-archive-jp":{},"stella-sora-cn":{"gameRuntime":"invalid"}}}""", "games.stella-sora-cn.gameRuntime")]
    [InlineData("""{"formatVersion":2,"enableHttp2":"false","games":{"blue-archive-jp":{},"stella-sora-cn":{}}}""", "enableHttp2")]
    public void Read_CurrentDocumentWithWrongValueTypes_DegradesOnlyTheOffendingField(string json, string degradedField)
    {
        var result = SettingsDocumentCodec.Read(json);

        Assert.Equal([degradedField], result.DegradedFields);
        Assert.Empty(result.Settings.GetGameSettings(LauncherProfiles.BlueArchiveJapan.RuntimeId).GamePath);
        Assert.Equal(new LauncherSettings().EnableHttp2, result.Settings.EnableHttp2);
        Assert.NotNull(result.Settings.GetGameSettings(LauncherProfiles.StellaSoraChina.RuntimeId).GameRuntime);
    }

    [Theory]
    [InlineData("future-game")]
    [InlineData("BLUE-ARCHIVE-JP")]
    public void Read_UnknownGameIdentity_RefusesLossyRewriting(string identity)
    {
        var json = "{\"formatVersion\":2,\"games\":{\"blue-archive-jp\":{},\"stella-sora-cn\":{},\"" + identity + "\":{\"opaqueFutureData\":[1,2,3]}}}";
        var exception = Assert.Throws<SettingsFormatException>(() => SettingsDocumentCodec.Read(json));
        Assert.Equal("unknown-game", exception.FailureCode);
    }

    [Theory]
    [InlineData("""{"games":{"blue-archive-jp":{},"stella-sora-cn":{}}}""")]
    [InlineData("""{"FormatVersion":3}""")]
    public void Read_NestedOrMisCasedVersionWithoutDiscriminator_RejectsLegacyFallback(string json)
    {
        Assert.Equal("missing-version", Assert.Throws<SettingsFormatException>(() => SettingsDocumentCodec.Read(json)).FailureCode);
    }

    [Theory]
    [InlineData("""{"formatVersion":3,"formatVersion":2,"games":{"blue-archive-jp":{},"stella-sora-cn":{}}}""")]
    [InlineData("""{"formatVersion":2,"games":{"blue-archive-jp":{},"blue-archive-jp":{},"stella-sora-cn":{}}}""")]
    public void Read_AmbiguousDuplicateProperties_RejectsHiddenVersionsOrGameData(string json)
    {
        Assert.Equal("duplicate-property", Assert.Throws<SettingsFormatException>(() => SettingsDocumentCodec.Read(json)).FailureCode);
    }

    [Fact]
    public void Read_SyntaxError_RemainsJsonExceptionForTheFileBoundary()
    {
        Assert.ThrowsAny<JsonException>(() => SettingsDocumentCodec.Read("{\"formatVersion\":2,"));
    }

    [Fact]
    public void Serialize_MigratedSettings_OnlyWritesGameFieldsInsideGamesAndLeavesInputUnchanged()
    {
        const string json = """{"gamePath":"legacy","resourcePanelUid":"ba-only","language":"ja"}""";
        var settings = SettingsDocumentCodec.Read(json).Settings;
        var before = settings.DeepClone();
        using var written = JsonDocument.Parse(SettingsDocumentCodec.Serialize(settings));
        var root = written.RootElement;

        Assert.Equal(SettingsFormatVersions.Current, root.GetProperty("formatVersion").GetInt32());
        foreach (var flatField in new[] { "gamePath", "patchUrlGroup", "launchCheckMode", "gameRuntime", "afterLaunchBehavior", "resourcePanelUid", "resourcePanelUidSource", "GamePath", "LaunchCheckMode" })
        {
            Assert.False(root.TryGetProperty(flatField, out _));
        }

        Assert.Equal("legacy", root.GetProperty("games").GetProperty("blue-archive-jp").GetProperty("gamePath").GetString());
        Assert.Equal("official", root.GetProperty("games").GetProperty("stella-sora-cn").GetProperty("patchUrlGroup").GetString());
        Assert.True(before.HasSameSettingsState(settings));
        Assert.Equal("""{"gamePath":"legacy","resourcePanelUid":"ba-only","language":"ja"}""", json);
    }

    [Fact]
    public void Serialize_NonCurrentOrIncompleteModel_RefusesToReplaceTheDocument()
    {
        var settings = new LauncherSettings { FormatVersion = 4 };
        Assert.Equal("unsupported-version", Assert.Throws<SettingsFormatException>(() => SettingsDocumentCodec.Serialize(settings)).FailureCode);
        settings.FormatVersion = SettingsFormatVersions.Current;
        settings.Games.Remove(LauncherProfiles.StellaSoraChina.RuntimeId);
        Assert.Equal("invalid-structure", Assert.Throws<SettingsFormatException>(() => SettingsDocumentCodec.Serialize(settings)).FailureCode);
        settings.Games[LauncherProfiles.StellaSoraChina.RuntimeId] = new GameSettings();
        settings.Games["future-game"] = new GameSettings();
        Assert.Equal("unknown-game", Assert.Throws<SettingsFormatException>(() => SettingsDocumentCodec.Serialize(settings)).FailureCode);
    }
}
