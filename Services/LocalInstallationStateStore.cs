using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Services;

public sealed class LocalInstallationStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        WriteIndented = true
    };

    private readonly ConcurrentDictionary<string, PathLockEntry> pathLocks =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<string, CancellationToken, Task>? beforeTempValidation;

    public LocalInstallationStateStore()
    {
    }

    internal LocalInstallationStateStore(
        Func<string, CancellationToken, Task> beforeTempValidation)
    {
        this.beforeTempValidation = beforeTempValidation;
    }

    public async Task<LocalInstallationState> ReadAsync(
        string gamePath,
        CancellationToken cancellationToken = default)
    {
        var normalizedGamePath = Path.GetFullPath(gamePath);
        await using var pathLock = await AcquirePathLockAsync(
            normalizedGamePath,
            cancellationToken).ConfigureAwait(false);
        return await ReadCoreAsync(
            normalizedGamePath,
            Path.Combine(normalizedGamePath, GamePaths.GameConfigFileName),
            Path.Combine(normalizedGamePath, GamePaths.ManifestFileName),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<LocalInstallationState> CommitAsync(
        string gamePath,
        LocalInstallationStateCommit commit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit);
        var normalizedGamePath = Path.GetFullPath(gamePath);
        var copiedCommit = ValidateAndCopyCommit(normalizedGamePath, commit);
        await using var pathLock = await AcquirePathLockAsync(
            normalizedGamePath,
            cancellationToken).ConfigureAwait(false);

        var configPath = Path.Combine(normalizedGamePath, GamePaths.GameConfigFileName);
        var manifestPath = Path.Combine(normalizedGamePath, GamePaths.ManifestFileName);
        var tempConfigPath = $"{configPath}.tmp";
        var tempManifestPath = $"{manifestPath}.tmp";

        try
        {
            if (!Directory.Exists(normalizedGamePath))
            {
                throw new DirectoryNotFoundException(
                    $"Game installation directory does not exist: {normalizedGamePath}");
            }

            var manifestFiles = copiedCommit.Files
                .Select(file =>
                {
                    var manifestFile = new ManifestFile
                    {
                        Path = file.Path,
                        Size = file.Size.ToString(CultureInfo.InvariantCulture),
                        Hash = file.Crc64.ToString(CultureInfo.InvariantCulture)
                    };
                    manifestFile.Vc = OfficialHashService.GetManifestFileHash(manifestFile);
                    return manifestFile;
                })
                .ToList();
            var manifest = new LocalManifest
            {
                Name = GamePaths.GameTag,
                Version = copiedCommit.Version,
                Basis = copiedCommit.ManifestBasis,
                Files = manifestFiles
            };
            manifest.Vc = OfficialHashService.GetManifestInfoHash(
                manifest.Name,
                manifest.Version,
                manifest.Basis);
            var config = new GameLauncherConfig
            {
                Tag = GamePaths.GameTag,
                Name = copiedCommit.ExecutableName,
                Params = copiedCommit.LaunchParameters.ToArray(),
                Version = copiedCommit.Version
            };
            config.Vc = OfficialHashService.GetGameConfigHash(config);

            await File.WriteAllTextAsync(
                tempManifestPath,
                JsonSerializer.Serialize(manifest, JsonOptions),
                cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(
                tempConfigPath,
                JsonSerializer.Serialize(config, JsonOptions),
                cancellationToken).ConfigureAwait(false);

            if (beforeTempValidation is not null)
            {
                await beforeTempValidation(normalizedGamePath, cancellationToken).ConfigureAwait(false);
            }

            var tempState = await ReadCoreAsync(
                normalizedGamePath,
                tempConfigPath,
                tempManifestPath,
                cancellationToken).ConfigureAwait(false);
            if (tempState.Kind != LocalInstallationStateKind.Valid)
            {
                return tempState;
            }

            File.Move(tempManifestPath, manifestPath, overwrite: true);
            File.Move(tempConfigPath, configPath, overwrite: true);
            return await ReadCoreAsync(
                normalizedGamePath,
                configPath,
                manifestPath,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return CreateFailure(
                LocalInstallationStateKind.IoFailure,
                normalizedGamePath,
                configPath,
                manifestPath,
                exception.Message);
        }
    }

    public async Task<LocalInstallationState> DeleteAsync(
        string gamePath,
        CancellationToken cancellationToken = default)
    {
        var normalizedGamePath = Path.GetFullPath(gamePath);
        await using var pathLock = await AcquirePathLockAsync(
            normalizedGamePath,
            cancellationToken).ConfigureAwait(false);
        var configPath = Path.Combine(normalizedGamePath, GamePaths.GameConfigFileName);
        var manifestPath = Path.Combine(normalizedGamePath, GamePaths.ManifestFileName);

        try
        {
            File.Delete(manifestPath);
            File.Delete(configPath);
            return CreateFailure(
                LocalInstallationStateKind.NotInstalled,
                normalizedGamePath,
                configPath,
                manifestPath,
                null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return CreateFailure(
                LocalInstallationStateKind.IoFailure,
                normalizedGamePath,
                configPath,
                manifestPath,
                exception.Message);
        }
    }

    private async Task<LocalInstallationState> ReadCoreAsync(
        string gamePath,
        string configPath,
        string manifestPath,
        CancellationToken cancellationToken)
    {
        try
        {
            var configExists = File.Exists(configPath);
            var manifestExists = File.Exists(manifestPath);
            if (!configExists && !manifestExists)
            {
                return CreateFailure(
                    LocalInstallationStateKind.NotInstalled,
                    gamePath,
                    configPath,
                    manifestPath,
                    null);
            }

            if (!configExists || !manifestExists)
            {
                return CreateFailure(
                    LocalInstallationStateKind.Corrupted,
                    gamePath,
                    configPath,
                    manifestPath,
                    null);
            }

            var config = await ReadConfigAsync(configPath, cancellationToken).ConfigureAwait(false);
            var manifest = await ReadManifestAsync(manifestPath, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(config.Version, manifest.Version, StringComparison.Ordinal))
            {
                return CreateFailure(
                    LocalInstallationStateKind.Corrupted,
                    gamePath,
                    configPath,
                    manifestPath,
                    null);
            }

            return new LocalInstallationState
            {
                Kind = LocalInstallationStateKind.Valid,
                GamePath = gamePath,
                ConfigPath = configPath,
                ManifestPath = manifestPath,
                GameConfig = config,
                Manifest = manifest
            };
        }
        catch (JsonException)
        {
            return CreateFailure(
                LocalInstallationStateKind.Corrupted,
                gamePath,
                configPath,
                manifestPath,
                null);
        }
        catch (InvalidDataException)
        {
            return CreateFailure(
                LocalInstallationStateKind.Corrupted,
                gamePath,
                configPath,
                manifestPath,
                null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return CreateFailure(
                LocalInstallationStateKind.IoFailure,
                gamePath,
                configPath,
                manifestPath,
                exception.Message);
        }
    }

    private static async Task<GameLauncherConfig> ReadConfigAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        using var document = await JsonDocument.ParseAsync(
            stream,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = RequireObject(document.RootElement);
        RequireNonEmptyString(root, "tag");
        RequireNonEmptyString(root, "name");
        RequireNonEmptyString(root, "version");
        RequireNonEmptyString(root, "vc");
        var parameters = RequireProperty(root, "params");
        if (parameters.ValueKind != JsonValueKind.Array
            || parameters.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String))
        {
            throw new InvalidDataException("Invalid params.");
        }

        var config = document.RootElement.Deserialize<GameLauncherConfig>(JsonOptions)
            ?? throw new InvalidDataException("Invalid game launcher config.");
        if (!OfficialHashService.IsGameConfigHashValid(config))
        {
            throw new InvalidDataException("Invalid game launcher config Vc.");
        }

        return config;
    }

    private static async Task<LocalManifest> ReadManifestAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        using var document = await JsonDocument.ParseAsync(
            stream,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = RequireObject(document.RootElement);
        RequireNonEmptyString(root, "name");
        RequireNonEmptyString(root, "version");
        RequireNonEmptyString(root, "basis");
        RequireNonEmptyString(root, "vc");
        var files = RequireProperty(root, "files");
        if (files.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Invalid files.");
        }

        foreach (var item in files.EnumerateArray())
        {
            var file = RequireObject(item);
            RequireNonEmptyString(file, "path");
            var size = RequireNonEmptyString(file, "size");
            var hash = RequireNonEmptyString(file, "hash");
            RequireNonEmptyString(file, "vc");
            if (!long.TryParse(
                    size,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var parsedSize)
                || parsedSize < 0
                || !ulong.TryParse(
                    hash,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out _))
            {
                throw new InvalidDataException("Invalid manifest file numeric value.");
            }
        }

        var manifest = document.RootElement.Deserialize<LocalManifest>(JsonOptions)
            ?? throw new InvalidDataException("Invalid manifest.");
        if (!OfficialHashService.IsManifestInfoHashValid(manifest)
            || manifest.Files.Any(file => !OfficialHashService.IsManifestFileHashValid(file)))
        {
            throw new InvalidDataException("Invalid manifest Vc.");
        }

        return manifest;
    }

    private static JsonElement RequireObject(JsonElement value)
    {
        return value.ValueKind == JsonValueKind.Object
            ? value
            : throw new InvalidDataException("Expected JSON object.");
    }

    private static JsonElement RequireProperty(JsonElement value, string propertyName)
    {
        return value.TryGetProperty(propertyName, out var property)
            ? property
            : throw new InvalidDataException($"Missing property: {propertyName}");
    }

    private static string RequireNonEmptyString(JsonElement value, string propertyName)
    {
        var property = RequireProperty(value, propertyName);
        if (property.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException($"Invalid property: {propertyName}");
        }

        var text = property.GetString();
        return !string.IsNullOrWhiteSpace(text)
            ? text
            : throw new InvalidDataException($"Empty property: {propertyName}");
    }

    private static LocalInstallationStateCommit ValidateAndCopyCommit(
        string gamePath,
        LocalInstallationStateCommit commit)
    {
        if (string.IsNullOrWhiteSpace(commit.Version))
        {
            throw new ArgumentException("Version is required.", nameof(commit));
        }

        if (string.IsNullOrWhiteSpace(commit.ManifestBasis))
        {
            throw new ArgumentException("Manifest basis is required.", nameof(commit));
        }

        if (string.IsNullOrWhiteSpace(commit.ExecutableName))
        {
            throw new ArgumentException("Executable name is required.", nameof(commit));
        }

        ArgumentNullException.ThrowIfNull(commit.LaunchParameters);
        ArgumentNullException.ThrowIfNull(commit.Files);
        var launchParameters = commit.LaunchParameters.ToArray();
        if (launchParameters.Any(parameter => parameter is null))
        {
            throw new ArgumentException("Launch parameters cannot contain null.", nameof(commit));
        }

        var files = commit.Files.ToArray();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            ArgumentNullException.ThrowIfNull(file);
            if (string.IsNullOrWhiteSpace(file.Path))
            {
                throw new ArgumentException("File path is required.", nameof(commit));
            }

            if (file.Size < 0)
            {
                throw new ArgumentException("File size cannot be negative.", nameof(commit));
            }

            string fullPath;
            try
            {
                fullPath = GamePathValidator.GetSafePath(gamePath, file.Path);
            }
            catch (InvalidOperationException exception)
            {
                throw new ArgumentException("File path escapes the game directory.", nameof(commit), exception);
            }

            if (string.Equals(fullPath, gamePath, StringComparison.OrdinalIgnoreCase)
                || !paths.Add(fullPath))
            {
                throw new ArgumentException("File paths must be unique files.", nameof(commit));
            }
        }

        return new LocalInstallationStateCommit(
            commit.Version,
            commit.ManifestBasis,
            commit.ExecutableName,
            launchParameters,
            files);
    }

    private async Task<PathLockReleaser> AcquirePathLockAsync(
        string gamePath,
        CancellationToken cancellationToken)
    {
        PathLockEntry entry;
        while (true)
        {
            entry = pathLocks.GetOrAdd(gamePath, static _ => new PathLockEntry());
            lock (entry)
            {
                if (entry.Removed)
                {
                    continue;
                }

                entry.ReferenceCount++;
                break;
            }
        }

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new PathLockReleaser(this, gamePath, entry);
        }
        catch
        {
            ReleaseReference(gamePath, entry, releaseSemaphore: false);
            throw;
        }
    }

    private void ReleaseReference(
        string gamePath,
        PathLockEntry entry,
        bool releaseSemaphore)
    {
        if (releaseSemaphore)
        {
            entry.Semaphore.Release();
        }

        lock (entry)
        {
            entry.ReferenceCount--;
            if (entry.ReferenceCount == 0)
            {
                entry.Removed = true;
                pathLocks.TryRemove(
                    new KeyValuePair<string, PathLockEntry>(gamePath, entry));
                entry.Semaphore.Dispose();
            }
        }
    }

    private static LocalInstallationState CreateFailure(
        LocalInstallationStateKind kind,
        string gamePath,
        string configPath,
        string manifestPath,
        string? error)
    {
        return new LocalInstallationState
        {
            Kind = kind,
            GamePath = gamePath,
            ConfigPath = configPath,
            ManifestPath = manifestPath,
            Error = error
        };
    }

    private sealed class PathLockEntry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public int ReferenceCount { get; set; }

        public bool Removed { get; set; }
    }

    private readonly struct PathLockReleaser : IAsyncDisposable
    {
        private readonly LocalInstallationStateStore owner;
        private readonly string gamePath;
        private readonly PathLockEntry entry;

        public PathLockReleaser(
            LocalInstallationStateStore owner,
            string gamePath,
            PathLockEntry entry)
        {
            this.owner = owner;
            this.gamePath = gamePath;
            this.entry = entry;
        }

        public ValueTask DisposeAsync()
        {
            owner.ReleaseReference(gamePath, entry, releaseSemaphore: true);
            return ValueTask.CompletedTask;
        }
    }
}
