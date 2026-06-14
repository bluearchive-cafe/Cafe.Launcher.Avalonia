using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class BestHttpCookieLibraryServiceTests
{
    [Fact]
    public void Read_WhenLibraryContainsUidCookie_ReturnsExactCookieFields()
    {
        var service = new BestHttpCookieLibraryService();
        using var stream = CreateLibraryStream(writer =>
        {
            writer.Write(1);
            writer.Write(1);
            writer.Write(1);
            writer.Write("uid");
            writer.Write("FEIFXVGH");
            writer.Write(new DateTime(2026, 6, 10, 16, 3, 9, DateTimeKind.Utc).ToBinary());
            writer.Write(new DateTime(2026, 6, 13, 15, 8, 21, DateTimeKind.Utc).ToBinary());
            writer.Write(DateTime.FromBinary(0).ToBinary());
            writer.Write(2147483647L);
            writer.Write(false);
            writer.Write("bluearchive.cafe");
            writer.Write("/");
            writer.Write(false);
            writer.Write(false);
        });

        var library = service.Read(stream);

        Assert.Equal(1, library.Version);
        var cookie = Assert.Single(library.Cookies);
        Assert.Equal(1, cookie.Version);
        Assert.Equal("uid", cookie.Name);
        Assert.Equal("FEIFXVGH", cookie.Value);
        Assert.Equal("bluearchive.cafe", cookie.Domain);
        Assert.Equal("/", cookie.Path);
        Assert.Equal(2147483647L, cookie.MaxAge);
        Assert.False(cookie.IsSession);
        Assert.False(cookie.IsSecure);
        Assert.False(cookie.IsHttpOnly);
    }

    [Fact]
    public void Read_WhenLibraryContainsSecureHttpOnlyCookie_ReturnsBooleanFlags()
    {
        var service = new BestHttpCookieLibraryService();
        using var stream = CreateLibraryStream(writer =>
        {
            writer.Write(1);
            writer.Write(1);
            writer.Write(1);
            writer.Write("cdn_sec_tc");
            writer.Write("2ff6269e17813632287724379e24d32fffedc7844a64b967351b409cef");
            writer.Write(new DateTime(2026, 6, 13, 15, 7, 8, DateTimeKind.Utc).ToBinary());
            writer.Write(new DateTime(2026, 6, 13, 15, 7, 46, DateTimeKind.Utc).ToBinary());
            writer.Write(DateTime.FromBinary(0).ToBinary());
            writer.Write(3600L);
            writer.Write(false);
            writer.Write("yostar-serverinfo.bluearchive.cafe");
            writer.Write("/");
            writer.Write(true);
            writer.Write(true);
        });

        var library = service.Read(stream);

        var cookie = Assert.Single(library.Cookies);
        Assert.Equal("cdn_sec_tc", cookie.Name);
        Assert.True(cookie.IsSecure);
        Assert.True(cookie.IsHttpOnly);
    }

    [Fact]
    public void Read_WhenCountIsNegative_ThrowsInvalidDataException()
    {
        var service = new BestHttpCookieLibraryService();
        using var stream = CreateLibraryStream(writer =>
        {
            writer.Write(1);
            writer.Write(-1);
        });

        Assert.Throws<InvalidDataException>(() => service.Read(stream));
    }

    [Fact]
    public void Read_WhenTrailingBytesExist_ThrowsInvalidDataException()
    {
        var service = new BestHttpCookieLibraryService();
        using var stream = CreateLibraryStream(writer =>
        {
            writer.Write(1);
            writer.Write(0);
            writer.Write((byte)1);
        });

        Assert.Throws<InvalidDataException>(() => service.Read(stream));
    }

    private static MemoryStream CreateLibraryStream(Action<BinaryWriter> write)
    {
        var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            write(writer);
        }

        stream.Position = 0;
        return stream;
    }
}
