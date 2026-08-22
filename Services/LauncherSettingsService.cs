using System;
using System.Collections.Generic;
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

        foreach (var (getter, setter, options, fallback) in SettingValidations)
        {
            var current = getter(settings);
            if (!SettingOptionDescriptors.ContainsCode(options, current))
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

        return settings;
    }

    private delegate string SettingGetter(LauncherSettings settings);
    private delegate void SettingSetter(LauncherSettings settings, string value);

    private static readonly (SettingGetter Get, SettingSetter Set, IReadOnlyList<SettingOptionDescriptor> Options, string Fallback)[] SettingValidations =
    [
        (s => s.LaunchCheckMode, (s, v) => s.LaunchCheckMode = v,
            SettingOptionDescriptors.LaunchCheckMode,
            LaunchCheckModes.LocalManifest),
        (s => s.ProxyMode, (s, v) => s.ProxyMode = v,
            SettingOptionDescriptors.ProxyMode,
            ProxyModes.Auto),
        (s => s.CloseBehavior, (s, v) => s.CloseBehavior = v,
            SettingOptionDescriptors.CloseBehavior,
            CloseBehaviors.Minimize),
        (s => s.Language, (s, v) => s.Language = v,
            SettingOptionDescriptors.Language,
            LauncherLanguages.Auto),
        (s => s.ThemeMode, (s, v) => s.ThemeMode = v,
            SettingOptionDescriptors.Theme,
            ThemeModes.System),
        (s => s.MotionMode, (s, v) => s.MotionMode = v,
            SettingOptionDescriptors.MotionMode,
            MotionModes.System),
        (s => s.StatusDetailMode, (s, v) => s.StatusDetailMode = v,
            SettingOptionDescriptors.StatusDetailMode,
            StatusDetailModes.Compact),
        (s => s.ThemeColorMode, (s, v) => s.ThemeColorMode = v,
            SettingOptionDescriptors.ThemeColor,
            ThemeColorModes.Default),
        (s => s.DownloadSpeedLimit, (s, v) => s.DownloadSpeedLimit = v,
            SettingOptionDescriptors.DownloadSpeedLimit,
            DownloadSpeedLimits.Unlimited),
        (s => s.PatchUrlGroup, (s, v) => s.PatchUrlGroup = v,
            SettingOptionDescriptors.PatchUrlGroup,
            PatchUrlGroups.Official),
        (s => s.BackgroundSource, (s, v) => s.BackgroundSource = v,
            SettingOptionDescriptors.BackgroundSource,
            BackgroundSources.Bundled),
        (s => s.BackgroundFit, (s, v) => s.BackgroundFit = v,
            SettingOptionDescriptors.BackgroundFit,
            BackgroundFits.UniformToFill),
        (s => s.UpdateChannel, (s, v) => s.UpdateChannel = v,
            SettingOptionDescriptors.UpdateChannel,
            UpdateChannels.Stable),
        (s => s.LogLevel, (s, v) => s.LogLevel = v,
            SettingOptionDescriptors.LogLevel,
            LogLevels.Information),
        (s => s.ResourcePanelUidSource, (s, v) => s.ResourcePanelUidSource = v,
            SettingOptionDescriptors.ResourcePanelUidSource,
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
