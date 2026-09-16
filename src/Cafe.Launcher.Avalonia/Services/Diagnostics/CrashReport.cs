using System;
using System.Globalization;
using System.Text.Json.Serialization;
using Cafe.Launcher.Avalonia.Constants;

namespace Cafe.Launcher.Avalonia.Services.Diagnostics;

/// <summary>Serializable snapshot describing one unrecoverable launcher failure.</summary>
public sealed record CrashReport
{
    /// <summary>
    /// 三处快照构造（正常持久化路径、日志不可用时的瞬态报告、读不到快照时的占位报告）共用的
    /// 构建身份字段：版本、提交、UI 区域性。OS 描述、异常类型与细节三者各不相同，由调用方传入。
    /// </summary>
    /// <remarks>
    /// 构建身份必须只有一处：版本与提交来自 <see cref="BuildInfo"/>，漏填其一会让用户回报的
    /// 快照指不到具体构建，而这类字段此前正是靠逐处手抄保持一致的。
    /// </remarks>
    public static CrashReport Build(
        string id,
        DateTimeOffset occurredAt,
        string source,
        string operatingSystem,
        string exceptionType,
        string technicalDetails) => new()
        {
            Id = id,
            OccurredAt = occurredAt,
            Source = source,
            AppVersion = BuildInfo.LauncherVersion,
            BuildSha = BuildInfo.CommitSha,
            OperatingSystem = operatingSystem,
            UiCulture = CultureInfo.CurrentUICulture.Name,
            ExceptionType = exceptionType,
            TechnicalDetails = technicalDetails
        };

    public required string Id { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }

    public required string Source { get; init; }

    public required string AppVersion { get; init; }

    /// <summary>Commit the crashing build was produced from; identifies the exact build.</summary>
    public required string BuildSha { get; init; }

    public required string OperatingSystem { get; init; }

    public required string UiCulture { get; init; }

    public required string ExceptionType { get; init; }

    public required string TechnicalDetails { get; init; }

    /// <summary>
    /// Absolute path of the persisted snapshot. Runtime-only: every reader receives the path from
    /// its caller (the reporter gets it as a command-line argument, <see cref="CrashReportStore.TryRead"/>
    /// re-derives it from its argument), so serializing it would only add the user's directory —
    /// and with it the OS account name — to a document that is meant to be share-safe.
    /// </summary>
    [JsonIgnore]
    public string SnapshotPath { get; init; } = "";
}
