using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Core.Constants;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Cafe.Launcher.Core.Services.Diagnostics;

/// <summary>
/// Centralised logging engine. All error, warning, and informational messages
/// flow through this singleton and are persisted to a single rotating log file
/// (backed by Serilog with an async sink wrapper).
/// </summary>
public sealed class UnifiedLogger : IDisposable
{
    private readonly Logger serilogLogger;
    private readonly LoggingLevelSwitch levelSwitch;
    private readonly string logFilePath;
    private readonly string launcherVersion;
    private readonly string commitSha;
    private readonly string buildConfiguration;
    private bool disposed;

    /// <summary>
    /// 日志目录必须显式给出：进程根由组合根或 ADR-019 保护的 pre-DI 路径解析，
    /// 日志器本身不再是「谁都能读一次」的静态消费点。
    /// </summary>
    /// <param name="buildIdentity">
    /// 宿主注入的构建标识：版本、提交与配置写进日志头与 Serilog 属性。此前读宿主
    /// <c>BuildInfo</c>，迁入 Core 后改为注入（缺省时留空，测试与辅助宿主不必伪造）。
    /// </param>
    public UnifiedLogger(string logDirectory, LauncherBuildIdentity? buildIdentity = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);
        logFilePath = Path.Combine(logDirectory, GamePaths.UnifiedLogFileName);
        launcherVersion = buildIdentity?.LauncherVersion ?? "";
        commitSha = buildIdentity?.CommitSha ?? "";
        buildConfiguration = buildIdentity?.BuildConfiguration ?? "";

        // Verbose in Debug builds so developers see everything; Information in
        // Release so production logs stay lean. The switch can be adjusted at
        // runtime via SetMinimumLevel().
        levelSwitch = new LoggingLevelSwitch(
#if DEBUG
            LogEventLevel.Verbose
#else
            LogEventLevel.Information
#endif
        );

        serilogLogger = new LoggerConfiguration()
            .MinimumLevel.ControlledBy(levelSwitch)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("AppVersion", launcherVersion)
            .Enrich.WithProperty("CommitSha", commitSha)
            .WriteTo.Async(a => a.File(
                logFilePath,
                formatProvider: CultureInfo.InvariantCulture,
                fileSizeLimitBytes: 5L * 1024 * 1024,     // 5 MB
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: 4,                  // current + 3 rotated
                rollingInterval: RollingInterval.Infinite,
                shared: true,                               // allow log viewer to read while writing
                outputTemplate: "{Timestamp:O} [{Level:u3}] [{LogTitle}] {Message}{NewLine}{Exception}"),
                bufferSize: 10000)
            .CreateLogger();

