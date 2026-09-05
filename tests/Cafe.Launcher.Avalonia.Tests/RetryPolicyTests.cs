using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

/// <summary>
/// 验证 RetryPolicy 的重试契约：首次成功不退避、可重试失败按序列退避后重试、
/// 不可重试异常直接透传、取消立即生效、尝试耗尽时抛出最后一次的异常实例。
/// 全部通过 delayAsync 测试接缝注入确定性延迟,不依赖真实时间。
/// </summary>
public sealed class RetryPolicyTests
{
    [Fact]
    public async Task ExecuteWithRetryAsync_FirstAttemptSucceeds_ExecutesOnceWithoutBackoff()
    {
        int attempts = 0;
        int backoffCalls = 0;
        var delays = new List<TimeSpan>();

        var result = await RetryPolicy.ExecuteWithRetryAsync<int>(
            _ =>
            {
                attempts++;
                return Task.FromResult(42);
            },
            maxAttempts: 3,
            backoff: _ =>
            {
                backoffCalls++;
                return TimeSpan.FromMilliseconds(100);
            },
            CancellationToken.None,
            delayAsync: (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });

        Assert.Equal(42, result);
        Assert.Equal(1, attempts);
        Assert.Equal(0, backoffCalls);
        Assert.Empty(delays);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_RetryableFailureThenSuccess_RetriesOnceWithRecordedBackoff()
    {
        int attempts = 0;
        var delays = new List<TimeSpan>();
        var transient = new HttpRequestException("first attempt fails");

        var result = await RetryPolicy.ExecuteWithRetryAsync<int>(
            _ =>
            {
                attempts++;
                return attempts == 1 ? throw transient : Task.FromResult(7);
            },
            maxAttempts: 3,
            backoff: i => TimeSpan.FromMilliseconds(50 * (i + 1)),
            CancellationToken.None,
            delayAsync: (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });

        // HttpRequestException 命中默认可重试过滤,重试一次并按 attempt 0 的退避等待。
        Assert.Equal(7, result);
        Assert.Equal(2, attempts);
        var recordedDelay = Assert.Single(delays);
        Assert.Equal(TimeSpan.FromMilliseconds(50), recordedDelay);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_NonRetryableFailure_PropagatesWithoutRetry()
    {
        int attempts = 0;
        var permanent = new InvalidOperationException("not retryable");

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RetryPolicy.ExecuteWithRetryAsync<int>(
                _ =>
                {
                    attempts++;
                    throw permanent;
                },
                maxAttempts: 4,
                backoff: _ => TimeSpan.FromMilliseconds(100),
                CancellationToken.None,
                delayAsync: (_, _) => Task.CompletedTask));

        // InvalidOperationException 不匹配默认可重试过滤,原样透传且不再尝试。
        Assert.Same(permanent, thrown);
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_CancelledBeforeFirstAttempt_ThrowsWithoutExecutingAction()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        int attempts = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            RetryPolicy.ExecuteWithRetryAsync<int>(
                _ =>
                {
                    attempts++;
                    return Task.FromResult(1);
                },
                maxAttempts: 3,
                backoff: _ => TimeSpan.Zero,
                cts.Token,
                delayAsync: (_, _) => Task.CompletedTask));

        Assert.Equal(0, attempts);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_CancelledDuringBackoff_ThrowsWithoutFurtherAttempts()
    {
        using var cts = new CancellationTokenSource();
        int attempts = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            RetryPolicy.ExecuteWithRetryAsync<int>(
                _ =>
                {
                    attempts++;
                    throw new HttpRequestException("transient");
                },
                maxAttempts: 3,
                backoff: _ => TimeSpan.FromMilliseconds(100),
                cts.Token,
                delayAsync: (_, token) =>
                {
                    cts.Cancel();
                    return Task.FromCanceled(token);
                }));

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_AllAttemptsExhausted_ThrowsLastExceptionAfterBackoffSequence()
    {
        int attempts = 0;
        var delays = new List<TimeSpan>();
        var first = new HttpRequestException("first");
        var second = new HttpRequestException("second");
        var third = new HttpRequestException("third");

        var thrown = await Assert.ThrowsAsync<HttpRequestException>(() =>
            RetryPolicy.ExecuteWithRetryAsync<int>(
                _ =>
                {
                    attempts++;
                    Exception failure = attempts switch
                    {
                        1 => first,
                        2 => second,
                        _ => third
                    };
                    throw failure;
                },
                maxAttempts: 3,
                backoff: i => TimeSpan.FromMilliseconds(10 * (i + 1)),
                CancellationToken.None,
                delayAsync: (delay, _) =>
                {
                    delays.Add(delay);
                    return Task.CompletedTask;
                }));

        // 失败语义:抛出最后一次尝试的异常实例,退避在除最后一次外的每次失败后执行。
        Assert.Equal(3, attempts);
        Assert.Same(third, thrown);
        Assert.Equal(
            new[] { TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(20) },
            delays);
    }
}
