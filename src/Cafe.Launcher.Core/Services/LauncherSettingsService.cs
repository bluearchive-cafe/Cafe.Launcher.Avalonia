using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Core.Constants;
using Cafe.Launcher.Core.Helpers;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Services.Diagnostics;
using Cafe.Launcher.Core;

namespace Cafe.Launcher.Core.Services;

/// <summary>
/// settings.json 的唯一读写入口：信号量串行化读写、原子写盘、
/// 未知/非法字段经 <c>NormalizeSettings</c> 兜底为有效默认值（向后兼容契约）。
/// 读取对结构非法与单字段类型不匹配降级为默认值并备份原文件，入口本身不抛异常。
/// 读取可以由任何模块直接调用；写入在生产代码里只由 <see cref="ISavedSettingsWriter"/>
/// 调用——它负责把落盘值同步给设置编辑器，绕过去就会让草稿与磁盘分叉。
/// </summary>
internal sealed class LauncherSettingsService : ILauncherSettingsService, IDisposable
{
    private readonly SemaphoreSlim writeLock = new(1, 1);
    private readonly LauncherDataRoot dataRoot;
    private readonly ILauncherDiagnostics? diagnostics;
    private readonly LauncherBuildIdentity? buildIdentity;
    private readonly Func<bool> isLinuxPlatform;

    /// <summary>
    /// <paramref name="isLinuxPlatform"/> 仅供测试注入平台判定；生产用运行时平台。
    /// <paramref name="buildIdentity"/> 决定首次落盘默认值的发布渠道（预发布→Beta）。
    /// </summary>
    public LauncherSettingsService(
        LauncherDataRoot dataRoot,
        ILauncherDiagnostics? diagnostics = null,
        Func<bool>? isLinuxPlatform = null,
        LauncherBuildIdentity? buildIdentity = null)
    {
        ArgumentNullException.ThrowIfNull(dataRoot);
        this.dataRoot = dataRoot;
        this.diagnostics = diagnostics;
        this.buildIdentity = buildIdentity;
        this.isLinuxPlatform = isLinuxPlatform ?? OperatingSystem.IsLinux;
    }

    public string SettingsPath => dataRoot.SettingsPath;

    private string SettingsBackupPath => dataRoot.SettingsBackupPath;

