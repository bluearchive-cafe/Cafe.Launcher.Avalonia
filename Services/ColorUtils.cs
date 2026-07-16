using System;
using Avalonia.Media;

namespace Cafe.Launcher.Avalonia.Services;

/// <summary>
/// Colour-space conversion and accent-colour normalisation utilities.
/// Extracted <see cref="ViewModels.SettingsAppearanceViewModel"/> so the VM
/// stays focused on state coordination.
/// </summary>
internal static class ColorUtils
{
    /// <summary>
    /// Adjusts a colour's RGB channels by a uniform factor (e.g. 1.15 = 15% brighter, 0.85 = 15% darker).
    /// </summary>
    public static Color AdjustColor(Color color, double factor)
    {
        static byte Adjust(byte value, double amount) =>
            (byte)Math.Clamp((int)Math.Round(value * amount), 0, 255);

        return Color.FromArgb(
            color.A,
            Adjust(color.R, factor),
            Adjust(color.G, factor),
            Adjust(color.B, factor));
    }

    /// <summary>
    /// Normalises an accent colour for UI display so it has sufficient saturation and value
    /// and the resulting relative luminance is <= 0.32 (ensuring readable on-accent text).
    /// </summary>
    public static Color NormalizeAccentColorForUi(Color color)
    {
        var (hue, saturation, value) = ToHsv(color);
        var adjustedSaturation = Math.Max(saturation, 0.22d);
        var adjustedValue = Math.Max(value, 0.30d);
        var adjustedColor = FromHsv(hue, adjustedSaturation, adjustedValue, color.A);

        if (GetRelativeLuminance(adjustedColor) <= 0.32d)
        {
            return adjustedColor;
        }

        var low = 0d;
        var high = adjustedValue;
        for (var i = 0; i < 12; i++)
        {
            var mid = (low + high) / 2d;
            var candidate = FromHsv(hue, adjustedSaturation, mid, color.A);
            if (GetRelativeLuminance(candidate) > 0.32d)
            {
                high = mid;
            }
            else
            {
                low = mid;
            }
        }

        return FromHsv(hue, adjustedSaturation, low, color.A);
    }

    /// <summary>
    /// Returns black or white, whichever offers the better contrast against <paramref name="color"/>.
    /// </summary>
    public static Color GetReadableOnAccentColor(Color color)
    {
        var luminance = GetRelativeLuminance(color);
        return luminance > 0.45
            ? Color.FromRgb(0x12, 0x18, 0x20)
            : Colors.White;
    }

    public static double GetRelativeLuminance(Color color) =>
        (0.2126 * SrgbToLinear(color.R / 255d))
        + (0.7152 * SrgbToLinear(color.G / 255d))
        + (0.0722 * SrgbToLinear(color.B / 255d));

    public static (double Hue, double Saturation, double Value) ToHsv(Color color)
    {
        var r = color.R / 255d;
        var g = color.G / 255d;
        var b = color.B / 255d;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        double hue;
        if (delta == 0)
        {
            hue = 0;
        }
        else if (max == r)
        {
            hue = 60d * (((g - b) / delta) % 6d);
        }
        else if (max == g)
        {
            hue = 60d * (((b - r) / delta) + 2d);
        }
        else
        {
            hue = 60d * (((r - g) / delta) + 4d);
        }

        if (hue < 0)
        {
            hue += 360d;
        }

        var saturation = max == 0 ? 0 : delta / max;
        return (hue, saturation, max);
    }

    public static Color FromHsv(double hue, double saturation, double value, byte alpha)
    {
        var chroma = value * saturation;
        var segment = hue / 60d;
        var x = chroma * (1d - Math.Abs((segment % 2d) - 1d));
        var match = value - chroma;
        double r;
        double g;
        double b;
        if (segment < 1d)
        {
            r = chroma;
            g = x;
            b = 0d;
        }
        else if (segment < 2d)
        {
            r = x;
            g = chroma;
            b = 0d;
        }
        else if (segment < 3d)
        {
            r = 0d;
            g = chroma;
            b = x;
        }
        else if (segment < 4d)
        {
            r = 0d;
            g = x;
            b = chroma;
        }
        else if (segment < 5d)
        {
            r = x;
            g = 0d;
            b = chroma;
        }
        else
        {
            r = chroma;
            g = 0d;
            b = x;
        }

        return Color.FromArgb(
            alpha,
            (byte)Math.Clamp((int)Math.Round((r + match) * 255d), 0, 255),
            (byte)Math.Clamp((int)Math.Round((g + match) * 255d), 0, 255),
            (byte)Math.Clamp((int)Math.Round((b + match) * 255d), 0, 255));
    }

    private static double SrgbToLinear(double value) =>
        value <= 0.04045
            ? value / 12.92
            : Math.Pow((value + 0.055) / 1.055, 2.4);
}
