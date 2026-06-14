using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Services;

public sealed class LauncherSettingsService
{
    private readonly string? settingsPath;
    private readonly JsonSerializerOptions jsonOptions = new()
    {
        WriteIndented = true
    };

    public LauncherSettingsService()
    {
    }

    public LauncherSettingsService(string settingsPath)
    {
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
            return Path.Combine(folder, LauncherConstants.LauncherSettingsFileName);
        }
    }

    public async Task<LauncherSettings> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(SettingsPath))
        {
            return new LauncherSettings();
        }

        try
        {
            var json = await File.ReadAllTextAsync(SettingsPath, cancellationToken);
            var settings = JsonSerializer.Deserialize<LauncherSettings>(json, jsonOptions) ?? new LauncherSettings();
            ApplyLegacyFields(settings, json);
            return Normalize(settings);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return new LauncherSettings();
        }
    }

    public async Task SaveAsync(LauncherSettings settings, CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(settings);
        var path = SettingsPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tempPath = $"{path}.tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, normalized, jsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        File.Move(tempPath, path, overwrite: true);
    }

    private static LauncherSettings Normalize(LauncherSettings settings)
    {
        if (settings.LaunchCheckMode is not LaunchCheckModes.LocalManifest
            and not LaunchCheckModes.RemoteManifest
            and not LaunchCheckModes.None)
        {
            settings.LaunchCheckMode = LaunchCheckModes.LocalManifest;
        }

        if (settings.ProxyMode is not ProxyModes.Direct and not ProxyModes.System)
        {
            settings.ProxyMode = ProxyModes.Direct;
        }

        if (settings.CloseBehavior is not CloseBehaviors.Minimize and not CloseBehaviors.Exit)
        {
            settings.CloseBehavior = CloseBehaviors.Minimize;
        }

        if (settings.Language is not LauncherLanguages.Auto
            and not LauncherLanguages.English
            and not LauncherLanguages.SimplifiedChinese
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
            and not DownloadSpeedLimits._1MBs
            and not DownloadSpeedLimits._5MBs
            and not DownloadSpeedLimits._10MBs
            and not DownloadSpeedLimits._25MBs
            and not DownloadSpeedLimits._50MBs)
        {
            settings.DownloadSpeedLimit = DownloadSpeedLimits.Unlimited;
        }

        if (settings.PatchUrlGroup is not PatchUrlGroups.Official and not PatchUrlGroups.Cafe)
        {
            settings.PatchUrlGroup = PatchUrlGroups.Official;
        }

        if (settings.BackgroundSource is not BackgroundSources.Bundled
            and not BackgroundSources.Remote
            and not BackgroundSources.Custom)
        {
            settings.BackgroundSource = BackgroundSources.Bundled;
        }

        settings.GamePath ??= "";
        return settings;
    }

    private static string NormalizeColor(string? value)
    {
        return TryNormalizeColor(value) ?? LauncherConstants.DefaultThemeColor;
    }

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
        foreach (var ch in value)
        {
            if (!Uri.IsHexDigit(ch))
            {
                return false;
            }
        }

        return true;
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
}
