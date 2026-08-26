using System;
using System.Collections.Generic;
using Avalonia.Media;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;
using MaterialColorUtilities.DynamicColors;
using MaterialColorUtilities.HCT;
using MaterialColorUtilities.Scheme;
using MaterialColorUtilities.Utils;
using CafeColorUtils = Cafe.Launcher.Avalonia.Helpers.ColorUtils;

namespace Cafe.Launcher.Avalonia.Services;

/// <summary>
/// M3 dynamic-scheme generation (Q13/Q17/Q23, spec §3.4). Consumes the
/// Shirasagi0012.MaterialColorUtilities core package directly (M0 spike: GO,
/// Spec2021 default, Platform.Phone) and maps scheme roles onto the
/// <c>Launcher.Color.*</c> brush keys consumed by the UI.
/// </summary>
internal static class MaterialSchemeGenerator
{
    /// <summary>
    /// Creates an M3 <see cref="DynamicScheme"/> for a seed colour, variant and
    /// brightness. The variant maps onto the eight Q24-approved variants and
    /// falls back to TonalSpot for unknown codes (settings are normalized
    /// before reaching this point).
    /// </summary>
    public static DynamicScheme CreateScheme(Color seed, string variant, bool isDark)
    {
        var seedHct = Hct.From(MaterialColorMapper.ToArgbColor(seed));
        const ColorSpec.SpecVersion specVersion = ColorSpec.SpecVersion.Spec2021;
        const DynamicScheme.Platform platform = DynamicScheme.Platform.Phone;

        return variant switch
        {
            ThemeColorVariants.Vibrant => new SchemeVibrant(seedHct, isDark, 0.0, specVersion, platform),
            ThemeColorVariants.Expressive => new SchemeExpressive(seedHct, isDark, 0.0, specVersion, platform),
            ThemeColorVariants.Fidelity => new SchemeFidelity(seedHct, isDark, 0.0, specVersion, platform),
            ThemeColorVariants.Content => new SchemeContent(seedHct, isDark, 0.0, specVersion, platform),
            ThemeColorVariants.Monochrome => new SchemeMonochrome(seedHct, isDark, 0.0, specVersion, platform),
            ThemeColorVariants.Neutral => new SchemeNeutral(seedHct, isDark, 0.0, specVersion, platform),
            ThemeColorVariants.Rainbow => new SchemeRainbow(seedHct, isDark, 0.0, specVersion, platform),
            _ => new SchemeTonalSpot(seedHct, isDark, 0.0, specVersion, platform)
        };
    }

