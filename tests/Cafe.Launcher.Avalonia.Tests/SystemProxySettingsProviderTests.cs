using System.Collections.Generic;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class SystemProxySettingsProviderTests
{
    [Theory]
    [InlineData("'manual'", "manual")]
    [InlineData("@s ''", "")]
    [InlineData("''", "")]
    [InlineData("'http://127.0.0.1'", "http://127.0.0.1")]
    [InlineData("  'none'  ", "none")]
    [InlineData("none", "none")]
    public void ParseGSettingsString_WithProbeOutput_UnquotesValue(string output, string expected)
    {
        Assert.Equal(expected, SystemProxySettingsProvider.ParseGSettingsString(output));
    }

    [Theory]
    [InlineData("uint32 8080", 8080)]
    [InlineData("8080", 8080)]
    [InlineData("@u 1080", 1080)]
    [InlineData("uint32 0", 0)]
    public void TryParseGSettingsPort_WithProbeOutput_ParsesPort(string output, int expected)
    {
        Assert.True(SystemProxySettingsProvider.TryParseGSettingsPort(output, out var port));
        Assert.Equal(expected, port);
    }

    [Theory]
    [InlineData("")]
    [InlineData("uint32")]
    [InlineData("not-a-number")]
    public void TryParseGSettingsPort_WithUnparsableOutput_ReturnsFalse(string output)
    {
        Assert.False(SystemProxySettingsProvider.TryParseGSettingsPort(output, out var port));
        Assert.Equal(0, port);
    }

    [Theory]
    [InlineData("@as []")]
    [InlineData("[]")]
    [InlineData("")]
    [InlineData("garbage")]
    public void ParseGSettingsStringArray_WithoutEntries_ReturnsEmpty(string output)
    {
        Assert.Empty(SystemProxySettingsProvider.ParseGSettingsStringArray(output));
    }

    [Fact]
    public void ParseGSettingsStringArray_WithQuotedEntries_SplitsAndUnquotes()
    {
        IReadOnlyList<string> expected = ["localhost", "127.0.0.0/8", "::1", ".example.com"];

        Assert.Equal(
            expected,
            SystemProxySettingsProvider.ParseGSettingsStringArray(
                "['localhost', '127.0.0.0/8', '::1', '.example.com']"));
    }

    [Fact]
    public void ParseGSettingsStringArray_WithBlankEntries_DropsThem()
    {
        IReadOnlyList<string> expected = ["localhost"];

        Assert.Equal(
            expected,
            SystemProxySettingsProvider.ParseGSettingsStringArray("['localhost', , '']"));
    }
}