    public async Task<LauncherSettings> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(SettingsPath))
        {
            return CreateDefaultSettings();
        }

        try
        {
            var json = await File.ReadAllTextAsync(SettingsPath, cancellationToken).ConfigureAwait(false);
            var document = SettingsDocumentCodec.Read(json);
            var settings = NormalizeSettings(document.Settings);
            if (document.DegradedFields.Count > 0)
            {
                await ReportSettingsRecoveryAsync(document).ConfigureAwait(false);
            }

            return settings;
        }
        catch (SettingsFormatException exception)
        {
            await CopySettingsFileToBackupAsync().ConfigureAwait(false);
            await ReportStructureFailureAsync(exception).ConfigureAwait(false);
            return CreateDefaultSettings();
        }
        catch (Exception exception) when (StorageFailure.IsRecoverableOrInvalidJson(exception))
        {
            if (exception is JsonException)
            {
                // A truncated document is still evidence: keep a copy before a later save
                // replaces it, so a "my settings were reset" report has something to inspect.
                await CopySettingsFileToBackupAsync().ConfigureAwait(false);
            }

            if (diagnostics is not null)
            {
                await diagnostics.ErrorAsync(
                    "Settings",
                    "Reading settings.json failed; defaults were applied.",
                    exception,
                    CancellationToken.None).ConfigureAwait(false);
            }

            return CreateDefaultSettings();
        }
    }

    private LauncherSettings CreateDefaultSettings()
    {
        return LauncherSettings.CreateDefaults(buildIdentity);
    }

    /// <summary>
    /// 仅向唯一写入事务提供尚未提交的旧格式基底；读取不落盘，也不回填编辑器。
    /// 返回 null 表示盘上已是当前格式（或没有文件），调用方无需迁移。
    /// </summary>
    internal async Task<LauncherSettings?> ReadMigrationBaseAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(SettingsPath))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(SettingsPath, cancellationToken).ConfigureAwait(false);
        var document = SettingsDocumentCodec.ReadForWrite(json);
        return document is { RequiresMigration: true } migration
            ? NormalizeSettings(migration.Settings)
            : null;
    }

    /// <summary>
    /// Backs up the file that could not be read as written and records one diagnostic entry.
    /// Both are side effects of recovery, so they complete even when the refresh that triggered
    /// the read has already been cancelled.
    /// </summary>
    private async Task ReportSettingsRecoveryAsync(SettingsDocumentReadResult result)
    {
        await CopySettingsFileToBackupAsync().ConfigureAwait(false);
        if (diagnostics is null)
        {
            return;
        }

        var message =
            $"settings.json contained invalid values for {string.Join(", ", result.DegradedFields)}; those values were reset to defaults.";

        await diagnostics.ErrorAsync(
            "Settings",
            message,
            new InvalidDataException(message),
            CancellationToken.None).ConfigureAwait(false);
    }

    private async Task ReportStructureFailureAsync(SettingsFormatException exception)
    {
        if (diagnostics is null)
        {
            return;
        }

        var message = exception.Detail is not null
            ? $"settings.json {exception.Detail}; defaults were applied."
            : $"settings.json was rejected ({exception.FailureCode}); defaults were applied.";

        await diagnostics.ErrorAsync(
            "Settings",
            message,
            exception,
            CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// Copies the unreadable file to <see cref="SettingsBackupPath"/> through the same
    /// temporary-file-then-replace sequence as atomic writes, so an interrupted copy can never
    /// leave a half-written backup. Best effort: a failed backup must not turn a recoverable
    /// load into a failed one.
    /// </summary>
    private async Task CopySettingsFileToBackupAsync()
    {
        var temporaryPath = $"{SettingsBackupPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var source = File.OpenRead(SettingsPath))
            await using (var destination = File.Create(temporaryPath))
            {
                await source.CopyToAsync(destination, CancellationToken.None).ConfigureAwait(false);
                await destination.FlushAsync(CancellationToken.None).ConfigureAwait(false);
            }

            File.Move(temporaryPath, SettingsBackupPath, overwrite: true);
        }
        catch (Exception exception) when (StorageFailure.IsRecoverable(exception))
        {
            // 备份只是诊断线索：读不到原文件或写不进备份，都不能把可恢复的读取变成失败。
        }
        finally
        {
            FileCleanup.TryDelete(temporaryPath);
        }
    }

    /// <summary>
    /// Writes <paramref name="settings"/> and returns the normalized value that was written, so a
    /// caller that keeps the value as its own state never holds a copy that normalization changed.
    /// </summary>
    public async Task<LauncherSettings> SaveAsync(LauncherSettings settings, CancellationToken cancellationToken = default)
    {
        await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var normalized = NormalizeSettings(settings);
            var path = SettingsPath;
            _ = SettingsDocumentCodec.Serialize(normalized);

            // Re-read the discriminator at the commit boundary. A stale/default editor must never
            // overwrite a future document or a failed migration on disk; only a document we can
            // migrate, or one with no syntax at all, is backed up before being replaced.
            if (File.Exists(path))
            {
                var originalBytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
                var originalJson = Encoding.UTF8.GetString(originalBytes);
                if (originalJson.Length > 0 && originalJson[0] == '\uFEFF')
                {
                    originalJson = originalJson[1..];
                }

                var original = SettingsDocumentCodec.ReadForWrite(originalJson);
                var backup = original switch
                {
                    null => dataRoot.CorruptSettingsBackupPath,
                    { RequiresMigration: true, SourceFormatVersion: SettingsFormatVersions.GamesWithBackgrounds } =>
                        dataRoot.SettingsBackgroundMigrationBackupPath,
                    { RequiresMigration: true } => dataRoot.SettingsMigrationBackupPath,
                    _ => null
                };

                if (backup is not null)
                {
                    await BackupSettingsAsync(originalBytes, backup, cancellationToken).ConfigureAwait(false);
                }
            }

            await AtomicJsonFileStore.WriteAsync(
                path,
                normalized,
                CoreStorageJsonContext.Default.LauncherSettings,
                cancellationToken).ConfigureAwait(false);
            return normalized;
        }
        finally
        {
            writeLock.Release();
        }
    }

    private static async Task BackupSettingsAsync(
        byte[] originalBytes,
        string baseBackup,
        CancellationToken cancellationToken)
    {
        var backup = baseBackup;
        for (var index = 1; File.Exists(backup); index++)
        {
            var existing = await File.ReadAllBytesAsync(backup, cancellationToken).ConfigureAwait(false);
            if (originalBytes.AsSpan().SequenceEqual(existing))
            {
                return;
            }

            // A downgrade or restored settings can produce another legitimate source.
            // Keep every prior backup and reuse an identical one after an interrupted migration.
            backup = baseBackup + "." + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var temporaryBackup = $"{backup}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(temporaryBackup, originalBytes, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryBackup, backup);
        }
        finally
        {
            FileCleanup.TryDelete(temporaryBackup);
        }
    }

    /// <summary>
    /// Normalize all setting values to valid codes, apply defaults for invalid values,
    /// normalize colors, trim UIDs, and return a deep-cloned copy.
    /// </summary>
    private LauncherSettings NormalizeSettings(LauncherSettings settings)
    {
        SettingsDocumentCodec.ValidateForWrite(settings);

        settings = settings.DeepClone();
        foreach (var profile in LauncherProfiles.SupportedGames)
        {
            NormalizeGameSettings(profile.RuntimeId, settings.GetGameSettings(profile.RuntimeId));
        }

        if (!LauncherProfiles.TryGetSupportedGame(settings.LastViewedGameId, out _))
        {
            throw new SettingsFormatException("unknown-selection", settings.FormatVersion);
        }

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
        settings.BackgroundVideoVolume = Math.Clamp(settings.BackgroundVideoVolume, 0, 100);

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

    private void NormalizeGameSettings(string gameId, GameSettings settings)
    {
        var definition = LauncherProfiles.GetGameDefinition(gameId);
        settings.GamePath ??= "";
        settings.CustomBackgroundPath ??= "";
        settings.BackgroundSource = settings.BackgroundSource switch
        {
            BackgroundSources.Remote => BackgroundSources.Remote,
            BackgroundSources.Custom => BackgroundSources.Custom,
            _ => BackgroundSources.Bundled
        };
        settings.BackgroundFit = settings.BackgroundFit switch
        {
            BackgroundFits.Fill => BackgroundFits.Fill,
            BackgroundFits.Uniform => BackgroundFits.Uniform,
            _ => BackgroundFits.UniformToFill
        };
        settings.BackgroundFillColor = TryNormalizeColor(settings.BackgroundFillColor) ?? "#FF000000";
        settings.BackgroundVideoVolume = Math.Clamp(settings.BackgroundVideoVolume, 0, 100);
        settings.LaunchCheckMode = settings.LaunchCheckMode switch
        {
            "LocalManifest" or LaunchCheckModes.LocalManifest => LaunchCheckModes.LocalManifest,
            "RemoteManifest" or LaunchCheckModes.RemoteManifest => LaunchCheckModes.RemoteManifest,
            "None" or LaunchCheckModes.None => LaunchCheckModes.None,
            _ => LaunchCheckModes.LocalManifest
        };
        settings.PatchUrlGroup = settings.PatchUrlGroup == PatchUrlGroups.Cafe
            && definition.SupportsCafeDownloadSource
                ? PatchUrlGroups.Cafe
                : PatchUrlGroups.Official;
        settings.AfterLaunchBehavior = settings.AfterLaunchBehavior switch
        {
            AfterLaunchBehaviors.KeepOpen => AfterLaunchBehaviors.KeepOpen,
            AfterLaunchBehaviors.Exit => AfterLaunchBehaviors.Exit,
            _ => AfterLaunchBehaviors.Minimize
        };
        settings.ResourcePanelUid = definition.SupportsResourcePanel
            ? settings.ResourcePanelUid?.Trim() ?? ""
            : "";
        settings.ResourcePanelUidSource = definition.SupportsResourcePanel
            && settings.ResourcePanelUidSource == ResourcePanelUidSources.Custom
                ? ResourcePanelUidSources.Custom
                : ResourcePanelUidSources.Auto;
        settings.GameRuntime ??= new GameRuntimeSettings();
        settings.GameRuntime.Runner = settings.GameRuntime.Runner switch
        {
            GameRuntimeRunners.Native when !isLinuxPlatform() => GameRuntimeRunners.Native,
            GameRuntimeRunners.Umu => GameRuntimeRunners.Umu,
            GameRuntimeRunners.Wine => GameRuntimeRunners.Wine,
            _ => GameRuntimeRunners.Auto
        };
        settings.GameRuntime.RunnerPath = NormalizeOptionalPath(settings.GameRuntime.RunnerPath);
        settings.GameRuntime.PrefixPath = NormalizeOptionalPath(settings.GameRuntime.PrefixPath);
        settings.GameRuntime.ProtonPath = NormalizeOptionalPath(settings.GameRuntime.ProtonPath);
    }

    private static bool IsValidWindowDimension(double value) =>
        double.IsFinite(value) && value > 0;

    private static string? NormalizeOptionalPath(string? path)
    {
        path = path?.Trim();
        return string.IsNullOrEmpty(path) ? null : path;
    }

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
        (s => s.AfterLaunchBehavior, (s, v) => s.AfterLaunchBehavior = v,
            v => v is AfterLaunchBehaviors.KeepOpen or AfterLaunchBehaviors.Minimize or AfterLaunchBehaviors.Exit,
            AfterLaunchBehaviors.Minimize),
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
