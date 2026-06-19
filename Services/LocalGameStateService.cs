using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Services;

public sealed class LocalGameStateService
{
    private readonly JsonSerializerOptions jsonOptions = new()
    {
        PropertyNameCaseInsensitive = false
    };

    public string GetDefaultGamePath()
    {
        var appDir = AppContext.BaseDirectory;
        var parent = Directory.GetParent(appDir)?.FullName ?? appDir;
        return NormalizeGamePath(parent);
    }

    public string NormalizeGamePath(string path)
    {
        var normalized = Path.GetFullPath(path);
        var segments = normalized.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        if (EndsWithSegments(segments, [GamePaths.RootFolderName, GamePaths.GameFolderName]))
        {
            return normalized;
        }

        if (EndsWithSegments(segments, [GamePaths.RootFolderName]))
        {
            return Path.Combine(normalized, GamePaths.GameFolderName);
        }

        return Path.Combine(normalized, GamePaths.RootFolderName, GamePaths.GameFolderName);
    }

    public async Task<LocalGameState> ReadAsync(string? gamePath = null, CancellationToken cancellationToken = default)
    {
        var normalizedGamePath = NormalizeGamePath(string.IsNullOrWhiteSpace(gamePath) ? GetDefaultGamePath() : gamePath);
        var configPath = Path.Combine(normalizedGamePath, GamePaths.GameConfigFileName);
        var manifestPath = Path.Combine(normalizedGamePath, GamePaths.ManifestFileName);

        var state = new LocalGameState
        {
            GamePath = normalizedGamePath,
            ConfigPath = configPath,
            ManifestPath = manifestPath,
            ConfigExists = File.Exists(configPath),
            ManifestExists = File.Exists(manifestPath)
        };

        try
        {
            if (state.ConfigExists)
            {
                await using var configStream = File.OpenRead(configPath);
                state.GameConfig = await JsonSerializer.DeserializeAsync<GameLauncherConfig>(
                    configStream,
                    jsonOptions,
                    cancellationToken);
                if (state.GameConfig is not null && !OfficialHashService.IsGameConfigHashValid(state.GameConfig))
                {
                    state.GameConfig = null;
                }
            }

            if (state.ManifestExists)
            {
                await using var manifestStream = File.OpenRead(manifestPath);
                state.Manifest = await JsonSerializer.DeserializeAsync<LocalManifest>(
                    manifestStream,
                    jsonOptions,
                    cancellationToken);
                if (state.Manifest is not null)
                {
                    NormalizeManifestByOfficialHash(state.Manifest);
                }
            }
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            state.Error = exception.Message;
        }

        return state;
    }

    private static void NormalizeManifestByOfficialHash(LocalManifest manifest)
    {
        if (!OfficialHashService.IsManifestInfoHashValid(manifest))
        {
            manifest.Name = "";
            manifest.Version = "";
            manifest.Basis = "";
        }

        manifest.Files = (manifest.Files ?? [])
            .Where(OfficialHashService.IsManifestFileHashValid)
            .ToList();
    }

    private static bool EndsWithSegments(string[] value, string[] suffix)
    {
        if (value.Length < suffix.Length)
        {
            return false;
        }

        var offset = value.Length - suffix.Length;
        for (var i = 0; i < suffix.Length; i++)
        {
            if (!string.Equals(value[offset + i], suffix[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
