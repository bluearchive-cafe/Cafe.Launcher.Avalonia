using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cafe.Launcher.Avalonia.Helpers;

/// <summary>
/// A <see cref="JsonConverter{T}"/> for <see cref="bool"/> that reads both
/// JSON booleans (<c>true</c> / <c>false</c>) and JSON numbers
/// (<c>0</c> → <c>false</c>, non-zero → <c>true</c>).
/// </summary>
/// <remarks>
/// Some upstream API endpoints represent boolean fields as integer 0/1
/// rather than true/false, which causes <see cref="JsonException"/> when
/// the model declares the property as <see cref="bool"/>.
/// </remarks>
public sealed class FlexibleBoolConverter : JsonConverter<bool>
{
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.True => true,
            JsonTokenType.False => false,
            JsonTokenType.Number => reader.GetInt64() != 0,
            _ => throw new JsonException(
                $"Expected boolean or number, got {reader.TokenType}.")
        };
    }

    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
    {
        writer.WriteBooleanValue(value);
    }
}
