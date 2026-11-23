using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Core.Constants;
using Cafe.Launcher.Core.Helpers;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Services;
using Cafe.Launcher.UI.Helpers;
using Cafe.Launcher.UI.Models;

namespace Cafe.Launcher.UI.Features.GameOperations;

/// <summary>Persists resumable state for an explicit game and protects unrecognized documents.</summary>
internal sealed class DownloadCheckpointStore
{
    /// <summary>格式 2 的检查点同时声明版本与稳定游戏身份。</summary>
    internal const int CurrentFormatVersion = 2;

    private readonly string filePath;
    private readonly string temporaryFilePath;
    private readonly bool allowsLegacy;
    private readonly string? migrationMarkerPath;

    /// <summary>旧根路径的过渡入口，仅对应 BA；不据无身份状态自动推断归属。</summary>
    public DownloadCheckpointStore(LauncherDataRoot dataRoot)
        : this((dataRoot ?? throw new ArgumentNullException(nameof(dataRoot))).DownloadStatePath,
            LauncherProfiles.BlueArchiveJapan.RuntimeId, allowsLegacy: true, migrationMarkerPath: null)
    {
    }

    /// <summary>读写指定游戏的版本化检查点；不隐式迁移旧根文件。</summary>
    public DownloadCheckpointStore(GameDataPaths paths)
        : this((paths ?? throw new ArgumentNullException(nameof(paths))).DownloadStatePath,
            paths.GameId, allowsLegacy: false, paths.DownloadMigrationMarkerPath)
    {
    }

    private DownloadCheckpointStore(string filePath, string gameId, bool allowsLegacy, string? migrationMarkerPath)
    {
        this.filePath = filePath;
        temporaryFilePath = filePath + ".tmp";
        GameId = gameId;
        this.allowsLegacy = allowsLegacy;
        this.migrationMarkerPath = migrationMarkerPath;
    }

    /// <summary>此存储唯一接受的稳定游戏身份。</summary>
    internal string GameId { get; }

