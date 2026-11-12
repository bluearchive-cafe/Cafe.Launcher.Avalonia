using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Core.Services.Diagnostics;

namespace Cafe.Launcher.Testing;

/// <summary>
/// 记录诊断调用的替身：不写文件、不吞断言。四条路线（卸载完成日志、自更新失败日志、
/// 设置恢复日志、看护扫描超时）原来各养一份同形实现，这里保留一份，避免格式与成员
/// 名在不同测试里悄悄分叉。
/// </summary>
public sealed class RecordingDiagnostics : ILauncherDiagnostics
{
    /// <summary>每条已记录消息，格式与各严重度前缀和既有两个替身一致。</summary>
    public List<string> Messages { get; } = [];

    /// <summary>警告正文（不带前缀），看护超时一类断言直接看这里。</summary>
    public List<string> Warnings { get; } = [];

    /// <summary>错误条目的标题与消息，设置恢复断言需要分开看两者。</summary>
    public List<(string Title, string? Message)> Errors { get; } = [];

    public string LogFilePath => string.Empty;

    public LogEntrySeverity MinimumLevel { get; private set; } = LogEntrySeverity.Info;

    public void SetMinimumLevel(LogEntrySeverity severity) => MinimumLevel = severity;

    public Task DebugAsync(
        string title,
        string? message = null,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task VerboseAsync(
        string title,
        string? message = null,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task MessageAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        Messages.Add($"{title}: {message}");
        return Task.CompletedTask;
    }

    public Task WarningAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        Warnings.Add(message);
        Messages.Add($"Warn: {title}: {message}");
        return Task.CompletedTask;
    }

    public Task ErrorAsync(
        string title,
        string? message,
        Exception exception,
        CancellationToken cancellationToken = default)
    {
        Errors.Add((title, message));
        Messages.Add($"Error: {title}: {message}");
        return Task.CompletedTask;
    }

    public Task ErrorAsync(string title, Exception exception, CancellationToken cancellationToken = default) =>
        ErrorAsync(title, exception.Message, exception, cancellationToken);

    public Task FatalAsync(string title, Exception exception, CancellationToken cancellationToken = default) =>
        ErrorAsync(title, exception.Message, exception, cancellationToken);

    public void LogMessage(LogEntrySeverity severity, string title, string? message = null) =>
        Messages.Add($"{severity}: {title}: {message}");
}
