using System.Net.Http;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Testing;

namespace Cafe.Launcher.Avalonia.Tests;

/// <summary>
/// <see cref="ResponseBodyReader"/> 的空闲读预算语义：停滞转可重试的
/// HttpRequestException、调用方取消照常传播、正常进展不受影响。
/// </summary>
public sealed class ResponseBodyReaderTests
{
    [Fact]
    public async Task ReadAsync_WhenStreamNeverProducesBytes_ThrowsHttpRequestExceptionAndKeepsCallerTokenLive()
    {
        using var callerCts = new CancellationTokenSource();

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => ResponseBodyReader.ReadAsync(
            ScriptedReadStream.Stalled(),
            new byte[16],
            callerCts.Token,
            TimeSpan.FromMilliseconds(50)));

        Assert.Contains("stalled", exception.Message, StringComparison.Ordinal);
        // 预算超时不得污染调用方的取消令牌：上层重试仍可复用该令牌继续请求。
        Assert.False(callerCts.IsCancellationRequested);
    }

    [Fact]
    public async Task ReadAsync_WhenCallerCancels_ThrowsOperationCanceledException()
    {
        using var callerCts = new CancellationTokenSource();
        var readTask = ResponseBodyReader.ReadAsync(
            ScriptedReadStream.Stalled(),
            new byte[16],
            callerCts.Token,
            TimeSpan.FromSeconds(30));

        callerCts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => readTask);
    }

    [Fact]
    public async Task ReadAsync_WhenStreamYieldsBytes_ReturnsReadCount()
    {
        var read = await ResponseBodyReader.ReadAsync(
            ScriptedReadStream.DeliveringOnce(5),
            new byte[16],
            CancellationToken.None,
            TimeSpan.FromSeconds(1));

        Assert.Equal(5, read);
    }
}
