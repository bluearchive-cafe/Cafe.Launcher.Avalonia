using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cafe.Launcher.Core.Constants;
using Cafe.Launcher.Core.Helpers;
using Cafe.Launcher.Core.Models;

namespace Cafe.Launcher.Core.Services;

/// <summary>
/// 纯设置文档编解码：识别格式版本、迁移旧形状、按顶层或游戏条目降解单个类型错误的字段。
/// 不做归一化、备份或磁盘操作——那些是 <see cref="LauncherSettingsService"/> 的职责。
/// </summary>
/// <remarks>
/// 无法安全解释的文档抛出 <see cref="SettingsFormatException"/> 而不是降级：把未知版本或
/// 未知游戏条目当成默认值读出，下一次保存就会用默认值覆盖用户文件。
/// </remarks>
internal static class SettingsDocumentCodec
{
    /// <summary>当前唯一可写的版本化设置格式。</summary>
    internal const int CurrentFormatVersion = SettingsFormatVersions.Current;

    /// <summary>读取版本化设置或迁移无版本的旧单游戏文档；无法安全解释的数据明确拒绝。</summary>
    internal static SettingsDocumentReadResult Read(string json)
    {
        EnsureVersionedObjectsHaveUniqueProperties(json);
        return ReadDocument(JsonNode.Parse(json));
    }

    /// <summary>Only a syntax failure may be recovered by backing up the file before writing.</summary>
    internal static SettingsDocumentReadResult? ReadForWrite(string json)
    {
        JsonNode? node;
        try
        {
            EnsureVersionedObjectsHaveUniqueProperties(json);
            node = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }

        return ReadDocument(node);
    }

    /// <summary>仅写当前格式与受支持条目；根模型的兼容投影不产生旧平铺字段。</summary>
    internal static string Serialize(LauncherSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ValidateForWrite(settings);
        return JsonSerializer.Serialize(settings, CoreStorageJsonContext.Default.LauncherSettings);
    }

    /// <summary>校验将要落盘的值；实现与 <see cref="Serialize"/> 共用，避免两份规则漂移。</summary>
    internal static void ValidateForWrite(LauncherSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.FormatVersion != CurrentFormatVersion)
        {
            throw new SettingsFormatException("unsupported-version", settings.FormatVersion);
        }

        if (settings.Games is null)
        {
            throw new SettingsFormatException("invalid-structure", settings.FormatVersion);
        }

        foreach (var (gameId, game) in settings.Games)
        {
            if (!LauncherProfiles.TryGetSupportedGame(gameId, out _))
            {
                throw new SettingsFormatException("unknown-game", settings.FormatVersion);
            }

            if (game is null)
            {
                throw new SettingsFormatException("invalid-structure", settings.FormatVersion);
            }
        }

