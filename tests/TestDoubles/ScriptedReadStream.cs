using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Cafe.Launcher.Avalonia.Testing;

/// <summary>读尽交付字节预算之后的尾部行为。</summary>
public enum ReadTail
{
    /// <summary>干净地到达 EOF。</summary>
    EndOfStream,

    /// <summary>挂起直到取消令牌触发——模拟头部已到、正文零字节停滞的连接。</summary>
    Stall,

    /// <summary>抛出构造时给定的异常——模拟传输中断。</summary>
    Throw
}

/// <summary>
/// 按「交付字节预算 + 尾部行为」脚本化读取的响应体替身。
/// </summary>
/// <remarks>
/// <para>响应体读取器的三条分支（长度上限截断、停滞预算、传输中断）共用同一个骨架，差异只在
/// 尾部行为与「是否声明长度」，故收成一个替身而不是每个用例各写一份。</para>
/// <para><see cref="Stream.Length"/> 是否可读是承重的，不是风格问题：声明长度的替身会让
/// <c>HttpContent</c> 走「已知长度」的快路径，而停滞替身必须<b>不</b>声明长度，否则读取器不会
/// 真的去流式读，用例也就验证不到停滞分支。</para>
/// </remarks>
public sealed class ScriptedReadStream : Stream
{
    private readonly byte[] content;
    private readonly int budget;
    private readonly ReadTail tail;
    private readonly Exception? failure;
    private readonly bool declaresLength;
    private readonly bool deliverOnce;
    private int position;
    private bool delivered;

    private ScriptedReadStream(
        byte[] content,
        int budget,
        ReadTail tail,
        Exception? failure,
        bool declaresLength,
        bool deliverOnce)
    {
        this.content = content;
        this.budget = budget;
        this.tail = tail;
        this.failure = failure;
        this.declaresLength = declaresLength;
        this.deliverOnce = deliverOnce;
    }

    /// <summary>零字节后挂起、且不声明长度：驱动停滞分支的最小替身。</summary>
    public static ScriptedReadStream Stalled() =>
        new([], 0, ReadTail.Stall, null, declaresLength: false, deliverOnce: false);

    /// <summary>交付 <paramref name="deliveredBytes"/> 字节后挂起，声明正文总长为 <c>content.Length</c>。</summary>
    public static ScriptedReadStream StallingAfter(byte[] content, int deliveredBytes) =>
        new(content, deliveredBytes, ReadTail.Stall, null, declaresLength: true, deliverOnce: false);

    /// <summary>交付 <paramref name="deliveredBytes"/> 字节后 EOF——声明的总长大于实际交付量。</summary>
    public static ScriptedReadStream EndingAfter(byte[] content, int deliveredBytes) =>
        new(content, deliveredBytes, ReadTail.EndOfStream, null, declaresLength: true, deliverOnce: false);

    /// <summary>一次读取交付至多 <paramref name="bytes"/> 字节后 EOF，且不声明长度。</summary>
    public static ScriptedReadStream DeliveringOnce(int bytes) =>
        new(new byte[bytes], bytes, ReadTail.EndOfStream, null, declaresLength: false, deliverOnce: true);

    /// <summary>交付 <paramref name="deliveredBytes"/> 字节后抛出 <paramref name="failure"/>。</summary>
    public static ScriptedReadStream ThrowingAfter(byte[] content, int deliveredBytes, Exception failure) =>
        new(content, deliveredBytes, ReadTail.Throw, failure, declaresLength: true, deliverOnce: false);

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => declaresLength
        ? content.Length
        : throw new NotSupportedException("此替身不声明长度：调用方必须流式读取。");

    public override long Position
    {
        get => position;
        set => throw new NotSupportedException();
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        if (IsExhausted())
        {
            return await TailAsync(cancellationToken).ConfigureAwait(false);
        }

        var bytesToCopy = Math.Min(buffer.Length, budget - position);
        content.AsMemory(position, bytesToCopy).CopyTo(buffer);
        return Advance(bytesToCopy);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (IsExhausted())
        {
            return tail switch
            {
                ReadTail.Throw => throw failure!,
                ReadTail.EndOfStream => 0,
                _ => throw new NotSupportedException("此替身仅支持异步读取。")
            };
        }

        var bytesToCopy = Math.Min(count, budget - position);
        Array.Copy(content, position, buffer, offset, bytesToCopy);
        return Advance(bytesToCopy);
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private bool IsExhausted() => position >= budget || (deliverOnce && delivered);

    private int Advance(int bytesToCopy)
    {
        position += bytesToCopy;
        delivered = true;
        return bytesToCopy;
    }

    private async ValueTask<int> TailAsync(CancellationToken cancellationToken)
    {
        switch (tail)
        {
            case ReadTail.EndOfStream:
                return 0;
            case ReadTail.Throw:
                throw failure!;
            default:
                // 字节预算用尽后挂起，直到取消令牌（用户取消或空闲读预算）触发。
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
                return 0;
        }
    }
}

/// <summary>
/// 合成指定长度与填充字节的响应体替身，用于驱动「分块读取的长度上限」这类不需要真实内容的用例。
/// </summary>
public sealed class SyntheticReadStream(long length, byte fill = 0, bool declaresLength = true) : Stream
{
    private long position;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => declaresLength
        ? length
        : throw new NotSupportedException("此替身不声明长度：调用方必须流式读取。");

    public override long Position
    {
        get => position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var remaining = length - position;
        if (remaining <= 0)
        {
            return 0;
        }

        var read = (int)Math.Min(count, remaining);
        Array.Fill(buffer, fill, offset, read);
        position += read;
        return read;
    }

    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var remaining = length - position;
        if (remaining <= 0)
        {
            return ValueTask.FromResult(0);
        }

        var read = (int)Math.Min(buffer.Length, remaining);
        buffer.Span[..read].Fill(fill);
        position += read;
        return ValueTask.FromResult(read);
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
