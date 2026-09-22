using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Helpers;

namespace Cafe.Launcher.Avalonia.Services.GameRuntime;

/// <summary>
/// 「这个兼容前缀是哪次组合创建/使用的」（P1-E）：创建时间、最近运行器与版本、最近 Proton、
/// 最近一次启动时间与次数。只记录、不做任何自动迁移——版本变化不等于必须迁移，元数据也不保证
/// 前缀可回滚（计划 §5 P1-E）。自定义前缀的所有权仍在用户手里：这里只写启动器数据根，不往
/// 用户的 prefix 目录里塞文件。
/// </summary>
internal sealed record PrefixMetadata(
    string PrefixPath,
    string GameId,
    string RunnerId,
    string? RunnerVersion,
    string? ProtonPath,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastLaunchedAt,
    int LaunchCount);

/// <summary>
/// 最近一次启动所用前缀的元数据存储：单文件、每次启动覆盖，前缀变化即重置创建信息与计数。
/// 写入失败不影响启动。
/// </summary>
public sealed class PrefixMetadataStore
{
    private readonly LauncherDataRoot dataRoot;

    public PrefixMetadataStore(LauncherDataRoot dataRoot)
    {
        ArgumentNullException.ThrowIfNull(dataRoot);
        this.dataRoot = dataRoot;
    }

    internal string FilePath => dataRoot.PrefixMetadataPath;

    /// <summary>
    /// 记录一次启动使用的组合并返回元数据。与前一条记录同前缀时保留创建时间并累加次数，否则
    /// 视作新前缀重新开始。不做自动迁移。
    /// </summary>
    internal PrefixMetadata Record(
        string prefixPath,
        string gameId,
        string runnerId,
        string? runnerVersion,
        string? protonPath)
    {
        string normalized;
        try
        {
            normalized = Path.GetFullPath(prefixPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            normalized = prefixPath;
        }

        var now = DateTimeOffset.Now;
        var (createdAt, launchCount) = ReadPrevious(normalized);
        var metadata = new PrefixMetadata(
            normalized,
            gameId,
            runnerId,
            runnerVersion,
            protonPath,
            createdAt ?? now,
            now,
            launchCount + 1);
        TryWrite(metadata);
        return metadata;
    }

    private (DateTimeOffset? CreatedAt, int LaunchCount) ReadPrevious(string prefixPath)
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return (null, 0);
            }

            using var document = JsonDocument.Parse(File.ReadAllText(FilePath));
            var root = document.RootElement;
            if (!root.TryGetProperty("prefixPath", out var stored)
                || !string.Equals(stored.GetString(), prefixPath, PathComparison))
            {
                return (null, 0);
            }

            var createdAt = root.TryGetProperty("createdAt", out var created)
                && created.GetString() is { } text
                && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
                    ? (DateTimeOffset?)parsed
                    : null;
            var launchCount = root.TryGetProperty("launchCount", out var count) && count.TryGetInt32(out var value)
                ? value
                : 0;
            return (createdAt, launchCount);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return (null, 0);
        }
    }

    private void TryWrite(PrefixMetadata metadata)
    {
        try
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var payload = new
            {
                prefixPath = metadata.PrefixPath,
                gameId = metadata.GameId,
                runnerId = metadata.RunnerId,
                runnerVersion = metadata.RunnerVersion,
                protonPath = metadata.ProtonPath,
                launcherVersion = BuildInfo.LauncherVersion,
                createdAt = metadata.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
                lastLaunchedAt = metadata.LastLaunchedAt.ToString("O", CultureInfo.InvariantCulture),
                launchCount = metadata.LaunchCount
            };
            File.WriteAllText(FilePath, JsonSerializer.Serialize(payload, JsonDefaults.Indented));
        }
        catch (Exception exception) when (StorageFailure.IsRecoverable(exception))
        {
            // 诊断增强：元数据写不进去不影响启动。
        }
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
