using System;
using System.Threading;
using System.Threading.Tasks;

namespace Cafe.Launcher.Avalonia.Helpers;

/// <summary>
/// 「最新胜出」的异步刷新槽（D8）：一次 <see cref="Run"/> 让上一次在飞的工作失效——
/// 取消它的令牌——只有最新一次的工作会继续走下去。此前各调用点自持一套陈旧判定
/// （裸 CTS 换取、代数计数、版本号加不可取消的防抖），语义相同、机制各异；收拢后槽
/// 只负责三件事：换取令牌、可选的防抖等待、把在飞任务暴露给 <see cref="Pending"/>
/// 供测试与「等待落定」的调用方观察。await 之后的陈旧判定留在各工作 lambda 里
/// （只有它们知道「应用结果」还要满足什么）；异常也由 lambda 自行处理，槽不吞。
/// </summary>
/// <remarks>
/// 槽在同一条 UI 线程上调用（与迁移前各站点的 CTS 换取一致）；工作延续不脱离调用方
/// 的同步上下文——多数工作 lambda 要回到 UI 线程写可观察状态。
/// </remarks>
public sealed class LatestRefresh
{
    private CancellationTokenSource? cancellationTokenSource;
    private Task pending = Task.CompletedTask;

    /// <summary>Gets the most recently started refresh; it completes when that run reaches its end.</summary>
    public Task Pending => pending;

    /// <summary>
    /// Starts a new refresh as the latest one, cancelling the previous. With a
    /// <paramref name="debounce"/>, the work starts only after the delay; a newer
    /// <see cref="Run"/> during that window cancels the waiting one.
    /// </summary>
    public void Run(TimeSpan? debounce, Func<CancellationToken, Task> work)
    {
        cancellationTokenSource?.Cancel();
        cancellationTokenSource?.Dispose();
        cancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = cancellationTokenSource.Token;
        pending = RunLatestAsync(debounce, work, cancellationToken);
    }

    /// <summary>Invalidates the in-flight refresh without starting a new one.</summary>
    public void Cancel()
    {
        cancellationTokenSource?.Cancel();
        cancellationTokenSource?.Dispose();
        cancellationTokenSource = null;
    }

    private static async Task RunLatestAsync(
        TimeSpan? debounce,
        Func<CancellationToken, Task> work,
        CancellationToken cancellationToken)
    {
        if (debounce is { } delay)
        {
            try
            {
                await Task.Delay(delay, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }

        await work(cancellationToken);
    }
}
