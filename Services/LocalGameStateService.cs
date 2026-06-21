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
    private readonly GameInstallationPath installationPath;
    private readonly JsonSerializerOptions jsonOptions = new()
    {
        PropertyNameCaseInsensitive = false
    };

    public LocalGameStateService()
        : this(new GameInstallationPath())
    {
    }

    public LocalGameStateService(GameInstallationPath installationPath)
    {
        this.installationPath = installationPath;
    }

    public async Task<LocalGameState> ReadAsync(string? gamePath = null, CancellationToken cancellationToken = default)
    {
        var normalizedGamePath = installationPath.NormalizeGamePath(
            string.IsNullOrWhiteSpace(gamePath) ? installationPath.GetDefaultGamePath() : gamePath);
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
                    cancellationToken).ConfigureAwait(false);
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
                    cancellationToken).ConfigureAwait(false);
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
}
