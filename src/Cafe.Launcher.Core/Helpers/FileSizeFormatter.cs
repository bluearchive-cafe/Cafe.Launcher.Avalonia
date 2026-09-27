using System;

namespace Cafe.Launcher.Avalonia.Helpers;

/// <summary>
/// Shared file size formatting used by ViewModels and Services.
/// </summary>
public static class FileSizeFormatter
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    public static string Format(long bytes)
    {
        if (bytes <= 0)
        {
            return "0B";
        }

        var unit = (int)Math.Floor(Math.Log(bytes) / Math.Log(1024));
        if (unit >= Units.Length)
        {
            unit = Units.Length - 1;
        }

        var value = bytes / Math.Pow(1024, unit);
        return $"{value:0.##}{Units[unit]}";
    }

    /// <summary>
    /// Parses a file size string from a manifest <c>Size</c> field (bytes, integer).
    /// Returns 0 for any non-parseable input.
    /// </summary>
    public static long ParseSize(string value)
    {
        return long.TryParse(value, out var size) ? size : 0;
    }

    /// <summary>
    /// Parses a human-readable size string such as "10GB", "10 GB", "1.5GB", or raw bytes
    /// ("1024"). Returns <c>false</c> for empty or unrecognised input (never throws).
    /// Used to compare the API <c>DecompressionSize</c> field against available bytes.
    /// </summary>
    public static bool TryParseHumanReadable(string value, out long bytes)
    {
        bytes = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.AsSpan().Trim();
        int i = 0;
        while (i < text.Length && (char.IsDigit(text[i]) || text[i] == '.'))
        {
            i++;
        }

        var numberText = text[..i];
        // Skip any whitespace between the number and the unit ("10 GB").
        while (i < text.Length && char.IsWhiteSpace(text[i]))
        {
            i++;
        }

        var unitStart = i;
        while (i < text.Length && !char.IsWhiteSpace(text[i]))
        {
            i++;
        }

        var unit = unitStart == i ? "" : text[unitStart..i].Trim().ToString();
        while (i < text.Length && char.IsWhiteSpace(text[i]))
        {
            i++;
        }

        if (numberText.IsEmpty
            || i != text.Length
            || !decimal.TryParse(numberText, System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out var number)
            || number < 0)
        {
            return false;
        }

        var factor = UnitFactor(unit);
        if (factor < 0)
        {
            return false;
        }

        if (number > long.MaxValue / factor)
        {
            return false;
        }

        var roundedBytes = decimal.Round(number * factor, 0, MidpointRounding.AwayFromZero);
        if (roundedBytes > long.MaxValue)
        {
            return false;
        }

        bytes = decimal.ToInt64(roundedBytes);
        return true;
    }

    private static decimal UnitFactor(string unit)
    {
        return unit.ToUpperInvariant() switch
        {
            "" or "B" => 1,
            "KB" => 1024,
            "MB" => 1024 * 1024,
            "GB" => 1024 * 1024 * 1024,
            "TB" => 1024L * 1024 * 1024 * 1024,
            _ => -1,
        };
    }
}
