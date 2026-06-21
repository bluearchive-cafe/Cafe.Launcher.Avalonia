using Cafe.Launcher.Avalonia.Helpers;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class FileSizeFormatterTests
{
    [Theory]
    [InlineData(0, "0B")]
    [InlineData(1, "1B")]
    [InlineData(1023, "1023B")]
    [InlineData(1024, "1KB")]
    [InlineData(1536, "1.5KB")]
    [InlineData(1048576, "1MB")]
    [InlineData(1073741824, "1GB")]
    [InlineData(1099511627776, "1TB")]
    [InlineData(-1, "0B")]
    public void Format_WhenGivenBytes_ReturnsExpectedString(long bytes, string expected)
    {
        Assert.Equal(expected, FileSizeFormatter.Format(bytes));
    }

    [Theory]
    [InlineData("1024", 1024)]
    [InlineData("0", 0)]
    [InlineData("", 0)]
    [InlineData("not-a-number", 0)]
    [InlineData("-1", -1)]
    [InlineData("9223372036854775807", 9223372036854775807)]
    public void ParseSize_WhenGivenString_ReturnsExpectedLong(string value, long expected)
    {
        Assert.Equal(expected, FileSizeFormatter.ParseSize(value));
    }
}
