using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    private static readonly JsonSerializerOptions jsonOptions = JsonDefaults.Indented;

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
            var result = ParseSettings(json);
            if (result.StructureProblem is not null || result.DegradedFields.Count > 0)
            {
                await ReportSettingsRecoveryAsync(result).ConfigureAwait(false);
            }

            return result.Settings;
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
    /// Reads the settings document without letting one malformed value abort the whole file:
    /// a non-object root yields defaults, and a value whose type does not match its setting is
    /// removed so every other field (game path, theme, UID) survives. Legacy capitalized names
    /// are applied only when their current name is absent, as before.
    /// </summary>
    private SettingsReadResult ParseSettings(string json)
    {
        var node = JsonNode.Parse(json);
        if (node is not JsonObject root)
        {
            return new SettingsReadResult(
                CreateDefaultSettings(),
                [],
                $"root is {DescribeRootKind(node)} instead of an object");
        }

        var degradedFields = new List<string>();
        while (true)
        {
            LauncherSettings? parsed = null;
            try
            {
                parsed = JsonSerializer.Deserialize<LauncherSettings>(root, jsonOptions);
            }
            catch (JsonException exception)
            {
                var propertyName = GetTopLevelPropertyName(exception.Path);
                if (propertyName is null || !root.Remove(propertyName))
                {
                    return new SettingsReadResult(
                        CreateDefaultSettings(),
                        degradedFields,
                        "contains a value that cannot be attributed to a single setting");
                }

                degradedFields.Add(propertyName);
                continue;
            }

            if (parsed is null)
            {
                return new SettingsReadResult(
                    CreateDefaultSettings(),
                    degradedFields,
                    "did not deserialize to a settings object");
            }

            ApplyLegacyFields(parsed, root);
            return new SettingsReadResult(NormalizeSettings(parsed), degradedFields, null);
        }
    }

    /// <summary>
    /// First path segment of a <see cref="JsonException.Path"/> (for example
    /// <c>$.themeColorPalette[0]</c> → <c>themeColorPalette</c>). Returns null when the failure
    /// is not tied to a top-level property, which the caller treats as a structural failure.
    /// </summary>
    private static string? GetTopLevelPropertyName(string? path)
    {
        if (path is null || !path.StartsWith("$.", StringComparison.Ordinal))
        {
            return null;
        }

        var name = path.AsSpan(2);
        var end = name.IndexOfAny('.', '[');
        return (end >= 0 ? name[..end] : name).ToString();
    }

    private static string DescribeRootKind(JsonNode? node) =>
        node is null ? "null" : node.GetValueKind().ToString();

    /// <summary>
    /// Backs up the file that could not be read as written and records one diagnostic entry.
    /// Both are side effects of recovery, so they complete even when the refresh that triggered
    /// the read has already been cancelled.
    /// </summary>
    private async Task ReportSettingsRecoveryAsync(SettingsReadResult result)
    {
        await CopySettingsFileToBackupAsync().ConfigureAwait(false);
        if (diagnostics is null)
        {
            return;
        }

        var message = result.StructureProblem is not null
            ? $"settings.json {result.StructureProblem}; defaults were applied."
            : $"settings.json contained invalid values for {string.Join(", ", result.DegradedFields)}; those values were reset to defaults.";

        await diagnostics.ErrorAsync(
            "Settings",
            message,
            new InvalidDataException(message),
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

    private static void ApplyLegacyFields(LauncherSettings settings, JsonObject root)
    {
        if (!root.ContainsKey("gamePath")
            && TryGetString(root, "GamePath") is { } legacyGamePath)
        {
            settings.GamePath = legacyGamePath;
        }

        if (!root.ContainsKey("launchCheckMode")
            && TryGetString(root, "LaunchCheckMode") is { } legacyLaunchCheckMode)
        {
            settings.LaunchCheckMode = legacyLaunchCheckMode;
        }
    }

    private static string? TryGetString(JsonObject root, string propertyName)
    {
        return root.TryGetPropertyValue(propertyName, out var node)
            && node is JsonValue value
            && value.TryGetValue<string>(out var text)
                ? text
                : null;
    }

    /// <summary>
    /// What a read recovered: the settings to use, the top-level fields that were dropped
    /// because their value had the wrong type, and a description when the whole document
    /// (rather than a single field) had to be discarded.
    /// </summary>
    private sealed record SettingsReadResult(
        LauncherSettings Settings,
        IReadOnlyList<string> DegradedFields,
        string? StructureProblem);

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
            await AtomicJsonFileStore.WriteAsync(
                path,
                normalized,
                jsonOptions,
                cancellationToken).ConfigureAwait(false);
            return normalized;
        }
        finally
        {
            writeLock.Release();
        }
    }

    /// <summary>
    /// Normalize all setting values to valid codes, apply defaults for invalid values,
    /// normalize colors, trim UIDs, and return a deep-cloned copy.
    /// </summary>
    private LauncherSettings NormalizeSettings(LauncherSettings settings)
    {
        settings = settings.DeepClone();
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
        settings.GamePath ??= "";
        settings.ResourcePanelUid = settings.ResourcePanelUid?.Trim() ?? "";

        settings.GameRuntime ??= new GameRuntimeSettings();
        // Native execution cannot work on Linux; a persisted "native" there (legacy or
        // hand-edited) would leave the runner dropdown with no matching selection.
        settings.GameRuntime.Runner = settings.GameRuntime.Runner switch
        {
            GameRuntimeRunners.Native when isLinuxPlatform() => GameRuntimeRunners.Auto,
            GameRuntimeRunners.Native => GameRuntimeRunners.Native,
            GameRuntimeRunners.Umu => GameRuntimeRunners.Umu,
            GameRuntimeRunners.Wine => GameRuntimeRunners.Wine,
            _ => GameRuntimeRunners.Auto
        };
        settings.GameRuntime.RunnerPath = NormalizeOptionalPath(settings.GameRuntime.RunnerPath);
        settings.GameRuntime.PrefixPath = NormalizeOptionalPath(settings.GameRuntime.PrefixPath);
        settings.GameRuntime.ProtonPath = NormalizeOptionalPath(settings.GameRuntime.ProtonPath);

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