    /// <summary>Reads compatible state; wrong identity, future format and corruption remain untouched.</summary>
    public async Task<DownloadTaskState?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            return ParseState(json, GameId, allowsLegacy);
        }
        catch (Exception exception) when (StorageFailure.IsRecoverableOrInvalidJson(exception))
        {
            return null;
        }
    }

    /// <summary>Writes this store's identity; legacy writes are confined to the root transition.</summary>
    public async Task SaveAsync(DownloadTaskState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        var isLegacy = state.FormatVersion == 0 && string.IsNullOrEmpty(state.GameId);
        if (!(allowsLegacy && isLegacy)
            && (state.FormatVersion != CurrentFormatVersion || !string.Equals(state.GameId, GameId, StringComparison.Ordinal)))
        {
            throw new ArgumentException("Checkpoint identity or format does not match its store.", nameof(state));
        }

        if (File.Exists(filePath) && await LoadAsync(cancellationToken).ConfigureAwait(false) is null)
        {
            throw new InvalidDataException("An unrecognized checkpoint cannot be overwritten.");
        }

        await AtomicJsonFileStore.WriteAsync(
            filePath,
            state,
            UiStorageJsonContext.Default.DownloadTaskState,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Clears recognized state for this game; legacy/future evidence is retained.</summary>
    public void Clear()
    {
        if (File.Exists(filePath))
        {
            try
            {
                if (ParseState(File.ReadAllText(filePath), GameId, allowLegacy: false) is null)
                {
                    return;
                }
            }
            catch (Exception exception) when (StorageFailure.IsRecoverableOrInvalidJson(exception))
            {
                return;
            }
        }

        DeleteIfPresent(filePath);
        DeleteIfPresent(temporaryFilePath);
    }

    /// <summary>
    /// 显式迁移已验证的旧 BA 检查点。原根文件作为原字节备份保留，目标存在时不覆盖。
    /// 无本地安装身份的旧首次安装返回 IdentityUnverified，不凭目录名推断游戏。
    /// </summary>
    internal async Task<LegacyCheckpointMigrationResult> TryMigrateLegacyBlueArchiveAsync(
        LauncherDataRoot legacyRoot,
        LauncherStatusSnapshot snapshot,
        ILocalInstallationStateStore installationStore,
        IGameInstallationPath installationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(legacyRoot);
        if (allowsLegacy || !string.Equals(GameId, LauncherProfiles.BlueArchiveJapan.RuntimeId, StringComparison.Ordinal)
            || !string.Equals(snapshot.GameId, GameId, StringComparison.Ordinal))
        {
            return LegacyCheckpointMigrationResult.WrongTarget;
        }

        string? temporaryPath = null;
        try
        {
            if (File.Exists(migrationMarkerPath))
            {
                try
                {
                    return await IsMatchingMigrationMarkerAsync(cancellationToken).ConfigureAwait(false)
                        ? LegacyCheckpointMigrationResult.AlreadyMigrated : LegacyCheckpointMigrationResult.InvalidMigrationMarker;
                }
                catch (JsonException)
                {
                    return LegacyCheckpointMigrationResult.InvalidMigrationMarker;
                }
            }

            if (File.Exists(filePath))
            {
                if (await LoadAsync(cancellationToken).ConfigureAwait(false) is null)
                {
                    return LegacyCheckpointMigrationResult.InvalidCheckpoint;
                }

                await WriteMigrationMarkerAsync(cancellationToken).ConfigureAwait(false);
                return LegacyCheckpointMigrationResult.DestinationExists;
            }

            if (!File.Exists(legacyRoot.DownloadStatePath))
            {
                return LegacyCheckpointMigrationResult.LegacyMissing;
            }

            var json = await File.ReadAllTextAsync(legacyRoot.DownloadStatePath, cancellationToken).ConfigureAwait(false);
            var state = ParseState(json, GameId, allowLegacy: true);
            if (state is null)
            {
                return LegacyCheckpointMigrationResult.InvalidCheckpoint;
            }

            if (!MatchesSnapshot(state, snapshot, installationPath))
            {
                return LegacyCheckpointMigrationResult.IncompatibleCheckpoint;
            }

            if (state.FormatVersion != CurrentFormatVersion)
            {
                var identityFailure = await GetLegacyIdentityFailureAsync(snapshot, installationStore, installationPath, cancellationToken).ConfigureAwait(false);
                if (identityFailure is not null)
                {
                    return identityFailure.Value;
                }
            }

            state.FormatVersion = CurrentFormatVersion;
            state.GameId = GameId;
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            temporaryPath = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    state,
                    UiStorageJsonContext.Default.DownloadTaskState,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            try
            {
                File.Move(temporaryPath, filePath, overwrite: false);
            }
            catch (IOException) when (File.Exists(filePath))
            {
                if (await LoadAsync(cancellationToken).ConfigureAwait(false) is null)
                {
                    return LegacyCheckpointMigrationResult.InvalidCheckpoint;
                }

                await WriteMigrationMarkerAsync(cancellationToken).ConfigureAwait(false);
                return LegacyCheckpointMigrationResult.DestinationExists;
            }

            // Publication is not complete until the marker prevents the retained
            // root backup from reviving a task cleared by an explicit later stop.
            await WriteMigrationMarkerAsync(cancellationToken).ConfigureAwait(false);
            return LegacyCheckpointMigrationResult.Migrated;
        }
        catch (JsonException)
        {
            return LegacyCheckpointMigrationResult.InvalidCheckpoint;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return LegacyCheckpointMigrationResult.InvalidCheckpoint;
        }
        catch (Exception exception) when (StorageFailure.IsRecoverable(exception))
        {
            return LegacyCheckpointMigrationResult.IoFailure;
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try
                {
                    DeleteIfPresent(temporaryPath);
                }
                catch (Exception exception) when (StorageFailure.IsRecoverable(exception))
                {
                    // A failed temporary-file cleanup must not turn the typed
                    // migration failure into an exception or touch the source.
                }
            }
        }
    }

    private async Task<bool> IsMatchingMigrationMarkerAsync(CancellationToken cancellationToken)
    {
        var json = await File.ReadAllTextAsync(migrationMarkerPath!, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        return root.ValueKind == JsonValueKind.Object && root.EnumerateObject().Count() == 2
            && root.TryGetProperty("formatVersion", out var version) && version.ValueKind == JsonValueKind.Number
            && version.TryGetInt32(out var value) && value == 1
            && root.TryGetProperty("gameId", out var identity) && identity.ValueKind == JsonValueKind.String
            && string.Equals(identity.GetString(), GameId, StringComparison.Ordinal);
    }

    private async Task WriteMigrationMarkerAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(migrationMarkerPath))
        {
            if (!await IsMatchingMigrationMarkerAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidDataException("An unrecognized checkpoint migration marker cannot be replaced.");
            }

            return;
        }

        var temporaryPath = migrationMarkerPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(migrationMarkerPath!)!);
            await using (var stream = File.Create(temporaryPath))
            await using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                writer.WriteNumber("formatVersion", 1);
                writer.WriteString("gameId", GameId);
                writer.WriteEndObject();
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            try
            {
                File.Move(temporaryPath, migrationMarkerPath!, overwrite: false);
            }
            catch (IOException) when (File.Exists(migrationMarkerPath))
            {
                if (!await IsMatchingMigrationMarkerAsync(cancellationToken).ConfigureAwait(false))
                {
                    throw;
                }
            }
        }
        finally
        {
            try
            {
                DeleteIfPresent(temporaryPath);
            }
            catch (Exception exception) when (StorageFailure.IsRecoverable(exception))
            {
                // Only a private temporary marker may remain; the source and any
                // already committed marker retain their original bytes.
            }
        }
    }

    /// <summary>按快照固定的条目设置验证路径、远端版本/basis 与下载源。</summary>
    internal static bool MatchesSnapshot(DownloadTaskState state, LauncherStatusSnapshot snapshot, IGameInstallationPath installationPath)
    {
        var game = snapshot.GameSettings;
        var config = snapshot.Remote.GameConfig;
        return config is not null
            && state.PatchUrlGroup is PatchUrlGroups.Official or PatchUrlGroups.Cafe
            && (!string.Equals(snapshot.GameId, LauncherProfiles.StellaSoraChina.RuntimeId, StringComparison.Ordinal)
                || state.PatchUrlGroup == PatchUrlGroups.Official)
            && !string.IsNullOrWhiteSpace(config.GameLatestVersion)
            && !string.IsNullOrWhiteSpace(config.GameLatestFilePath)
            && !string.IsNullOrWhiteSpace(game.GamePath)
            && !string.IsNullOrWhiteSpace(state.GamePath)
            && string.Equals(state.Version, config.GameLatestVersion, StringComparison.Ordinal)
            && string.Equals(state.Basis, config.GameLatestFilePath, StringComparison.Ordinal)
            && string.Equals(installationPath.NormalizeGamePath(state.GamePath), installationPath.NormalizeGamePath(game.GamePath), PathComparison)
            && string.Equals(state.PatchUrlGroup, game.PatchUrlGroup, StringComparison.Ordinal);
    }

    /// <summary>旧状态仅凭经过状态服务验证的 BA 配置/清单身份与规范路径恢复。</summary>
    internal static async Task<bool> HasVerifiedLegacyIdentityAsync(LauncherStatusSnapshot snapshot,
        ILocalInstallationStateStore installationStore, IGameInstallationPath installationPath, CancellationToken cancellationToken)
        => await GetLegacyIdentityFailureAsync(snapshot, installationStore, installationPath, cancellationToken).ConfigureAwait(false) is null;

    private static async Task<LegacyCheckpointMigrationResult?> GetLegacyIdentityFailureAsync(LauncherStatusSnapshot snapshot,
        ILocalInstallationStateStore installationStore, IGameInstallationPath installationPath, CancellationToken cancellationToken)
    {
        if (!string.Equals(snapshot.GameId, LauncherProfiles.BlueArchiveJapan.RuntimeId, StringComparison.Ordinal))
        {
            return LegacyCheckpointMigrationResult.IdentityUnverified;
        }

        var path = installationPath.NormalizeGamePath(snapshot.GameSettings.GamePath);
        var local = await installationStore.ReadAsync(path, cancellationToken).ConfigureAwait(false);
        if (local.Kind == LocalInstallationStateKind.IoFailure)
        {
            return LegacyCheckpointMigrationResult.IoFailure;
        }

        var verified = local.Kind == LocalInstallationStateKind.Valid
            && local.GameConfig is not null && local.Manifest is not null
            && string.Equals(local.GameConfig.Tag, LauncherProfiles.BlueArchiveJapan.Tag, StringComparison.Ordinal)
            && string.Equals(local.Manifest.Name, LauncherProfiles.BlueArchiveJapan.Tag, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(local.GameConfig.Version)
            && string.Equals(local.GameConfig.Version, local.Manifest.Version, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(local.GamePath)
            && string.Equals(installationPath.NormalizeGamePath(local.GamePath), path, PathComparison);
        return verified ? null : LegacyCheckpointMigrationResult.IdentityUnverified;
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static DownloadTaskState? ParseState(string json, string expectedGameId, bool allowLegacy)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || root.EnumerateObject().Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != root.EnumerateObject().Count())
        {
            return null;
        }

        if (root.EnumerateObject().Any(property =>
            (string.Equals(property.Name, "formatVersion", StringComparison.OrdinalIgnoreCase) && property.Name != "formatVersion")
            || (string.Equals(property.Name, "gameId", StringComparison.OrdinalIgnoreCase) && property.Name != "gameId")))
        {
            return null;
        }

        var hasVersion = root.TryGetProperty("formatVersion", out var versionElement);
        var hasIdentity = root.TryGetProperty("gameId", out var identityElement);
        var version = 0;
        if (hasVersion && (versionElement.ValueKind != JsonValueKind.Number || !versionElement.TryGetInt32(out version)))
        {
            return null;
        }

        if (hasIdentity && identityElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var identity = hasIdentity ? identityElement.GetString() : "";
        var isLegacy = version == 0 && string.IsNullOrEmpty(identity);
        if (!(allowLegacy && isLegacy)
            && (version != CurrentFormatVersion || !string.Equals(identity, expectedGameId, StringComparison.Ordinal)))
        {
            return null;
        }

        return JsonSerializer.Deserialize(json, UiStorageJsonContext.Default.DownloadTaskState);
    }

    private static void DeleteIfPresent(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}

/// <summary>旧根检查点的安全迁移结果；非成功时原件保留，交给 Shell 显式反馈。</summary>
internal enum LegacyCheckpointMigrationResult
{
    Migrated,
    AlreadyMigrated,
    DestinationExists,
    LegacyMissing,
    WrongTarget,
    IdentityUnverified,
    IncompatibleCheckpoint,
    InvalidCheckpoint,
    InvalidMigrationMarker,
    IoFailure
}
