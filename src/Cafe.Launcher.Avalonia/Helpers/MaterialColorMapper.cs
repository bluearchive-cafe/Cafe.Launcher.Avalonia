using Avalonia.Media;
using MaterialColorUtilities.Utils;

namespace Cafe.Launcher.Avalonia.Helpers;

/// <summary>
/// ArgbColor ↔ Avalonia Color/SolidColorBrush conversion layer.
/// M0 spike (docs/design/color-utilities-spike.md §5) chose Option B:
/// core package only, handwritten wiring instead of the Avalonia integration
/// package (which would pull the third-party DesignTokens token framework).
/// </summary>
internal static class MaterialColorMapper
{
    /// <summary>Converts an M3 ArgbColor to an Avalonia Color.</summary>
    public static Color ToAvaloniaColor(ArgbColor color) =>
        Color.FromArgb(color.Alpha, color.Red, color.Green, color.Blue);

    /// <summary>Converts an Avalonia Color to an M3 ArgbColor.</summary>
    public static ArgbColor ToArgbColor(Color color) =>
        new(color.A, color.R, color.G, color.B);

    /// <summary>Converts an M3 ArgbColor to a solid brush with full alpha.</summary>
    public static SolidColorBrush ToBrush(ArgbColor color) =>
        new(ToAvaloniaColor(color));
}