    /// <summary>
    /// Maps scheme roles onto the <c>Launcher.Color.*</c> brush keys. The key set
    /// preserves the pre-M3 override behaviour (accent family + flat/state/ring
    /// derivatives) as a subset and adds the M3 secondary/tertiary role families;
    /// "Info" stays a fixed business colour and is never overridden (spec §3.4) —
    /// including Info.Background, which keeps its static Light/Dark values and is
    /// not tinted by the accent. The neutral roles are always written so switching
    /// away from seed-following cannot leave stale dynamic brushes behind.
    /// </summary>
    public static IReadOnlyDictionary<string, SolidColorBrush> BuildRoleBrushes(
        DynamicScheme scheme,
        bool seedFollowingNeutrals,
        bool isDark = false)
    {
        var result = new Dictionary<string, SolidColorBrush>(StringComparer.Ordinal);
        var primary = MaterialColorMapper.ToAvaloniaColor(scheme.Primary);

        // Pre-existing override subset (previously ApplyAccentBrushes).
        result["Launcher.Color.Primary"] = new SolidColorBrush(primary);
        result["Launcher.Color.Primary.Hover"] = new SolidColorBrush(CafeColorUtils.AdjustColor(primary, 1.15));
        result["Launcher.Color.Primary.Pressed"] = new SolidColorBrush(CafeColorUtils.AdjustColor(primary, 0.85));
        result["Launcher.Color.Primary.Soft"] = new SolidColorBrush(Color.FromArgb(0x24, primary.R, primary.G, primary.B));
        result["Launcher.Color.Primary.Border"] = new SolidColorBrush(Color.FromArgb(0x80, primary.R, primary.G, primary.B));
        result["Launcher.Color.OnPrimary"] = new SolidColorBrush(CafeColorUtils.GetReadableOnAccentColor(primary));
        result["Launcher.Color.FocusRing"] = new SolidColorBrush(Color.FromArgb(0x99, primary.R, primary.G, primary.B));
        result["Launcher.Color.Carousel.Dot.Active"] = new SolidColorBrush(primary);
        result["Launcher.Color.Button.Flat.Hover"] = new SolidColorBrush(Color.FromArgb(0x14, primary.R, primary.G, primary.B));
        result["Launcher.Color.Button.Flat.Pressed"] = new SolidColorBrush(Color.FromArgb(0x30, primary.R, primary.G, primary.B));

        var error = MaterialColorMapper.ToAvaloniaColor(scheme.Error);
        var onError = MaterialColorMapper.ToAvaloniaColor(scheme.OnError);
        result["Launcher.Color.Error"] = new SolidColorBrush(error);
        result["Launcher.Color.Error.Hover"] = new SolidColorBrush(Blend(error, onError, 0.08));
        result["Launcher.Color.Error.Pressed"] = new SolidColorBrush(Blend(error, onError, 0.16));
        result["Launcher.Color.OnError"] = new SolidColorBrush(onError);

        // M3 scheme roles.
        var secondaryContainer = MaterialColorMapper.ToAvaloniaColor(scheme.SecondaryContainer);
        var onSecondaryContainer = MaterialColorMapper.ToAvaloniaColor(scheme.OnSecondaryContainer);
        result["Launcher.Color.Secondary"] = MaterialColorMapper.ToBrush(scheme.Secondary);
        result["Launcher.Color.OnSecondary"] = MaterialColorMapper.ToBrush(scheme.OnSecondary);
        result["Launcher.Color.SecondaryContainer"] = new SolidColorBrush(secondaryContainer);
        result["Launcher.Color.OnSecondaryContainer"] = new SolidColorBrush(onSecondaryContainer);
        result["Launcher.Color.SecondaryContainer.Hover"] = new SolidColorBrush(Blend(secondaryContainer, onSecondaryContainer, 0.08));
        result["Launcher.Color.SecondaryContainer.Pressed"] = new SolidColorBrush(Blend(secondaryContainer, onSecondaryContainer, 0.16));
        result["Launcher.Color.Tertiary"] = MaterialColorMapper.ToBrush(scheme.Tertiary);
        result["Launcher.Color.OnTertiary"] = MaterialColorMapper.ToBrush(scheme.OnTertiary);
        result["Launcher.Color.TertiaryContainer"] = MaterialColorMapper.ToBrush(scheme.TertiaryContainer);
        result["Launcher.Color.OnTertiaryContainer"] = MaterialColorMapper.ToBrush(scheme.OnTertiaryContainer);
        result["Launcher.Color.PrimaryContainer"] = MaterialColorMapper.ToBrush(scheme.PrimaryContainer);
        result["Launcher.Color.OnPrimaryContainer"] = MaterialColorMapper.ToBrush(scheme.OnPrimaryContainer);

        var neutralScheme = seedFollowingNeutrals
            ? scheme
            : CreateScheme(
                Color.Parse(LauncherConstants.DefaultThemeColor),
                ThemeColorVariants.TonalSpot,
                isDark);
        result["Launcher.Color.Surface"] = MaterialColorMapper.ToBrush(neutralScheme.Surface);
        result["Launcher.Color.OnSurface"] = MaterialColorMapper.ToBrush(neutralScheme.OnSurface);
        result["Launcher.Color.Outline"] = MaterialColorMapper.ToBrush(neutralScheme.Outline);

        // The visible solid surfaces (dialogs/settings workspace) stay on the fixed
        // neutral business token by default (Q13 "surface neutral"); only the
        // seed-following strategy dyes them from the neutral scheme (Q23).
        if (seedFollowingNeutrals)
        {
            result["Launcher.Color.Dialog.Background"] =
                MaterialColorMapper.ToBrush(neutralScheme.Surface);
        }

        return result;
    }

    private static Color Blend(Color background, Color foreground, double opacity) =>
        Color.FromRgb(
            (byte)Math.Round(background.R + ((foreground.R - background.R) * opacity)),
            (byte)Math.Round(background.G + ((foreground.G - background.G) * opacity)),
            (byte)Math.Round(background.B + ((foreground.B - background.B) * opacity)));
}
