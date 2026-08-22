using System;
using Avalonia.Media;

namespace Cafe.Launcher.Avalonia.Helpers;

/// <summary>
/// Colour-space conversion and accent-colour normalisation utilities.
/// Extracted <see cref="Features.Settings.SettingsAppearanceViewModel"/> so the VM
/// stays focused on state coordination.
/// </summary>
internal static class ColorUtils
{
    private const double MinimumNormalTextContrast = 4.5d;
    private static readonly Color DarkOnAccent = Color.FromRgb(0x12, 0x18, 0x20);

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
    /// Normalises an accent colour for UI display so it has sufficient saturation and a
    /// foreground with WCAG normal-text contrast. The old luminance cap could produce an
    /// accent that was too light for white text and too dark for the dark foreground.
    /// </summary>
    public static Color NormalizeAccentColorForUi(Color color)
    {
        var (hue, saturation, value) = ToHsv(color);
        var adjustedSaturation = Math.Max(saturation, 0.22d);
        var adjustedValue = Math.Max(value, 0.30d);
        var adjustedColor = FromHsv(hue, adjustedSaturation, adjustedValue, color.A);

        if (GetBestForegroundContrast(adjustedColor) >= MinimumNormalTextContrast)
        {
            return adjustedColor;
        }

        var darkForegroundContrast = GetContrastRatio(adjustedColor, DarkOnAccent);
        var prefersDarkForeground = darkForegroundContrast
            >= GetContrastRatio(adjustedColor, Colors.White);
        var low = prefersDarkForeground ? adjustedValue : 0d;
        var high = prefersDarkForeground ? 1d : adjustedValue;
        for (var i = 0; i < 12; i++)
        {
            var mid = (low + high) / 2d;
            var candidate = FromHsv(hue, adjustedSaturation, mid, color.A);
            var hasContrast = GetContrastRatio(
                candidate,
                prefersDarkForeground ? DarkOnAccent : Colors.White) >= MinimumNormalTextContrast;
            if (hasContrast)
            {
                if (prefersDarkForeground)
                {
                    high = mid;
                }
                else
                {
                    low = mid;
                }
            }
            else
            {
                if (prefersDarkForeground)
                {
                    low = mid;
                }
                else
                {
                    high = mid;
                }
            }
        }

        return FromHsv(
            hue,
            adjustedSaturation,
            prefersDarkForeground ? high : low,
            color.A);
    }

    /// <summary>
    /// Returns the accessible dark foreground or white, whichever offers the better contrast.
    /// </summary>
    public static Color GetReadableForegroundColor(Color color)
    {
        return GetContrastRatio(color, DarkOnAccent) >= GetContrastRatio(color, Colors.White)
            ? DarkOnAccent
            : Colors.White;
    }

    /// <summary>
    /// Returns an accessible foreground for an accent colour.
    /// </summary>
    public static Color GetReadableOnAccentColor(Color color) =>
        GetReadableForegroundColor(color);

    /// <summary>Computes the WCAG contrast ratio between two opaque UI colors.</summary>
    public static double GetContrastRatio(Color first, Color second)
    {
        var firstLuminance = GetRelativeLuminance(first);
        var secondLuminance = GetRelativeLuminance(second);
        return (Math.Max(firstLuminance, secondLuminance) + 0.05d)
            / (Math.Min(firstLuminance, secondLuminance) + 0.05d);
    }

    /// <summary>
    /// Computes WCAG relative luminance for an sRGB colour.
    /// </summary>
    public static double GetRelativeLuminance(Color color) =>
        (0.2126 * SrgbToLinear(color.R / 255d))
        + (0.7152 * SrgbToLinear(color.G / 255d))
        + (0.0722 * SrgbToLinear(color.B / 255d));

    private static double GetBestForegroundContrast(Color color) =>
        Math.Max(
            GetContrastRatio(color, DarkOnAccent),
            GetContrastRatio(color, Colors.White));

    /// <summary>
    /// Converts an sRGB colour to HSV components.
    /// </summary>
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

    /// <summary>
    /// Converts HSV components to an sRGB colour with the supplied alpha channel.
    /// </summary>
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