        // Route Serilog's own diagnostics to Debug output so sink failures
        // (e.g. disk full) are visible during development and debugging.
        Serilog.Debugging.SelfLog.Enable(msg => Debug.WriteLine($"[Serilog.SelfLog] {msg}"));
    }

    // ── diagnostics / testing ──────────────────────────────────────────

    /// <summary>
    /// 当前统一日志文件路径。这是<strong>词干</strong>而不是「一定存在的那个文件」：日志一旦按大小
    /// 轮转（<c>rollOnFileSizeLimit</c> + <c>RollingInterval.Infinite</c>），Serilog 会把后续内容写进
    /// <c>unified_001.log</c> 一类带序号的名字，基名从此不再被创建。需要读「现在正在写的那个文件」的
    /// 调用点必须走 <see cref="ResolveActiveLogFile"/>，否则 5 MB 之后日志查看器会全空、导出会抛异常。
    /// </summary>
    public string LogFilePath => logFilePath;

    /// <summary>
    /// 解析当前<strong>存在</strong>的统一日志文件：优先未经轮转的基名，否则取目录里最新的
    /// <c>unified_*.log</c> 兄弟文件。日志目录读不到或没有任何候选时返回 <c>null</c>（调用点按
    /// 「暂无日志」处理，而不是把基名当成一个必然存在的文件）。
    /// </summary>
    /// <param name="expectedPath">
    /// <see cref="LogFilePath"/> 给出的基名路径。词干与扩展名由它推导，因此改 <see cref="GamePaths.UnifiedLogFileName"/>
    /// 会带着轮转命名一起走。
    /// </param>
    public static string? ResolveActiveLogFile(string expectedPath)
    {
        if (string.IsNullOrWhiteSpace(expectedPath))
        {
            return null;
        }

        var existing = ExistingLogFiles(expectedPath);
        if (existing.Count == 0)
        {
            return null;
        }

        if (File.Exists(expectedPath))
        {
            return expectedPath;
        }

        // 基名已经不再写入（轮转后就是这种状态），取写时间最新的那个兄弟文件。
        return existing
            .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
            .ThenByDescending(path => path, StringComparer.Ordinal)
            .First();
    }

    /// <summary>
    /// 目录里现有的统一日志文件：基名（若在）加上所有 <c>unified_*.log</c> 兄弟文件。
    /// 供导出面板枚举「基名 + 轮转文件」使用，避免第二处自己拼序号名。
    /// </summary>
    public static IReadOnlyList<string> ExistingLogFiles(string expectedPath)
    {
        if (string.IsNullOrWhiteSpace(expectedPath))
        {
            return [];
        }

        var directory = Path.GetDirectoryName(expectedPath);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return [];
        }

        var stem = Path.GetFileNameWithoutExtension(expectedPath);
        var extension = Path.GetExtension(expectedPath);
        if (string.IsNullOrEmpty(stem) || string.IsNullOrEmpty(extension))
        {
            return [];
        }

        var files = new List<string>();
        try
        {
            if (File.Exists(expectedPath))
            {
                files.Add(expectedPath);
            }

            // 只认「词干 + 可选序号 + 原扩展名」：目录里同前缀的其他文件（例如用户手工留下的
            // unified.log.bak）不该被当成轮转产物读进日志查看器。
            foreach (var candidate in Directory.EnumerateFiles(directory, $"{stem}*{extension}"))
            {
                var name = Path.GetFileName(candidate);
                if (files.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                var middleLength = name.Length - stem.Length - extension.Length;
                if (middleLength < 0
                    || !name.StartsWith(stem, StringComparison.Ordinal)
                    || !name.EndsWith(extension, StringComparison.Ordinal))
                {
                    continue;
                }

                // 序号段必须全为数字：unified_001.log 是轮转产物，unified_notes.log 不是。
                // Serilog 的拼接是「词干 + _ + 序号 + 扩展名」（PathRoller），所以先吃掉那一个下划线。
                var sequence = name.AsSpan(stem.Length, middleLength);
                if (sequence.Length > 0 && sequence[0] == '_')
                {
                    sequence = sequence[1..];
                }

                if (middleLength == 0 || IsAllDigits(sequence))
                {
                    files.Add(candidate);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 读不到目录就是「没有日志可读」，不是错误：诊断读取本身不该把界面打断。
            return files.Count > 0 ? files : [];
        }

        return files;
    }

    /// <summary>序号段是否为纯数字（空段按纯数字处理：基名本身没有序号）。</summary>
    private static bool IsAllDigits(ReadOnlySpan<char> value)
    {
        foreach (var character in value)
        {
            if (!char.IsAsciiDigit(character))
            {
                return false;
            }
        }

        return true;
    }

    // ── public API ──────────────────────────────────────────────────────

    /// <summary>
    /// Adjusts the minimum log level at runtime without restarting the process.
    /// </summary>
    public void SetMinimumLevel(LogEventLevel level)
    {
        levelSwitch.MinimumLevel = level;
    }

    /// <summary>
    /// Returns the current minimum log level so callers (e.g. settings UI)
    /// can display it or persist it.
    /// </summary>
    public LogEventLevel MinimumLevel => levelSwitch.MinimumLevel;

    public async Task LogAsync(
        LogEntrySeverity severity,
        string title,
        string? message = null,
        Exception? exception = null,
        CancellationToken cancellationToken = default)
    {
        // Note: cancellationToken is checked here to support the caller's
        // cancellation needs, but logging is inherently fire-and-forget.
        // If the caller has already been cancelled, their diagnostic about
        // why they were cancelled is the most valuable log entry to keep.
        try
        {
            var level = severity switch
            {
                LogEntrySeverity.Verbose => LogEventLevel.Verbose,
                LogEntrySeverity.Debug => LogEventLevel.Debug,
                LogEntrySeverity.Info => LogEventLevel.Information,
                LogEntrySeverity.Warn => LogEventLevel.Warning,
                LogEntrySeverity.Error => LogEventLevel.Error,
                LogEntrySeverity.Fatal => LogEventLevel.Fatal,
                _ => LogEventLevel.Information
            };

            var msg = title;
            if (!string.IsNullOrEmpty(message))
                msg += "\n" + message;

            // Attach structured properties for searchability without changing
            // the human-readable log line.
            serilogLogger
                .ForContext("LogTitle", title)
                .ForContext("LogMessage", message)
                .Write(level, exception, msg);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Cancelled by caller — expected during shutdown.
            // Suppress the OperationCanceledException here since logging
            // must never crash the app.
        }
        catch
        {
            // Best-effort — logging must never crash the app.
        }
    }

    public async Task WriteSessionStartAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var version = launcherVersion;
            var commitSha = this.commitSha;
            var os = Environment.OSVersion.ToString();
            var framework = RuntimeInformation.FrameworkDescription;
            var buildConfig = buildConfiguration;

            var message = new StringBuilder();
            message.AppendLine("Session started");
            message.Append("Version: ").Append(version)
                   .Append("  CommitSha: ").Append(commitSha).AppendLine();
            message.Append("OS: ").Append(os)
                   .Append("  Framework: ").Append(framework).AppendLine();
            message.Append("BuildConfig: ").Append(buildConfig).AppendLine();

            serilogLogger
                .ForContext("LogTitle", "Session")
                .ForContext("SessionVersion", version)
                .ForContext("SessionCommitSha", commitSha)
                .ForContext("SessionOS", os)
                .ForContext("SessionFramework", framework)
                .ForContext("SessionBuildConfig", buildConfig)
                .Information(message.ToString());
        }
        catch
        {
            // Best-effort.
        }
    }

    public async Task WriteSessionEndAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            serilogLogger.ForContext("LogTitle", "Session").Information("Session ended");
        }
        catch
        {
            // Best-effort.
        }
    }

    // ── IDisposable ────────────────────────────────────────────────────

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        serilogLogger.Dispose();
        GC.SuppressFinalize(this);
    }
}
