using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services.Diagnostics;

namespace Cafe.Launcher.Avalonia.Services;

public sealed class LauncherSettingsService
{
    private readonly SemaphoreSlim writeLock = new(1, 1);
    private readonly string? settingsPath;
    private readonly LocalDiagnostics? diagnostics;
    private readonly SettingsNormalizer normalizer;
    private readonly JsonSerializerOptions jsonOptions = new()
    {
        WriteIndented = true
    };

    public LauncherSettingsService()
    {
        normalizer = new SettingsNormalizer();
    }

    public LauncherSettingsService(LocalDiagnostics diagnostics)
    {
        this.diagnostics = diagnostics;
        normalizer = new SettingsNormalizer();
    }

    public LauncherSettingsService(
        LocalDiagnostics diagnostics,
        SettingsNormalizer normalizer)
    {
        this.diagnostics = diagnostics;
        this.normalizer = normalizer;
    }

    public LauncherSettingsService(string settingsPath)
    {
        this.settingsPath = settingsPath;
        normalizer = new SettingsNormalizer();
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
            return normalizer.Normalize(settings);
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
            var normalized = normalizer.Normalize(settings);
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
}