        foreach (var profile in LauncherProfiles.SupportedGames)
        {
            if (!settings.Games.ContainsKey(profile.RuntimeId))
            {
                throw new SettingsFormatException("invalid-structure", settings.FormatVersion);
            }
        }
    }

    private static SettingsDocumentReadResult ReadDocument(JsonNode? node)
    {
        if (node is not JsonObject root)
        {
            throw new SettingsFormatException(
                "invalid-root",
                null,
                detail: $"root is {DescribeRootKind(node)} instead of an object");
        }

        if (!root.TryGetPropertyValue("formatVersion", out var versionNode))
        {
            // A nested document missing its discriminator must never be treated as
            // old flat settings and rewritten after silently discarding game data.
            if (root.ContainsKey("games") || root.ContainsKey("FormatVersion"))
            {
                throw new SettingsFormatException("missing-version", null);
            }

            return ReadLegacy(root);
        }

        if (versionNode is not JsonValue versionValue
            || versionValue.GetValueKind() != JsonValueKind.Number
            || !versionValue.TryGetValue<int>(out var version))
        {
            throw new SettingsFormatException("invalid-version", null);
        }

        if (version is not (SettingsFormatVersions.GamesWithBackgrounds or CurrentFormatVersion))
        {
            throw new SettingsFormatException("unsupported-version", version);
        }

        ValidateCurrentStructure(root, version);
        var degradedFields = new List<string>();
        var settings = DeserializeTolerant(root, version, degradedFields);
        if (version == SettingsFormatVersions.GamesWithBackgrounds)
        {
            MigrateBackgrounds(settings);
        }

        return new SettingsDocumentReadResult(
            settings,
            degradedFields,
            RequiresMigration: version != CurrentFormatVersion,
            SourceFormatVersion: version);
    }

    /// <summary>
    /// 旧文档没有版本号：全局字段与单游戏字段写在同一层。把整份文档同时读成根与一个游戏条目，
    /// 再交给与版本 2 相同的背景迁移，得到当前形状。
    /// </summary>
    private static SettingsDocumentReadResult ReadLegacy(JsonObject root)
    {
        var degradedFields = new List<string>();
        LauncherSettings settings;
        GameSettings game;
        while (true)
        {
            try
            {
                settings = root.Deserialize(CoreStorageJsonContext.Default.LauncherSettings)
                    ?? throw new SettingsFormatException("invalid-structure", null);
                game = root.Deserialize(CoreStorageJsonContext.Default.GameSettings)
                    ?? throw new SettingsFormatException("invalid-structure", null);
                break;
            }
            catch (JsonException exception)
                when (TryRemoveOffendingField(root, exception.Path, out var removed))
            {
                degradedFields.Add(removed);
            }
        }

        ApplyLegacyString(root, "gamePath", "GamePath", value => game.GamePath = value);
        ApplyLegacyString(root, "launchCheckMode", "LaunchCheckMode", value => game.LaunchCheckMode = value);
        settings.FormatVersion = CurrentFormatVersion;
        settings.LastViewedGameId = LauncherProfiles.BlueArchiveJapan.RuntimeId;
        settings.Games ??= new Dictionary<string, GameSettings>(StringComparer.Ordinal);
        settings.Games[LauncherProfiles.BlueArchiveJapan.RuntimeId] = game;
        MigrateBackgrounds(settings);
        return new SettingsDocumentReadResult(
            settings,
            degradedFields,
            RequiresMigration: true,
            SourceFormatVersion: null);
    }

    /// <summary>
    /// 反序列化时只丢弃发生类型错误的那个字段：顶层字段直接移除，游戏条目内的字段只移除该字段。
    /// 无法归因到单个字段的失败照常上抛，由写入边界决定拒绝还是备份。
    /// </summary>
    private static LauncherSettings DeserializeTolerant(
        JsonObject root,
        int version,
        List<string> degradedFields)
    {
        while (true)
        {
            try
            {
                return root.Deserialize(CoreStorageJsonContext.Default.LauncherSettings)
                    ?? throw new SettingsFormatException("invalid-structure", version);
            }
            catch (JsonException exception)
                when (TryRemoveOffendingField(root, exception.Path, out var removed))
            {
                degradedFields.Add(removed);
            }
            catch (JsonException exception)
            {
                // Current documents cannot fall through the legacy default path in the
                // file service, which would make a later save destructive.
                throw new SettingsFormatException("invalid-values", version, exception);
            }
        }
    }

    /// <summary>
    /// 旧文档的背景是全局的。迁移把当时的全局值复制进每个游戏条目，但保留全局值作为
    /// 「未开启独立背景」时继承的默认背景；因此副本的 <see cref="GameSettings.UseBackgroundOverride"/>
    /// 保持关闭——两份值不会同时成为真值。
    /// </summary>
    private static void MigrateBackgrounds(LauncherSettings settings)
    {
        foreach (var game in settings.Games.Values)
        {
            game.UseBackgroundOverride = false;
            game.BackgroundSource = !string.IsNullOrEmpty(settings.CustomBackgroundPath)
                ? BackgroundSources.Custom
                : settings.BackgroundSource;
            game.CustomBackgroundPath = settings.CustomBackgroundPath;
            game.BackgroundFit = settings.BackgroundFit;
            game.BackgroundFillColor = settings.BackgroundFillColor;
            game.BackgroundVideoVolume = settings.BackgroundVideoVolume;
            game.BackgroundVideoMuted = settings.BackgroundVideoMuted;
        }

        settings.FormatVersion = CurrentFormatVersion;
    }

    private static void ApplyLegacyString(
        JsonObject root,
        string currentName,
        string legacyName,
        Action<string> apply)
    {
        // An explicitly present camelCase value (including null) takes precedence,
        // matching the former single-game reader's legacy compatibility rule.
        if (!root.ContainsKey(currentName)
            && root.TryGetPropertyValue(legacyName, out var value)
            && value is JsonValue jsonValue
            && jsonValue.TryGetValue<string>(out var text))
        {
            apply(text);
        }
    }

    private static void ValidateCurrentStructure(JsonObject root, int version)
    {
        if (!root.TryGetPropertyValue("games", out var gamesNode) || gamesNode is not JsonObject games)
        {
            throw new SettingsFormatException("invalid-structure", version);
        }

        foreach (var (gameId, game) in games)
        {
            // Until the model can preserve opaque extension data, refusing an
            // unknown identity protects its original bytes from a lossy save.
            if (!LauncherProfiles.TryGetSupportedGame(gameId, out _))
            {
                throw new SettingsFormatException("unknown-game", version);
            }

            if (game is not JsonObject)
            {
                throw new SettingsFormatException("invalid-structure", version);
            }
        }

        foreach (var profile in LauncherProfiles.SupportedGames)
        {
            if (!games.ContainsKey(profile.RuntimeId))
            {
                throw new SettingsFormatException("invalid-structure", version);
            }
        }
    }

    /// <summary>
    /// 从 <see cref="JsonException.Path"/> 定位出错字段并移除：<c>$.themeMode</c> 移除顶层字段，
    /// <c>$.games.&lt;id&gt;.gameRuntime.runner</c> 只移除该游戏的 <c>gameRuntime</c>。
    /// 返回 false 表示失败无法归因，调用方按结构错误处理。
    /// </summary>
    private static bool TryRemoveOffendingField(JsonObject root, string? path, out string removed)
    {
        removed = "";
        if (path is null || !path.StartsWith("$.", StringComparison.Ordinal))
        {
            return false;
        }

        var segments = path[2..].Split('.');
        if (segments.Length == 0 || segments[0].Length == 0)
        {
            return false;
        }

        if (segments[0] != "games")
        {
            removed = TrimIndexSuffix(segments[0]);
            return root.Remove(removed);
        }

        if (segments.Length < 3
            || !root.TryGetPropertyValue("games", out var gamesNode)
            || gamesNode is not JsonObject games
            || !games.TryGetPropertyValue(segments[1], out var gameNode)
            || gameNode is not JsonObject game)
        {
            return false;
        }

        removed = $"games.{segments[1]}.{TrimIndexSuffix(segments[2])}";
        return game.Remove(TrimIndexSuffix(segments[2]));
    }

    private static string TrimIndexSuffix(string segment)
    {
        var end = segment.IndexOf('[');
        return end >= 0 ? segment[..end] : segment;
    }

    /// <summary>
    /// 重复属性名会让解析器静默只保留最后一个值：隐藏的版本号或游戏条目会被下一次保存永久抹掉。
    /// 因此根对象、<c>games</c> 映射与每个游戏条目都按无法安全解释的文档整份拒绝。
    /// </summary>
    private static void EnsureVersionedObjectsHaveUniqueProperties(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var version = root.TryGetProperty("formatVersion", out var versionElement)
            && versionElement.ValueKind == JsonValueKind.Number
            && versionElement.TryGetInt32(out var parsed)
                ? parsed
                : (int?)null;

        EnsureUniqueProperties(root, null);
        if (!root.TryGetProperty("games", out var games) || games.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        EnsureUniqueProperties(games, version);
        foreach (var game in games.EnumerateObject())
        {
            if (game.Value.ValueKind == JsonValueKind.Object)
            {
                EnsureUniqueProperties(game.Value, version);
            }
        }
    }

    private static void EnsureUniqueProperties(JsonElement element, int? version)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Add(property.Name))
            {
                throw new SettingsFormatException("duplicate-property", version);
            }
        }
    }

    private static string DescribeRootKind(JsonNode? node) => node switch
    {
        null => "null",
        JsonArray => "Array",
        JsonObject => "Object",
        JsonValue value => value.GetValueKind().ToString(),
        _ => node.GetValueKind().ToString()
    };
}

