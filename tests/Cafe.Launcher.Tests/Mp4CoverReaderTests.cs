using System.Buffers.Binary;
using System.Text;
using Cafe.Launcher.UI.Helpers;

namespace Cafe.Launcher.Tests;

public sealed class Mp4CoverReaderTests
{
    [Theory]
    [InlineData(13u, true)]
    [InlineData(14u, true)]
    [InlineData(27u, false)]
    public void Read_WhenArtworkExists_ReturnsEncodedImage(uint type, bool underUserData)
    {
        byte[] expected = [1, 2, 3, 4];
        using var stream = new MemoryStream(Movie(Artwork(expected, type), underUserData));

        Assert.Equal(expected, Mp4CoverReader.Read(stream, CancellationToken.None));
    }

    [Fact]
    public void Read_WhenMovieUsesExtendedSize_ReturnsArtwork()
    {
        byte[] expected = [5, 6, 7];
        var normal = Movie(Artwork(expected));
        var extended = new byte[normal.Length + 8];
        BinaryPrimitives.WriteUInt32BigEndian(extended, 1);
        "moov"u8.CopyTo(extended.AsSpan(4));
        BinaryPrimitives.WriteUInt64BigEndian(extended.AsSpan(8), (ulong)extended.Length);
        normal.AsSpan(8).CopyTo(extended.AsSpan(16));
        using var stream = new MemoryStream(extended);

        Assert.Equal(expected, Mp4CoverReader.Read(stream, CancellationToken.None));
    }

    [Fact]
    public void Read_WhenMovieSizeExtendsToEndOfFile_ReturnsArtwork()
    {
        byte[] expected = [8, 9];
        var bytes = Movie(Artwork(expected));
        bytes.AsSpan(0, 4).Clear();
        using var stream = new MemoryStream(bytes);

        Assert.Equal(expected, Mp4CoverReader.Read(stream, CancellationToken.None));
    }

    [Fact]
    public void Read_WhenMediaPayloadContainsFalseMetadata_SkipsPayload()
    {
        byte[] expected = [10, 11];
        var media = Box("mdat", Movie(Artwork([99])));
        using var stream = new MemoryStream([.. media, .. Movie(Artwork(expected))]);

        Assert.Equal(expected, Mp4CoverReader.Read(stream, CancellationToken.None));
    }

    [Fact]
    public void Read_WhenFirstDataTypeIsUnknown_UsesNextSupportedImage()
    {
        byte[] expected = [12, 13];
        var cover = Box("covr", [.. Data([99], 1), .. Data(expected, 14)]);
        using var stream = new MemoryStream(Movie(cover));

        Assert.Equal(expected, Mp4CoverReader.Read(stream, CancellationToken.None));
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(uint.MaxValue)]
    public void Read_WhenArtworkTypeIsUnknown_ReturnsNull(uint type)
    {
        using var stream = new MemoryStream(Movie(Artwork([1], type)));

        Assert.Null(Mp4CoverReader.Read(stream, CancellationToken.None));
    }

    [Theory]
    [InlineData(1u)]
    [InlineData(4u)]
    [InlineData(uint.MaxValue)]
    public void Read_WhenBoxSizeIsInvalid_ReturnsNull(uint size)
    {
        var bytes = Movie(Artwork([1]));
        BinaryPrimitives.WriteUInt32BigEndian(bytes, size);
        using var stream = new MemoryStream(bytes);

        Assert.Null(Mp4CoverReader.Read(stream, CancellationToken.None));
    }

    [Fact]
    public void Read_WhenFileIsTruncated_ReturnsNull()
    {
        var bytes = Movie(Artwork([1]));
        using var stream = new MemoryStream(bytes[..^1]);

        Assert.Null(Mp4CoverReader.Read(stream, CancellationToken.None));
    }

    [Fact]
    public void Read_WhenCoverExceedsLimit_ReturnsNull()
    {
        using var stream = new MemoryStream(Movie(Artwork(new byte[Mp4CoverReader.MaxCoverBytes + 1])));

        Assert.Null(Mp4CoverReader.Read(stream, CancellationToken.None));
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("no-metadata")]
    [InlineData("short-metadata")]
    [InlineData("no-items")]
    [InlineData("no-cover")]
    [InlineData("no-data")]
    [InlineData("short-data")]
    [InlineData("empty-image")]
    public void Read_WhenArtworkIsUnavailable_ReturnsNull(string scenario)
    {
        byte[] bytes = scenario switch
        {
            "empty" => [],
            "no-metadata" => Box("moov", []),
            "short-metadata" => Box("moov", Box("meta", [0, 0, 0])),
            "no-items" => Box("moov", Box("meta", [0, 0, 0, 0])),
            "no-cover" => Movie(Box("free", [])),
            "no-data" => Movie(Box("covr", [])),
            "short-data" => Movie(Box("covr", Box("data", [0, 0, 0, 14]))),
            "empty-image" => Movie(Artwork([])),
            _ => throw new ArgumentException(scenario, nameof(scenario))
        };
        using var stream = new MemoryStream(bytes);

        Assert.Null(Mp4CoverReader.Read(stream, CancellationToken.None));
    }

    [Fact]
    public void Read_WhenCancelled_PropagatesCancellation()
    {
        using var stream = new MemoryStream(Movie(Artwork([1])));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => Mp4CoverReader.Read(stream, cancellation.Token));
    }

    private static byte[] Movie(byte[] cover, bool underUserData = true)
    {
        var metadata = Box("meta", [0, 0, 0, 0, .. Box("ilst", cover)]);
        return Box("moov", underUserData ? Box("udta", metadata) : metadata);
    }

    private static byte[] Artwork(byte[] bytes, uint type = 14) => Box("covr", Data(bytes, type));

    private static byte[] Data(byte[] bytes, uint type)
    {
        var payload = new byte[8 + bytes.Length];
        BinaryPrimitives.WriteUInt32BigEndian(payload, type);
        bytes.CopyTo(payload, 8);
        return Box("data", payload);
    }

    private static byte[] Box(string kind, byte[] payload)
    {
        var bytes = new byte[8 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)bytes.Length);
        Encoding.ASCII.GetBytes(kind).CopyTo(bytes, 4);
        payload.CopyTo(bytes, 8);
        return bytes;
    }
}
