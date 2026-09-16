using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Cafe.Launcher.Avalonia.Testing;

/// <summary>
/// 测试对异步状态的等待：单一实现的有截止时间轮询（单调计时、超时、可选取消），
/// 以及把「等什么」写进失败消息的约定。
/// </summary>
/// <remarks>
/// <para>计时用 <see cref="Stopwatch"/> 的时间戳而不是墙上时钟：Docker/CI 上
/// <see cref="DateTime.UtcNow"/> 会跳（宿主时钟同步、时区/夏令时切换），跳错方向的墙钟
/// 会让等待提前超时，表现为随机的假红。挂钟时间只应该描述「等了多久」，不应该决定
/// 「还要不要等」。</para>
/// <para>UI 测试需要每轮泵一次调度器再评估条件，那层由无头工程自己的包装提供
/// （<c>HeadlessTestHost.WaitUntilAsync</c> 传入 <c>tickAsync</c>）；本类不引用 Avalonia。</para>
/// </remarks>
public static class TestWait
{
    /// <summary>两轮评估之间的默认间隔：短到不掩盖真实进度，长到不空转占满 CPU。</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(10);

    /// <summary>
    /// 轮询等待 <paramref name="condition"/> 成立；超时抛 <see cref="TimeoutException"/>。
    /// 条件先评估一次，成立即返回，不白白等一个轮询间隔。
    /// </summary>
    /// <param name="tickAsync">每轮评估前的推进动作，缺省为 <see cref="PollInterval"/> 的等待。</param>
    public static async Task UntilAsync(
        Func<bool> condition,
        TimeSpan timeout,
        string? failureMessage = null,
        Func<ValueTask>? tickAsync = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(condition);
        var startedAt = Stopwatch.GetTimestamp();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (condition())
            {
                return;
            }

            var elapsed = Stopwatch.GetElapsedTime(startedAt);
            if (elapsed >= timeout)
            {
                throw new TimeoutException(DescribeTimeout(failureMessage, timeout, elapsed));
            }

            if (tickAsync is not null)
            {
                // 不 ConfigureAwait(false)：条件的评估必须留在调用方的同步上下文上。
                // 无头用例的调度器上下文就是 UI 线程，条件里读的是 Avalonia 控件属性
                // （Image.Source 等），落到线程池上会直接抛 VerifyAccess。
                await tickAsync();
            }
            else
            {
                await Task.Delay(PollInterval, cancellationToken);
            }
        }
    }

    private static string DescribeTimeout(string? failureMessage, TimeSpan timeout, TimeSpan elapsed)
    {
        var subject = string.IsNullOrWhiteSpace(failureMessage) ? "Condition" : failureMessage;
        return $"{subject} was not met within {timeout.TotalSeconds:0.#}s "
            + $"(waited {elapsed.TotalSeconds:0.#}s).";
    }

    /// <summary>
    /// 在 <paramref name="window"/> 这段时间内持续要求 <paramref name="condition"/> 成立，
    /// 用于「这段时间里什么都没有发生」这类负向断言。
    /// </summary>
    /// <remarks>
    /// 观察窗同样用 <see cref="Stopwatch"/> 而非墙上时钟：墙钟往前跳会让窗口提前结束，
    /// 负向断言于是在几乎没观察的情况下通过——它证明的是「没看到」，而不是「不存在」。
    /// </remarks>
    public static async Task HoldsForAsync(
        TimeSpan window,
        Func<bool> condition,
        string? failureMessage = null,
        Func<ValueTask>? tickAsync = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(condition);
        var startedAt = Stopwatch.GetTimestamp();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var elapsed = Stopwatch.GetElapsedTime(startedAt);
            if (!condition())
            {
                var subject = string.IsNullOrWhiteSpace(failureMessage) ? "Condition" : failureMessage;
                throw new InvalidOperationException(
                    $"{subject} was violated after {elapsed.TotalSeconds:0.#}s "
                    + $"of a {window.TotalSeconds:0.#}s observation window.");
            }

            if (elapsed >= window)
            {
                return;
            }

            if (tickAsync is not null)
            {
                await tickAsync();
            }
            else
            {
                await Task.Delay(PollInterval, cancellationToken);
            }
        }
    }
}
