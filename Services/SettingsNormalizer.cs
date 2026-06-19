using System;
using System.Linq;
using System.Text.Json;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Services;

public sealed class SettingsNormalizer
{
    private static readonly JsonSerializerOptions CloneOptions = new()
    {
        PropertyNameCaseInsensitive = false
    };

    public LauncherSettings Normalize(LauncherSettings settings)
    {
        settings = Clone(settings);
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
            and not BackgroundSources.Custom)
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

        if (settings.UpdateChannel is not UpdateChannels.Stable and not UpdateChannels.Beta)
        {
            settings.UpdateChannel = UpdateChannels.Stable;
        }

        settings.GamePath ??= "";
        settings.ResourcePanelUid = settings.ResourcePanelUid?.Trim() ?? "";
        return settings;
    }

    private static LauncherSettings Clone(LauncherSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, CloneOptions);
        return JsonSerializer.Deserialize<LauncherSettings>(json, CloneOptions)
            ?? new LauncherSettings();
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
}
