using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Cafe.Launcher.Avalonia.Services;

/// <summary>远程响应正文超过调用方给定的字节上限。</summary>
internal sealed class RemoteBodyTooLargeException(long actualBytes, long? declaredBytes)
    : Exception($"Remote body exceeded the byte budget ({actualBytes} bytes).")
{
    /// <summary>触发上限的字节数：声明长度可判时即声明值，否则是流式读取的累计值。</summary>
    public long ActualBytes { get; } = actualBytes;

    /// <summary>响应声明的长度；未声明（分块传输）为 null。</summary>
    public long? DeclaredBytes { get; } = declaredBytes;
}

/// <summary>
/// 把远程响应正文读进内存，并施加字节上限。
/// </summary>
/// <remarks>
/// <para>两道闸门是同一条策略，必须同址：声明长度可判时先拒（省掉一次流读取），否则按分块累计
/// 并在越界时立刻停手——后一道是必需的，分块传输根本没有 Content-Length 可拒。</para>
/// <para>此前这套闸门在传输层与图片缓存各写一遍。共用它的理由是它是「响应体不能被读爆」这条
/// 不变量，而不是省几行：一处修好、另一处漏改，是这类上限最容易出的错。</para>
/// <para>调用方对「过大」的对外表达不同（传输层要带 URI、状态与响应头预览的
/// <c>HttpRequestException</c>，图片缓存只要一句 <see cref="InvalidDataException"/>），
/// 因此这里只负责判定，映射由调用方做。</para>
/// </remarks>
internal static class RemoteBodyReader
{
    /// <summary>分块读的块大小：与调用方原先各自的取值一致。</summary>
    private const int ChunkBytes = 64 * 1024;

    /// <summary>
    /// 读尽 <paramref name="readAsync"/> 的产出；超过 <paramref name="maxBytes"/> 时抛出
    /// <see cref="RemoteBodyTooLargeException"/>，且已读缓冲由本方法释放。
    /// </summary>
    /// <param name="readAsync">
    /// 单次读取。缓冲区由本方法提供，调用方决定它落到哪个流上——传输层要在读外面套停顿预算
    /// (<c>ResponseBodyReader.ReadAsync</c>)，图片缓存走传输层流包装自带的预算。
    /// </param>
    /// <param name="declaredBytes">响应声明的正文长度；未声明传 null。</param>
    public static async Task<MemoryStream> ReadAllAsync(
        Func<byte[], CancellationToken, Task<int>> readAsync,
        int maxBytes,
        long? declaredBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(readAsync);

        if (declaredBytes is { } declared && declared > maxBytes)
        {
            throw new RemoteBodyTooLargeException(declared, declared);
        }

        var buffer = new MemoryStream();
        try
        {
            var chunk = new byte[ChunkBytes];
            while (true)
            {
                var read = await readAsync(chunk, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    return buffer;
                }

                if (buffer.Length + read > maxBytes)
                {
                    throw new RemoteBodyTooLargeException(buffer.Length + read, declaredBytes: null);
                }

                await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            // 越界时缓冲不交给调用方，由这里释放，调用方只需处理异常。
            buffer.Dispose();
            throw;
        }
    }
}
