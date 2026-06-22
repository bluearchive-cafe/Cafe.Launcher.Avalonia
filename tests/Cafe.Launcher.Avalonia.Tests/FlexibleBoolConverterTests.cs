using System.Text.Json;
using Cafe.Launcher.Avalonia.Helpers;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class FlexibleBoolConverterTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new FlexibleBoolConverter() }
    };

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("-1", true)]
    public void Deserialize_ReadsBooleanAndNumericTokens(string json, bool expected)
    {
        Assert.Equal(expected, JsonSerializer.Deserialize<bool>(json, Options));
    }

    [Fact]
    public void Deserialize_WhenTokenTypeIsUnsupported_Throws()
    {
        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<bool>("\"true\"", Options));
    }

    [Theory]
    [InlineData(true, "true")]
    [InlineData(false, "false")]
    public void Serialize_WritesBooleanToken(bool value, string expected)
    {
        Assert.Equal(expected, JsonSerializer.Serialize(value, Options));
    }
}
