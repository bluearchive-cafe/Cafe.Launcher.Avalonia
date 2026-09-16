using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Testing;

/// <summary>一次 <see cref="IErrorHandlingService.HandleErrorAsync"/> 调用的完整记录。</summary>
public readonly record struct HandledError(
    string Context,
    Exception Exception,
    ErrorHandlingOptions? Options);

/// <summary>
/// 记录型 <see cref="IErrorHandlingService"/> 替身（无 mocking 框架）。
/// </summary>
/// <remarks>
/// 必须记录 <see cref="ErrorHandlingOptions"/> 而不只是计数：本仓库有多处
/// 「失败是否真的会到达用户」的断言（ADR-027 的确认后拒绝必须可见、ADR-029 的卸载预检
/// 不静默），只看调用次数无法区分「报给了用户」与「只写了日志」。
/// </remarks>
public sealed class RecordingErrorHandlingService : IErrorHandlingService
{
    /// <summary>按调用顺序记录的全部错误。</summary>
    public List<HandledError> Handled { get; } = [];

    /// <summary>与 <see cref="Handled"/> 同序的呈现选项。</summary>
    public IEnumerable<ErrorHandlingOptions?> HandledOptions => Handled.Select(handled => handled.Options);

    /// <summary>已请求的关键错误，按发生顺序。</summary>
    public List<CriticalErrorInfo> CriticalErrors { get; } = [];

    public int HandleErrorCount => Handled.Count;

    public string? LastContext => Handled.Count == 0 ? null : Handled[^1].Context;

    public Exception? LastException => Handled.Count == 0 ? null : Handled[^1].Exception;

    public ErrorHandlingOptions? LastOptions => Handled.Count == 0 ? null : Handled[^1].Options;

    public Task HandleErrorAsync(string context, Exception exception, ErrorHandlingOptions? options = null)
    {
        Handled.Add(new HandledError(context, exception, options));
        return Task.CompletedTask;
    }

    public Task HandleCriticalErrorAsync(string context, Exception exception) => Task.CompletedTask;

    public event Action<CriticalErrorInfo>? CriticalErrorRequested;

    /// <summary>触发关键错误事件，供窗口/对话框接线的用例驱动该分支。</summary>
    public void RaiseCriticalError(CriticalErrorInfo info)
    {
        CriticalErrors.Add(info);
        CriticalErrorRequested?.Invoke(info);
    }
}
