using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services.Diagnostics;

namespace Cafe.Launcher.Avalonia.Services;

public sealed class LauncherSettingsService : IDisposable
{
    private readonly SemaphoreSlim writeLock = new(1, 1);
    private readonly string? settingsPath;
    private readonly LocalDiagnostics? diagnostics;
    private static readonly JsonSerializerOptions jsonOptions = JsonDefaults.Indented;

    public LauncherSettingsService() : this(null, null)
    {
    }

    public LauncherSettingsService(LocalDiagnostics diagnostics) : this(diagnostics, null)
    {
    }

    public LauncherSettingsService(string settingsPath) : this(null, settingsPath)
    {
    }

    private LauncherSettingsService(LocalDiagnostics? diagnostics, string? settingsPath)
    {
        this.diagnostics = diagnostics;
        this.settingsPath = settingsPath;
    }

    public string SettingsPath
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(settingsPath))
            {
                return settingsPath;
            }

            return Path.Combine(
                LauncherUserDataDirectory.Root,
                GamePaths.LauncherSettingsFileName);
        }
    }

    public async Task<LauncherSettings> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(SettingsPath))
        {
            return CreateDefaultSettings();
        }

        try
        {
            var json = await File.ReadAllTextAsync(SettingsPath, cancellationToken).ConfigureAwait(false);
            var settings = JsonSerializer.Deserialize<LauncherSettings>(json, jsonOptions) ?? new LauncherSettings();
            ApplyLegacyFields(settings, json);
            return NormalizeSettings(settings);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            var message = $"LauncherSettingsService.ReadAsync: failed to read settings from {SettingsPath}: {exception.Message}";
            Debug.WriteLine(message);
            if (diagnostics is not null)
            {
                await diagnostics.ErrorAsync("Settings read failed", exception, CancellationToken.None).ConfigureAwait(false);
            }

            return CreateDefaultSettings();
        }
    }

    private static LauncherSettings CreateDefaultSettings()
    {
        return LauncherSettings.CreateDefaults();
    }

    public async Task SaveAsync(LauncherSettings settings, CancellationToken cancellationToken = default)
    {
        await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var normalized = NormalizeSettings(settings);
            var path = SettingsPath;
            await AtomicJsonFileStore.WriteAsync(
                path,
                normalized,
                jsonOptions,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            writeLock.Release();
        }
    }

    private static void ApplyLegacyFields(LauncherSettings settings, string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var hasGamePath = root.TryGetProperty("gamePath", out _);
        var hasLaunchCheckMode = root.TryGetProperty("launchCheckMode", out _);

        if (!hasGamePath
            && root.TryGetProperty("GamePath", out var legacyGamePath)
            && legacyGamePath.ValueKind == JsonValueKind.String)
        {
            settings.GamePath = legacyGamePath.GetString() ?? "";
        }

        if (!hasLaunchCheckMode
            && root.TryGetProperty("LaunchCheckMode", out var legacyLaunchCheckMode)
            && legacyLaunchCheckMode.ValueKind == JsonValueKind.String)
        {
            settings.LaunchCheckMode = legacyLaunchCheckMode.GetString() ?? LaunchCheckModes.LocalManifest;
        }
    }

    /// <summary>
    /// Normalize all setting values to valid codes, apply defaults for invalid values,
    /// normalize colors, trim UIDs, and return a deep-cloned copy.
    /// </summary>
    private static LauncherSettings NormalizeSettings(LauncherSettings settings)
    {
        settings = settings.DeepClone();
        settings.LaunchCheckMode = settings.LaunchCheckMode switch
        {
            "LocalManifest" => LaunchCheckModes.LocalManifest,
            "RemoteManifest" => LaunchCheckModes.RemoteManifest,
            "None" => LaunchCheckModes.None,
            _ => settings.LaunchCheckMode
        };

        foreach (var (getter, setter, isValid, fallback) in SettingValidations)
        {
            var current = getter(settings);
            if (!isValid(current))
            {
                setter(settings, fallback);
            }
        }

        settings.CustomThemeColor = NormalizeColor(settings.CustomThemeColor);
        settings.ThemeColorPalette = (settings.ThemeColorPalette ?? [])
            .Select(TryNormalizeColor)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (settings.SelectedThemeColorPaletteIndex < 0
            || settings.SelectedThemeColorPaletteIndex >= settings.ThemeColorPalette.Count)
        {
            settings.SelectedThemeColorPaletteIndex = 0;
        }

        settings.BackgroundFillColor = NormalizeColor(settings.BackgroundFillColor);
        settings.GamePath ??= "";
        settings.ResourcePanelUid = settings.ResourcePanelUid?.Trim() ?? "";

        if (settings.WindowWidth is not null && !IsValidWindowDimension(settings.WindowWidth.Value))
        {
            settings.WindowWidth = null;
        }

        if (settings.WindowHeight is not null && !IsValidWindowDimension(settings.WindowHeight.Value))
        {
            settings.WindowHeight = null;
        }

        return settings;
    }

    private static bool IsValidWindowDimension(double value) =>
        double.IsFinite(value) && value > 0;

    private delegate string SettingGetter(LauncherSettings settings);
    private delegate void SettingSetter(LauncherSettings settings, string value);

    private static readonly (SettingGetter Get, SettingSetter Set, Func<string, bool> IsValid, string Fallback)[] SettingValidations =
    [
        (s => s.LaunchCheckMode, (s, v) => s.LaunchCheckMode = v,
            v => v is LaunchCheckModes.LocalManifest or LaunchCheckModes.RemoteManifest or LaunchCheckModes.None,
            LaunchCheckModes.LocalManifest),
        (s => s.ProxyMode, (s, v) => s.ProxyMode = v,
            v => v is ProxyModes.Direct or ProxyModes.Auto or ProxyModes.System,
            ProxyModes.Auto),
        (s => s.CloseBehavior, (s, v) => s.CloseBehavior = v,
            v => v is CloseBehaviors.Minimize or CloseBehaviors.Exit,
            CloseBehaviors.Minimize),
        (s => s.Language, (s, v) => s.Language = v,
            v => v is LauncherLanguages.Auto or LauncherLanguages.English or LauncherLanguages.SimplifiedChinese or LauncherLanguages.TraditionalChinese or LauncherLanguages.Japanese,
            LauncherLanguages.Auto),
        (s => s.ThemeMode, (s, v) => s.ThemeMode = v,
            v => v is ThemeModes.System or ThemeModes.Light or ThemeModes.Dark,
            ThemeModes.System),
        (s => s.MotionMode, (s, v) => s.MotionMode = v,
            v => v is MotionModes.System or MotionModes.Full or MotionModes.Reduced,
            MotionModes.System),
        (s => s.StatusDetailMode, (s, v) => s.StatusDetailMode = v,
            v => v is StatusDetailModes.Hidden or StatusDetailModes.Compact,
            StatusDetailModes.Compact),
        (s => s.ThemeColorMode, (s, v) => s.ThemeColorMode = v,
            v => v is ThemeColorModes.Default or ThemeColorModes.System or ThemeColorModes.Wallpaper or ThemeColorModes.Custom,
            ThemeColorModes.Default),
        (s => s.ThemeColorExtractionAlgorithm, (s, v) => s.ThemeColorExtractionAlgorithm = v,
            v => v is ThemeColorExtractionAlgorithms.Octree or ThemeColorExtractionAlgorithms.CelebiScore or ThemeColorExtractionAlgorithms.Wu or ThemeColorExtractionAlgorithms.Wsmeans,
            ThemeColorExtractionAlgorithms.CelebiScore),
        (s => s.ThemeColorVariant, (s, v) => s.ThemeColorVariant = v,
            v => v is ThemeColorVariants.TonalSpot or ThemeColorVariants.Vibrant or ThemeColorVariants.Expressive or ThemeColorVariants.Fidelity or ThemeColorVariants.Content or ThemeColorVariants.Monochrome or ThemeColorVariants.Neutral or ThemeColorVariants.Rainbow,
            ThemeColorVariants.TonalSpot),
        (s => s.NeutralColorStrategy, (s, v) => s.NeutralColorStrategy = v,
            v => v is NeutralColorStrategies.BrandBlue or NeutralColorStrategies.SeedFollowing,
            NeutralColorStrategies.BrandBlue),
        (s => s.DownloadSpeedLimit, (s, v) => s.DownloadSpeedLimit = v,
            v => v is DownloadSpeedLimits.Unlimited or DownloadSpeedLimits.Speed1MBs or DownloadSpeedLimits.Speed5MBs or DownloadSpeedLimits.Speed10MBs or DownloadSpeedLimits.Speed25MBs or DownloadSpeedLimits.Speed50MBs,
            DownloadSpeedLimits.Unlimited),
        (s => s.PatchUrlGroup, (s, v) => s.PatchUrlGroup = v,
            v => v is PatchUrlGroups.Official or PatchUrlGroups.Cafe,
            PatchUrlGroups.Official),
        (s => s.BackgroundSource, (s, v) => s.BackgroundSource = v,
            v => v is BackgroundSources.Bundled or BackgroundSources.Remote or BackgroundSources.Custom,
            BackgroundSources.Bundled),
        (s => s.BackgroundFit, (s, v) => s.BackgroundFit = v,
            v => v is BackgroundFits.Fill or BackgroundFits.Uniform or BackgroundFits.UniformToFill,
            BackgroundFits.UniformToFill),
        (s => s.UpdateChannel, (s, v) => s.UpdateChannel = v,
            v => v is UpdateChannels.Stable or UpdateChannels.Beta,
            UpdateChannels.Stable),
        (s => s.LogLevel, (s, v) => s.LogLevel = v,
            v => v is LogLevels.Verbose or LogLevels.Debug or LogLevels.Information or LogLevels.Warning or LogLevels.Error or LogLevels.Fatal,
            LogLevels.Information),
        (s => s.ResourcePanelUidSource, (s, v) => s.ResourcePanelUidSource = v,
            v => v is ResourcePanelUidSources.Auto or ResourcePanelUidSources.Custom,
            ResourcePanelUidSources.Auto),
    ];

    private static string NormalizeColor(string? value) =>
        TryNormalizeColor(value) ?? LauncherConstants.DefaultThemeColor;

    private static string? TryNormalizeColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();
        if (text.Length == 7 && text[0] == '#'
            && IsHex(text.AsSpan(1)))
        {
            return $"#FF{text[1..].ToUpperInvariant()}";
        }

        if (text.Length == 9 && text[0] == '#'
            && IsHex(text.AsSpan(1)))
        {
            return $"#{text[1..].ToUpperInvariant()}";
        }

        return null;
    }

    private static bool IsHex(ReadOnlySpan<char> value)
    {
        foreach (var character in value)
        {
            if (!Uri.IsHexDigit(character))
            {
                return false;
            }
        }

        return true;
    }

    public void Dispose()
    {
        writeLock.Dispose();
    }
}
