using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;

namespace Cafe.Launcher.UI.Helpers;

/// <summary>Reads MP4 covr artwork without loading or decoding the movie payload.</summary>
internal static class Mp4CoverReader
{
    internal const int MaxCoverBytes = 16 * 1024 * 1024;

    public static byte[]? Read(Stream stream, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var movie = FindChild(stream, new Box(0, stream.Length), "moov"u8, cancellationToken);
            if (movie is null) return null;
            var metadata = FindChild(stream, movie.Value, "meta"u8, cancellationToken);
            if (metadata is null && FindChild(stream, movie.Value, "udta"u8, cancellationToken) is { } userData)
                metadata = FindChild(stream, userData, "meta"u8, cancellationToken);
            // meta is a FullBox: its children follow the four version/flags bytes.
            if (metadata is not { } meta || meta.End - meta.Start < 4) return null;
            var items = FindChild(stream, meta with { Start = meta.Start + 4 }, "ilst"u8, cancellationToken);
            if (items is null) return null;
            var cover = FindChild(stream, items.Value, "covr"u8, cancellationToken);
            if (cover is null) return null;

            var remaining = cover.Value;
            while (FindChild(stream, remaining, "data"u8, cancellationToken) is { } data)
            {
                var length = data.End - data.Start - 8; // type/flags and locale precede the encoded image.
                if (length is > 0 and <= MaxCoverBytes)
                {
                    stream.Position = data.Start;
                    var type = ReadUInt32(stream) & 0x00ffffff;
                    if (type is 13 or 14 or 27) // JPEG, PNG, BMP (iTunes artwork types).
                    {
                        stream.Position += 4;
                        var bytes = new byte[(int)length];
                        stream.ReadExactly(bytes);
                        cancellationToken.ThrowIfCancellationRequested();
                        return bytes;
                    }
                }
                remaining = remaining with { Start = data.End };
            }
            return null;
        }
        catch (EndOfStreamException)
        {
            return null;
        }
    }

    private static Box? FindChild(Stream stream, Box parent, ReadOnlySpan<byte> kind, CancellationToken cancellationToken)
    {
        Span<byte> header = stackalloc byte[8];
        for (var offset = parent.Start; parent.End - offset >= 8;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            stream.Position = offset;
            stream.ReadExactly(header);
            ulong size = BinaryPrimitives.ReadUInt32BigEndian(header);
            var matches = header[4..].SequenceEqual(kind);
            var headerSize = 8;
            if (size == 1)
            {
                if (parent.End - offset < 16) return null;
                stream.ReadExactly(header);
                size = BinaryPrimitives.ReadUInt64BigEndian(header);
                headerSize = 16;
            }
            else if (size == 0)
            {
                size = (ulong)(parent.End - offset);
            }
            if (size < (ulong)headerSize || size > (ulong)(parent.End - offset)) return null;
            var box = new Box(offset + headerSize, offset + (long)size);
            if (matches) return box;
            offset = box.End;
        }
        return null;
    }

    private static uint ReadUInt32(Stream stream)
    {
        Span<byte> bytes = stackalloc byte[4];
        stream.ReadExactly(bytes);
        return BinaryPrimitives.ReadUInt32BigEndian(bytes);
    }

    private readonly record struct Box(long Start, long End);
}
