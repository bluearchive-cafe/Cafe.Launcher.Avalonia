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

            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                LauncherConstants.ProductName);
            return Path.Combine(folder, GamePaths.LauncherSettingsFileName);
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
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
            try
            {
                await using (var stream = File.Create(tempPath))
                {
                    await JsonSerializer.SerializeAsync(stream, normalized, jsonOptions, cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                File.Move(tempPath, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
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

    internal static LauncherSettings NormalizeForTesting(LauncherSettings settings) =>
        NormalizeSettings(settings);

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

        if (settings.LaunchCheckMode is not LaunchCheckModes.LocalManifest
            and not LaunchCheckModes.RemoteManifest
            and not LaunchCheckModes.None)
        {
            settings.LaunchCheckMode = LaunchCheckModes.LocalManifest;
        }

        if (settings.ProxyMode is not ProxyModes.Direct and not ProxyModes.Auto and not ProxyModes.System)
        {
            settings.ProxyMode = ProxyModes.Auto;
        }

        if (settings.CloseBehavior is not CloseBehaviors.Minimize and not CloseBehaviors.Exit)
        {
            settings.CloseBehavior = CloseBehaviors.Minimize;
        }

        if (settings.Language is not LauncherLanguages.Auto
            and not LauncherLanguages.English
            and not LauncherLanguages.SimplifiedChinese
            and not LauncherLanguages.TraditionalChinese
            and not LauncherLanguages.Japanese)
        {
            settings.Language = LauncherLanguages.Auto;
        }

        if (settings.ThemeMode is not ThemeModes.System
            and not ThemeModes.Light
            and not ThemeModes.Dark)
        {
            settings.ThemeMode = ThemeModes.System;
        }

        if (settings.MotionMode is not MotionModes.System
            and not MotionModes.Full
            and not MotionModes.Reduced)
        {
            settings.MotionMode = MotionModes.System;
        }

        if (settings.StatusDetailMode is not StatusDetailModes.Hidden
            and not StatusDetailModes.Compact
            and not StatusDetailModes.Detailed)
        {
            settings.StatusDetailMode = StatusDetailModes.Detailed;
        }

        if (settings.ThemeColorMode is not ThemeColorModes.Default
            and not ThemeColorModes.System
            and not ThemeColorModes.Wallpaper
            and not ThemeColorModes.Custom)
        {
            settings.ThemeColorMode = ThemeColorModes.Default;
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

        if (settings.DownloadSpeedLimit is not DownloadSpeedLimits.Unlimited
            and not DownloadSpeedLimits.Speed1MBs
            and not DownloadSpeedLimits.Speed5MBs
            and not DownloadSpeedLimits.Speed10MBs
            and not DownloadSpeedLimits.Speed25MBs
            and not DownloadSpeedLimits.Speed50MBs)
        {
            settings.DownloadSpeedLimit = DownloadSpeedLimits.Unlimited;
        }

        if (settings.PatchUrlGroup is not PatchUrlGroups.Official and not PatchUrlGroups.Cafe)
        {
            settings.PatchUrlGroup = PatchUrlGroups.Official;
        }

        if (settings.BackgroundSource is not BackgroundSources.Bundled
            and not BackgroundSources.Remote
            and not BackgroundSources.Custom
            and not BackgroundSources.Video)
        {
            settings.BackgroundSource = BackgroundSources.Bundled;
        }

        if (settings.BackgroundFit is not BackgroundFits.Fill
            and not BackgroundFits.Uniform
            and not BackgroundFits.UniformToFill)
        {
            settings.BackgroundFit = BackgroundFits.UniformToFill;
        }

        settings.BackgroundFillColor = NormalizeColor(settings.BackgroundFillColor);
        settings.VideoBackgroundVolume = Math.Clamp(settings.VideoBackgroundVolume, 0, 100);
        settings.VideoBackgroundPath = settings.VideoBackgroundPath?.Trim() ?? "";

        if (settings.UpdateChannel is not UpdateChannels.Stable and not UpdateChannels.Beta)
        {
            settings.UpdateChannel = UpdateChannels.Stable;
        }

        if (settings.LogLevel is not LogLevels.Verbose
            and not LogLevels.Debug
            and not LogLevels.Information
            and not LogLevels.Warning
            and not LogLevels.Error
            and not LogLevels.Fatal)
        {
            settings.LogLevel = LogLevels.Information;
        }

        settings.GamePath ??= "";
        settings.ResourcePanelUid = settings.ResourcePanelUid?.Trim() ?? "";
        if (settings.ResourcePanelUidSource is not ResourcePanelUidSources.Auto
            and not ResourcePanelUidSources.Custom)
        {
            settings.ResourcePanelUidSource = ResourcePanelUidSources.Auto;
        }

        return settings;
    }

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