/// <summary>读取结果；迁移标识交由唯一设置写入边界决定备份与落盘时机。</summary>
internal sealed record SettingsDocumentReadResult(
    LauncherSettings Settings,
    IReadOnlyList<string> DegradedFields,
    bool RequiresMigration,
    int? SourceFormatVersion = null);

/// <summary>无法安全读取或写出的设置格式；不属于可恢复 IO/JSON 语法错误，禁止默认值覆盖。</summary>
internal sealed class SettingsFormatException : Exception
{
    /// <summary>以稳定失败代码及可选原始版本描述拒绝原因，不携带文档内容。</summary>
    internal SettingsFormatException(
        string failureCode,
        int? formatVersion,
        Exception? innerException = null,
        string? detail = null)
        : base(BuildMessage(failureCode, formatVersion, detail), innerException)
    {
        FailureCode = failureCode;
        FormatVersion = formatVersion;
        Detail = detail;
    }

    /// <summary>稳定的格式失败代码，不含原始设置、路径或用户数据。</summary>
    internal string FailureCode { get; }

    /// <summary>可解析的文档版本；不存在或不是整数时为空。</summary>
    internal int? FormatVersion { get; }

    /// <summary>给人看的补充说明（如根节点种类）；不含用户数据。</summary>
    internal string? Detail { get; }

    private static string BuildMessage(string failureCode, int? formatVersion, string? detail)
    {
        var version = formatVersion?.ToString(CultureInfo.InvariantCulture) ?? "missing";
        return detail is null
            ? $"Settings document rejected: {failureCode}; format version: {version}."
            : $"Settings document rejected: {failureCode} ({detail}); format version: {version}.";
    }
}
