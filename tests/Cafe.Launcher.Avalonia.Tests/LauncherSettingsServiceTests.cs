using System.Text.Json;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class LauncherSettingsServiceTests : IDisposable
{
    [Fact]
    public void LauncherSettings_Serialize_LeavesExistingPropertyOrderUnchanged()
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new LauncherSettings()));
        var propertyNames = document.RootElement.EnumerateObject().Select(property => property.Name).ToList();

        Assert.True(propertyNames.IndexOf("updateChannel") < propertyNames.IndexOf("logLevel"));
        Assert.True(propertyNames.IndexOf("resourcePanelUidSource") < propertyNames.IndexOf("statusDetailMode"));
        Assert.Equal("statusDetailMode", propertyNames[^1]);
    }

    [Fact]
    public void LauncherSettings_DefaultMotionModeIsSystem()
    {
        Assert.Equal(MotionModes.System, new LauncherSettings().MotionMode);
    }

    [Fact]
    public void LauncherSettings_DefaultStatusDetailModeIsCompact()
    {
        Assert.Equal(StatusDetailModes.Compact, new LauncherSettings().StatusDetailMode);
    }

    [Fact]
    public void LauncherSettings_DynamicColorFields_DefaultToSpecValues()
    {
        var settings = new LauncherSettings();

        Assert.Equal(ThemeColorExtractionAlgorithms.CelebiScore, settings.ThemeColorExtractionAlgorithm);
        Assert.Equal(ThemeColorVariants.TonalSpot, settings.ThemeColorVariant);
        Assert.Equal(NeutralColorStrategies.BrandBlue, settings.NeutralColorStrategy);
    }

    [Fact]
    public async Task DynamicColorFields_RoundTripAndInvalidValuesFallbackToDefaults()
    {
        var service = new LauncherSettingsService(settingsPath);
        var settings = new LauncherSettings
        {
            ThemeColorExtractionAlgorithm = ThemeColorExtractionAlgorithms.Wu,
            ThemeColorVariant = ThemeColorVariants.Expressive,
            NeutralColorStrategy = NeutralColorStrategies.SeedFollowing
        };

        await service.SaveAsync(settings);
        var readBack = await service.ReadAsync();
        Assert.Equal(ThemeColorExtractionAlgorithms.Wu, readBack.ThemeColorExtractionAlgorithm);
        Assert.Equal(ThemeColorVariants.Expressive, readBack.ThemeColorVariant);
        Assert.Equal(NeutralColorStrategies.SeedFollowing, readBack.NeutralColorStrategy);

        await File.WriteAllTextAsync(
            settingsPath,
            """{"themeColorExtractionAlgorithm":"bogus","themeColorVariant":"bogus","neutralColorStrategy":"bogus"}""");
        var normalized = await service.ReadAsync();
        Assert.Equal(ThemeColorExtractionAlgorithms.CelebiScore, normalized.ThemeColorExtractionAlgorithm);
        Assert.Equal(ThemeColorVariants.TonalSpot, normalized.ThemeColorVariant);
        Assert.Equal(NeutralColorStrategies.BrandBlue, normalized.NeutralColorStrategy);
    }

    [Fact]
    public async Task ReadAsync_WhenOldJsonMissingDynamicColorFields_AppliesDefaults()
    {
        await File.WriteAllTextAsync(settingsPath, """{"language":"ja"}""");

        var settings = await new LauncherSettingsService(settingsPath).ReadAsync();

        Assert.Equal(ThemeColorExtractionAlgorithms.CelebiScore, settings.ThemeColorExtractionAlgorithm);
        Assert.Equal(ThemeColorVariants.TonalSpot, settings.ThemeColorVariant);
        Assert.Equal(NeutralColorStrategies.BrandBlue, settings.NeutralColorStrategy);
    }

    [Fact]
    public async Task MotionMode_RoundTripsAndInvalidValueFallsBackToSystem()
    {
        var service = new LauncherSettingsService(settingsPath);
        await service.SaveAsync(new LauncherSettings { MotionMode = MotionModes.Reduced });
        Assert.Equal(MotionModes.Reduced, (await service.ReadAsync()).MotionMode);

        await File.WriteAllTextAsync(settingsPath, """{"motionMode":"invalid"}""");
        Assert.Equal(MotionModes.System, (await service.ReadAsync()).MotionMode);
    }

    [Fact]
    public async Task Language_RoundTripsAllSupportedValuesAndInvalidFallsBackToAuto()
    {
        var service = new LauncherSettingsService(settingsPath);

        foreach (var language in new[]
        {
            LauncherLanguages.English,
            LauncherLanguages.SimplifiedChinese,
            LauncherLanguages.TraditionalChinese,
            LauncherLanguages.Japanese
        })
        {
            await service.SaveAsync(new LauncherSettings { Language = language });
            Assert.Equal(language, (await service.ReadAsync()).Language);
        }

        await File.WriteAllTextAsync(settingsPath, """{"language":"invalid"}""");
        Assert.Equal(LauncherLanguages.Auto, (await service.ReadAsync()).Language);
    }
    private readonly string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private readonly string settingsPath;

    public LauncherSettingsServiceTests()
    {
        settingsPath = Path.Combine(tempDir, "settings.json");
        Directory.CreateDirectory(tempDir);
    }

    [Fact]
    public async Task ReadAsync_WhenFileMissing_ReturnsDefaults()
    {
        var service = new LauncherSettingsService(settingsPath);

        var settings = await service.ReadAsync();

        Assert.Equal("", settings.GamePath);
        Assert.Equal(LaunchCheckModes.LocalManifest, settings.LaunchCheckMode);
        Assert.Equal(ProxyModes.Auto, settings.ProxyMode);
        Assert.Equal(CloseBehaviors.Minimize, settings.CloseBehavior);
        Assert.Equal(LauncherLanguages.Auto, settings.Language);
        Assert.Equal(ThemeModes.System, settings.ThemeMode);
        Assert.Equal(MotionModes.System, settings.MotionMode);
        Assert.Equal(ThemeColorModes.Default, settings.ThemeColorMode);
        Assert.Equal(LauncherConstants.DefaultThemeColor, settings.CustomThemeColor);
        Assert.Empty(settings.ThemeColorPalette);
        Assert.Equal(0, settings.SelectedThemeColorPaletteIndex);
        Assert.Equal(DownloadSpeedLimits.Unlimited, settings.DownloadSpeedLimit);
        Assert.True(settings.EnableStartupUpdateCheck);
        Assert.True(settings.ShowRemoteContentCard);
        Assert.False(settings.RememberWindowPositionAndSize);
        Assert.Null(settings.WindowPositionX);
        Assert.Null(settings.WindowPositionY);
        Assert.Null(settings.WindowWidth);
        Assert.Null(settings.WindowHeight);
        // PatchUrlGroup defaults to Cafe when UI culture is Chinese, otherwise Official.
        var expectedGroup = System.Globalization.CultureInfo.CurrentUICulture.Name is
            "zh-CN" or "zh-TW" or "zh-HK" or "zh-MO" or "zh-SG" or "zh-Hans" or "zh-Hant"
            ? PatchUrlGroups.Cafe
            : PatchUrlGroups.Official;
        Assert.Equal(expectedGroup, settings.PatchUrlGroup);
        Assert.Equal("", settings.CustomBackgroundPath);
        Assert.Equal(BackgroundSources.Bundled, settings.BackgroundSource);
        Assert.Equal("", settings.ResourcePanelUid);
    }

    [Fact]
    public async Task ReadAsync_WhenMotionModeMissing_UsesSystem()
    {
        await File.WriteAllTextAsync(settingsPath, """{"language":"ja"}""");

        var settings = await new LauncherSettingsService(settingsPath).ReadAsync();

        Assert.Equal(MotionModes.System, settings.MotionMode);
    }

    [Fact]
    public async Task ReadAsync_WhenLegacyFieldsExist_UsesExactLegacyNames()
    {
        await File.WriteAllTextAsync(
            settingsPath,
            """
            {
              "GamePath": "D:\\Games",
              "LaunchCheckMode": "RemoteManifest"
            }
            """);
        var service = new LauncherSettingsService(settingsPath);

        var settings = await service.ReadAsync();

        Assert.Equal(@"D:\Games", settings.GamePath);
        Assert.Equal(LaunchCheckModes.RemoteManifest, settings.LaunchCheckMode);
    }

    [Fact]
    public async Task ReadAsync_WhenRemovedFirstLaunchWizardFieldExists_IgnoresUnknownField()
    {
        await File.WriteAllTextAsync(
            settingsPath,
            """
            {
              "hasCompletedFirstLaunchWizard": true,
              "resourcePanelUid": "LEGACY-COMPAT-UID"
            }
            """);
        var service = new LauncherSettingsService(settingsPath);

        var exception = await Record.ExceptionAsync(async () =>
        {
            LauncherSettings settings = await service.ReadAsync();
            Assert.NotNull(settings);
            Assert.Equal("LEGACY-COMPAT-UID", settings.ResourcePanelUid);
        });

        Assert.Null(exception);
    }

    [Fact]
    public async Task ReadAsync_WhenValuesInvalid_NormalizesToDefaults()
    {
        await File.WriteAllTextAsync(
            settingsPath,
            """
            {
              "gamePath": null,
              "launchCheckMode": "invalid",
              "proxyMode": "invalid",
              "closeBehavior": "invalid",
              "language": "invalid",
              "themeMode": "invalid",
              "themeColorMode": "invalid",
              "customThemeColor": "invalid",
              "themeColorPalette": ["#ff112233", "invalid", "#445566"],
              "selectedThemeColorPaletteIndex": 99,
              "downloadSpeedLimit": "invalid",
              "toastNotificationsEnabled": false,
              "showRemoteContentCard": false,
              "windowWidth": 0,
              "windowHeight": -1,
              "patchUrlGroup": "invalid",
              "backgroundSource": "invalid",
              "resourcePanelUid": "  UID123  ",
              "statusDetailMode": "detailed"
            }
            """);
        var service = new LauncherSettingsService(settingsPath);

        var settings = await service.ReadAsync();

        Assert.Equal("", settings.GamePath);
        Assert.Equal(LaunchCheckModes.LocalManifest, settings.LaunchCheckMode);
        Assert.Equal(ProxyModes.Auto, settings.ProxyMode);
        Assert.Equal(CloseBehaviors.Minimize, settings.CloseBehavior);
        Assert.Equal(LauncherLanguages.Auto, settings.Language);
        Assert.Equal(ThemeModes.System, settings.ThemeMode);
        Assert.Equal(ThemeColorModes.Default, settings.ThemeColorMode);
        Assert.Equal(LauncherConstants.DefaultThemeColor, settings.CustomThemeColor);
        Assert.Equal(["#FF112233", "#FF445566"], settings.ThemeColorPalette);
        Assert.Equal(0, settings.SelectedThemeColorPaletteIndex);
        Assert.Equal(DownloadSpeedLimits.Unlimited, settings.DownloadSpeedLimit);
        Assert.False(settings.ShowRemoteContentCard);
        Assert.Null(settings.WindowWidth);
        Assert.Null(settings.WindowHeight);
        Assert.Equal(PatchUrlGroups.Official, settings.PatchUrlGroup);
        Assert.Equal(BackgroundSources.Bundled, settings.BackgroundSource);
        Assert.Equal("UID123", settings.ResourcePanelUid);
        Assert.Equal(StatusDetailModes.Compact, settings.StatusDetailMode);
    }

    [Fact]
    public async Task SaveAsync_WritesExactCurrentJsonFieldNames()
    {
        var service = new LauncherSettingsService(settingsPath);
        var settings = new LauncherSettings
        {
            GamePath = @"D:\YostarGames\BlueArchive_JP",
            LaunchCheckMode = LaunchCheckModes.RemoteManifest,
            ProxyMode = ProxyModes.System,
            CloseBehavior = CloseBehaviors.Exit,
            Language = LauncherLanguages.Japanese,
            ThemeMode = ThemeModes.Dark,
            ThemeColorMode = ThemeColorModes.Custom,
            CustomThemeColor = "#FF336699",
            ThemeColorPalette = ["#FF112233", "#FF445566"],
            SelectedThemeColorPaletteIndex = 1,
            DownloadSpeedLimit = DownloadSpeedLimits.Speed10MBs,
            EnableStartupUpdateCheck = false,
            ShowRemoteContentCard = false,
            RememberWindowPositionAndSize = true,
            WindowPositionX = 120,
            WindowPositionY = 240,
            WindowWidth = 1400,
            WindowHeight = 820,
            PatchUrlGroup = PatchUrlGroups.Cafe,
            CustomBackgroundPath = tempDir,
            BackgroundSource = BackgroundSources.Remote,
            BackgroundFit = BackgroundFits.Fill,
            BackgroundFillColor = "#FF112233",
            ResourcePanelUid = "UID123"
        };

        await service.SaveAsync(settings);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(settingsPath));
        var root = document.RootElement;
        var propertyNames = root.EnumerateObject()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);
        Assert.True(root.TryGetProperty("gamePath", out _));
        Assert.True(root.TryGetProperty("launchCheckMode", out _));
        Assert.True(root.TryGetProperty("proxyMode", out _));
        Assert.True(root.TryGetProperty("closeBehavior", out _));
        Assert.True(root.TryGetProperty("language", out _));
        Assert.True(root.TryGetProperty("themeMode", out _));
        Assert.True(root.TryGetProperty("motionMode", out _));
        Assert.True(root.TryGetProperty("themeColorMode", out var themeColorMode));
        Assert.Equal(ThemeColorModes.Custom, themeColorMode.GetString());
        Assert.True(root.TryGetProperty("customThemeColor", out var customThemeColor));
        Assert.Equal("#FF336699", customThemeColor.GetString());
        Assert.True(root.TryGetProperty("themeColorPalette", out var themeColorPalette));
        Assert.Equal(["#FF112233", "#FF445566"], themeColorPalette.EnumerateArray().Select(item => item.GetString()));
        Assert.True(root.TryGetProperty("selectedThemeColorPaletteIndex", out var selectedThemeColorPaletteIndex));
        Assert.Equal(1, selectedThemeColorPaletteIndex.GetInt32());
        Assert.True(root.TryGetProperty("downloadSpeedLimit", out _));
        Assert.False(root.TryGetProperty("toastNotificationsEnabled", out _));
        Assert.True(root.TryGetProperty("enableStartupUpdateCheck", out _));
        Assert.True(root.TryGetProperty("showRemoteContentCard", out _));
        Assert.True(root.TryGetProperty("rememberWindowPositionAndSize", out var rememberWindowPositionAndSize));
        Assert.True(rememberWindowPositionAndSize.GetBoolean());
        Assert.Equal(120, root.GetProperty("windowPositionX").GetInt32());
        Assert.Equal(240, root.GetProperty("windowPositionY").GetInt32());
        Assert.Equal(1400, root.GetProperty("windowWidth").GetDouble());
        Assert.Equal(820, root.GetProperty("windowHeight").GetDouble());
        Assert.True(root.TryGetProperty("patchUrlGroup", out _));
        Assert.True(root.TryGetProperty("customBackgroundPath", out var customBackgroundPath));
        Assert.Equal(tempDir, customBackgroundPath.GetString());
        Assert.True(root.TryGetProperty("backgroundSource", out _));
        Assert.True(root.TryGetProperty("backgroundFit", out var backgroundFit));
        Assert.Equal(BackgroundFits.Fill, backgroundFit.GetString());
        Assert.True(root.TryGetProperty("backgroundFillColor", out var backgroundFillColor));
        Assert.Equal("#FF112233", backgroundFillColor.GetString());
        Assert.True(root.TryGetProperty("resourcePanelUid", out var resourcePanelUid));
        Assert.Equal("UID123", resourcePanelUid.GetString());
        var expectedPropertyNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "gamePath",
            "launchCheckMode",
            "proxyMode",
            "closeBehavior",
            "language",
            "themeMode",
            "motionMode",
            "themeColorMode",
            "themeColorExtractionAlgorithm",
            "themeColorVariant",
            "neutralColorStrategy",
            "customThemeColor",
            "themeColorPalette",
            "selectedThemeColorPaletteIndex",
            "downloadSpeedLimit",
            "enableStartupUpdateCheck",
            "showRemoteContentCard",
            "rememberWindowPositionAndSize",
            "windowPositionX",
            "windowPositionY",
            "windowWidth",
            "windowHeight",
            "patchUrlGroup",
            "customBackgroundPath",
            "backgroundSource",
            "backgroundFit",
            "backgroundFillColor",
            "resourcePanelUid",
            "resourcePanelUidSource",
            "gameRuntime",
            "statusDetailMode",
            "updateChannel",
            "logLevel"
        };
        Assert.True(expectedPropertyNames.SetEquals(propertyNames));
        Assert.False(File.Exists($"{settingsPath}.tmp"));
    }

    [Fact]
    public async Task EnableStartupUpdateCheck_RoundTripsAndMissingDefaultsToTrue()
    {
        var service = new LauncherSettingsService(settingsPath);
        await service.SaveAsync(new LauncherSettings { EnableStartupUpdateCheck = false });
        Assert.False((await service.ReadAsync()).EnableStartupUpdateCheck);

        await File.WriteAllTextAsync(settingsPath, """{"language":"ja"}""");
        Assert.True((await service.ReadAsync()).EnableStartupUpdateCheck);
    }

    [Fact]
    public async Task SaveAsync_WhenCalledConcurrently_LeavesOneCompleteSettingsDocument()
    {
        var service = new LauncherSettingsService(settingsPath);
        var writes = Enumerable.Range(0, 32)
            .Select(index => service.SaveAsync(new LauncherSettings
            {
                GamePath = $@"D:\Games\{index}",
                ResourcePanelUid = $"UID{index}"
            }));

        await Task.WhenAll(writes);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(settingsPath));
        var root = document.RootElement;
        var gamePath = root.GetProperty("gamePath").GetString();
        var resourcePanelUid = root.GetProperty("resourcePanelUid").GetString();
        Assert.NotNull(gamePath);
        Assert.NotNull(resourcePanelUid);
        Assert.Equal(gamePath!["D:\\Games\\".Length..], resourcePanelUid!["UID".Length..]);
        Assert.Empty(Directory.EnumerateFiles(tempDir, "*.tmp"));
    }

    [Fact]
    public async Task SaveAsync_GameRuntimeSettings_RoundTripsAndNormalizesInvalidValues()
    {
        var service = new LauncherSettingsService(settingsPath);
        var settings = new LauncherSettings();
        settings.GameRuntime.Runner = "not-a-runner";
        settings.GameRuntime.RunnerPath = "  /usr/bin/umu-run  ";
        settings.GameRuntime.PrefixPath = "   ";
        await service.SaveAsync(settings);

        var reloaded = await new LauncherSettingsService(settingsPath).ReadAsync();

        Assert.Equal(GameRuntimeRunners.Auto, reloaded.GameRuntime.Runner);
        Assert.Equal("/usr/bin/umu-run", reloaded.GameRuntime.RunnerPath);
        Assert.Null(reloaded.GameRuntime.PrefixPath);
    }

    [Fact]
    public async Task ReadAsync_WhenGameRuntimeIsMissing_UsesDefaults()
    {
        var service = new LauncherSettingsService(settingsPath);
        await service.SaveAsync(new LauncherSettings());

        var reloaded = await new LauncherSettingsService(settingsPath).ReadAsync();

        Assert.Equal(GameRuntimeRunners.Auto, reloaded.GameRuntime.Runner);
        Assert.Null(reloaded.GameRuntime.RunnerPath);
        Assert.Null(reloaded.GameRuntime.PrefixPath);
        Assert.Null(reloaded.GameRuntime.ProtonPath);
    }

    [Fact]
    public async Task ReadAsync_WhenGameRuntimeIsNull_RecoversWithDefaults()
    {
        await File.WriteAllTextAsync(settingsPath, """{"gameRuntime":null}""");

        var reloaded = await new LauncherSettingsService(settingsPath).ReadAsync();

        Assert.NotNull(reloaded.GameRuntime);
        Assert.Equal(GameRuntimeRunners.Auto, reloaded.GameRuntime.Runner);
        Assert.Null(reloaded.GameRuntime.RunnerPath);
        Assert.Null(reloaded.GameRuntime.PrefixPath);
        Assert.Null(reloaded.GameRuntime.ProtonPath);
    }

    [Fact]
    public void DeepClone_WhenGameRuntimeIsNull_ProducesDefaultRuntime()
    {
        var source = JsonSerializer.Deserialize<LauncherSettings>("""{"gameRuntime":null}""");

        Assert.Null(source!.GameRuntime);

        var clone = source.DeepClone();

        Assert.NotNull(clone.GameRuntime);
        Assert.Equal(GameRuntimeRunners.Auto, clone.GameRuntime.Runner);
    }

    [Fact]
    public async Task ReadAsync_WhenNativeRunnerOnLinux_NormalizesToAuto()
    {
        await File.WriteAllTextAsync(settingsPath, """{"gameRuntime":{"runner":"native"}}""");
        var service = new LauncherSettingsService(settingsPath, isLinuxPlatform: () => true);

        var reloaded = await service.ReadAsync();

        Assert.Equal(GameRuntimeRunners.Auto, reloaded.GameRuntime.Runner);
    }

    [Fact]
    public async Task ReadAsync_WhenNativeRunnerOnWindows_KeepsNative()
    {
        await File.WriteAllTextAsync(settingsPath, """{"gameRuntime":{"runner":"native"}}""");
        var service = new LauncherSettingsService(settingsPath, isLinuxPlatform: () => false);

        var reloaded = await service.ReadAsync();

        Assert.Equal(GameRuntimeRunners.Native, reloaded.GameRuntime.Runner);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
