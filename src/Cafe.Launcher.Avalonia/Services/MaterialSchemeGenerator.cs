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

namespace Cafe.Launcher.Avalonia.Services;

/// <summary>
/// M3 dynamic-scheme generation (Q13/Q17/Q23, spec §3.4). Consumes the
/// Shirasagi0012.MaterialColorUtilities core package directly (M0 spike: GO,
/// Spec2021 default, Platform.Phone) and maps scheme roles onto the
/// <c>Launcher.Color.*</c> brush keys consumed by the UI.
/// </summary>
internal static class MaterialSchemeGenerator
{
    internal const double HoverStateOpacity = 0.08;
    internal const double PressedStateOpacity = 0.12;
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
    /// not tinted by the accent. The neutral roles and the whole dialog surface
    /// family (<c>Dialog.Background/Header/Footer/Close.Hover/Close.Pressed</c>)
    /// are always written for every strategy, so toggling seed-following can
    /// never leave stale in-place brush overrides behind (ADR-010): Brand Blue
    /// resets the family to the declared App.axaml defaults
    /// (<see cref="DialogSurfaceDefaults"/>, <see cref="NeutralContentDefaults"/>
    /// and <see cref="NeutralSurfaceDefaults"/>), seed-following dyes it from the
    /// scheme's neutral surface ladder. The full M3 container ladder is published
    /// as <c>Launcher.Color.SurfaceContainer[.Lowest/.Low/.High/.Highest]</c> for
    /// both strategies.
    /// </summary>
    public static IReadOnlyDictionary<string, SolidColorBrush> BuildRoleBrushes(
        DynamicScheme scheme,
        bool seedFollowingNeutrals,
        bool isDark = false)
    {
        var result = new Dictionary<string, SolidColorBrush>(StringComparer.Ordinal);
        var primary = MaterialColorMapper.ToAvaloniaColor(scheme.Primary);
        var onPrimary = MaterialColorMapper.ToAvaloniaColor(scheme.OnPrimary);

        // Pre-existing override subset (previously ApplyAccentBrushes).
        result["Launcher.Color.Primary"] = new SolidColorBrush(primary);
        result["Launcher.Color.Primary.Hover"] = new SolidColorBrush(Blend(primary, onPrimary, HoverStateOpacity));
        result["Launcher.Color.Primary.Pressed"] = new SolidColorBrush(Blend(primary, onPrimary, PressedStateOpacity));
        result["Launcher.Color.Primary.Soft"] = new SolidColorBrush(Color.FromArgb(0x24, primary.R, primary.G, primary.B));
        result["Launcher.Color.Primary.Border"] = new SolidColorBrush(Color.FromArgb(0x80, primary.R, primary.G, primary.B));
        result["Launcher.Color.OnPrimary"] = new SolidColorBrush(onPrimary);
        // Focus indicators use the opaque M3 primary role. Primary's tone is
        // selected against the scheme's neutral surfaces; reducing its alpha
        // can erase that contrast after compositing.
        result["Launcher.Color.FocusRing"] = new SolidColorBrush(primary);
        // The active banner indicator is a fixed over-image chrome color, not a
        // dynamic accent role. Keep it white across theme and seed changes.
        result["Launcher.Color.Carousel.Dot.Active"] = new SolidColorBrush(Colors.White);
        result["Launcher.Color.Button.Flat.Hover"] = new SolidColorBrush(
            Color.FromArgb(ToAlphaByte(HoverStateOpacity), primary.R, primary.G, primary.B));
        result["Launcher.Color.Button.Flat.Pressed"] = new SolidColorBrush(
            Color.FromArgb(ToAlphaByte(PressedStateOpacity), primary.R, primary.G, primary.B));

        var error = MaterialColorMapper.ToAvaloniaColor(scheme.Error);
        var onError = MaterialColorMapper.ToAvaloniaColor(scheme.OnError);
        result["Launcher.Color.Error"] = new SolidColorBrush(error);
        result["Launcher.Color.Error.Hover"] = new SolidColorBrush(Blend(error, onError, HoverStateOpacity));
        result["Launcher.Color.Error.Pressed"] = new SolidColorBrush(Blend(error, onError, PressedStateOpacity));
        result["Launcher.Color.OnError"] = new SolidColorBrush(onError);

        // M3 scheme roles.
        var secondaryContainer = MaterialColorMapper.ToAvaloniaColor(scheme.SecondaryContainer);
        var onSecondaryContainer = MaterialColorMapper.ToAvaloniaColor(scheme.OnSecondaryContainer);
        result["Launcher.Color.Secondary"] = MaterialColorMapper.ToBrush(scheme.Secondary);
        result["Launcher.Color.OnSecondary"] = MaterialColorMapper.ToBrush(scheme.OnSecondary);
        result["Launcher.Color.SecondaryContainer"] = new SolidColorBrush(secondaryContainer);
        result["Launcher.Color.OnSecondaryContainer"] = new SolidColorBrush(onSecondaryContainer);
        result["Launcher.Color.SecondaryContainer.Hover"] = new SolidColorBrush(Blend(secondaryContainer, onSecondaryContainer, HoverStateOpacity));
        result["Launcher.Color.SecondaryContainer.Pressed"] = new SolidColorBrush(Blend(secondaryContainer, onSecondaryContainer, PressedStateOpacity));
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
        result["Launcher.Color.OutlineVariant"] = MaterialColorMapper.ToBrush(neutralScheme.OutlineVariant);
        result["Launcher.Color.SurfaceContainer.Lowest"] = MaterialColorMapper.ToBrush(neutralScheme.SurfaceContainerLowest);
        result["Launcher.Color.SurfaceContainer.Low"] = MaterialColorMapper.ToBrush(neutralScheme.SurfaceContainerLow);
        result["Launcher.Color.SurfaceContainer"] = MaterialColorMapper.ToBrush(neutralScheme.SurfaceContainer);
        result["Launcher.Color.SurfaceContainer.High"] = MaterialColorMapper.ToBrush(neutralScheme.SurfaceContainerHigh);
        result["Launcher.Color.SurfaceContainer.Highest"] = MaterialColorMapper.ToBrush(neutralScheme.SurfaceContainerHighest);

        // M3 dialogs share SurfaceContainerHigh across body, header and footer.
        // Close states layer OnSurface over this base. Reset all neutral overrides
        // when leaving seed-following mode, including text and input fields.
        if (seedFollowingNeutrals)
        {
            var container = MaterialColorMapper.ToAvaloniaColor(neutralScheme.SurfaceContainerHigh);
            var onSurface = MaterialColorMapper.ToAvaloniaColor(neutralScheme.OnSurface);
            result["Launcher.Color.Dialog.Background"] = new SolidColorBrush(container);
            result["Launcher.Color.Dialog.Footer"] = new SolidColorBrush(container);
            result["Launcher.Color.Dialog.Header"] = new SolidColorBrush(container);
            result["Launcher.Color.Dialog.Close.Hover"] = new SolidColorBrush(Blend(container, onSurface, HoverStateOpacity));
            result["Launcher.Color.Dialog.Close.Pressed"] = new SolidColorBrush(Blend(container, onSurface, PressedStateOpacity));
            result["Launcher.Text.Primary"] = MaterialColorMapper.ToBrush(neutralScheme.OnSurface);
            result["Launcher.Text.Secondary"] = MaterialColorMapper.ToBrush(neutralScheme.OnSurfaceVariant);
            result["Launcher.Text.Body"] = MaterialColorMapper.ToBrush(neutralScheme.OnSurfaceVariant);
            result["Launcher.Color.Field.Background"] = MaterialColorMapper.ToBrush(neutralScheme.SurfaceContainerHighest);
            result["Launcher.Color.Field.Border"] = MaterialColorMapper.ToBrush(neutralScheme.Outline);

            // Content surfaces join the same ladder: cards are elevated
            // containers (brighter than the dialog in dark, Low in light), rows
            // the mid step, soft boundaries the variant outline. Semi-transparent
            // wallpaper-overlaid panels stay brand chrome and are not mapped.
            var outlineVariant = MaterialColorMapper.ToAvaloniaColor(neutralScheme.OutlineVariant);
            result["Launcher.Color.Card.Background"] = new SolidColorBrush(isDark
                ? MaterialColorMapper.ToAvaloniaColor(neutralScheme.SurfaceContainerHighest)
                : MaterialColorMapper.ToAvaloniaColor(neutralScheme.SurfaceContainerLow));
            result["Launcher.Color.Card.Border"] = new SolidColorBrush(outlineVariant);
            result["Launcher.Color.Content.Row"] = MaterialColorMapper.ToBrush(neutralScheme.SurfaceContainer);
            result["Launcher.Color.Button.Border"] = new SolidColorBrush(outlineVariant);
            result["Launcher.Color.SiteButton.Background"] = new SolidColorBrush(isDark
                ? MaterialColorMapper.ToAvaloniaColor(neutralScheme.SurfaceContainer)
                : MaterialColorMapper.ToAvaloniaColor(neutralScheme.Surface));
            result["Launcher.Color.SiteButton.Border"] = new SolidColorBrush(outlineVariant);
            result["Launcher.Color.Toast.Background"] = new SolidColorBrush(isDark
                ? MaterialColorMapper.ToAvaloniaColor(neutralScheme.SurfaceContainerHigh)
                : MaterialColorMapper.ToAvaloniaColor(neutralScheme.Surface));

            // M3 状态层预合成盘（hover 8% / pressed 12% 的 onSurface），供 radio
            // 等选择控件的图标底盘消费；不透明度与 Launcher.StateLayer.* 一致。
            result["Launcher.Color.StateLayer.OnSurface.Hover"] = new SolidColorBrush(
                Color.FromArgb(ToAlphaByte(HoverStateOpacity), onSurface.R, onSurface.G, onSurface.B));
            result["Launcher.Color.StateLayer.OnSurface.Pressed"] = new SolidColorBrush(
                Color.FromArgb(ToAlphaByte(PressedStateOpacity), onSurface.R, onSurface.G, onSurface.B));
        }
        else
        {
            foreach (var (key, light, dark) in DialogSurfaceDefaults)
            {
                result[key] = new SolidColorBrush(Color.Parse(isDark ? dark : light));
            }

            foreach (var (key, light, dark) in NeutralContentDefaults)
            {
                result[key] = new SolidColorBrush(Color.Parse(isDark ? dark : light));
            }

            foreach (var (key, light, dark) in NeutralSurfaceDefaults)
            {
                result[key] = new SolidColorBrush(Color.Parse(isDark ? dark : light));
            }

            foreach (var (key, light, dark) in StateLayerDefaults)
            {
                result[key] = new SolidColorBrush(Color.Parse(isDark ? dark : light));
            }
        }

        return result;
    }

    internal static readonly (string Key, string Light, string Dark)[] NeutralSurfaceDefaults =
    [
        ("Launcher.Color.Card.Background", "#FFFAFCFF", "#FF202733"),
        ("Launcher.Color.Card.Border", "#FFD6E2EE", "#FF344150"),
        ("Launcher.Color.Content.Row", "#FFF1F5F9", "#FF202833"),
        ("Launcher.Color.Button.Border", "#FFC8D2DE", "#FF4A5B73"),
        ("Launcher.Color.SiteButton.Background", "#FFFFFFFF", "#FF192232"),
        ("Launcher.Color.SiteButton.Border", "#FFD5E3F3", "#FF3D5B80"),
        ("Launcher.Color.Toast.Background", "#FFFFFFFF", "#FF1C2533"),
    ];

    /// <summary>
    /// Declared App.axaml on-surface state-layer defaults as (key, light, dark)
    /// rows: pre-composed M3 state layers (hover 8% / pressed 12% of onSurface)
    /// consumed by the radio glyph disc. Alpha bytes are the quantised ratios
    /// produced by <c>ToAlphaByte</c> (8% → 0x14, 12% → 0x1F). The Brand Blue
    /// strategy resets these to neutral chrome, and UiStyleContractTests asserts
    /// the XAML declarations against this table so the two cannot drift apart.
    /// </summary>
    internal static readonly (string Key, string Light, string Dark)[] StateLayerDefaults =
    [
        ("Launcher.Color.StateLayer.OnSurface.Hover", "#14000000", "#14FFFFFF"),
        ("Launcher.Color.StateLayer.OnSurface.Pressed", "#1F000000", "#1FFFFFFF"),
    ];

    /// <summary>
    /// Declared App.axaml dialog-surface defaults as (key, light, dark) rows.
    /// The Brand Blue strategy resets the dialog family to these values, and
    /// UiStyleContractTests asserts the XAML declarations against this table so
    /// the two cannot drift apart.
    /// </summary>
    internal static readonly (string Key, string Light, string Dark)[] DialogSurfaceDefaults =
    [
        ("Launcher.Color.Dialog.Background", "#FFFFFFFF", "#FF161C26"),
        ("Launcher.Color.Dialog.Header", "#FFF4F8FD", "#FF1B2430"),
        ("Launcher.Color.Dialog.Footer", "#FFFFFFFF", "#FF161C26"),
        ("Launcher.Color.Dialog.Close.Hover", "#FFEDF2F7", "#FF2A3547"),
        ("Launcher.Color.Dialog.Close.Pressed", "#FFDDE6F0", "#FF344156"),
    ];

    internal static readonly (string Key, string Light, string Dark)[] NeutralContentDefaults =
    [
        ("Launcher.Text.Primary", "#FF232A31", "#FFE8EEF6"),
        ("Launcher.Text.Secondary", "#FF646D79", "#FFA8B3C2"),
        ("Launcher.Text.Body", "#FF3F4954", "#FFC8D2DF"),
        ("Launcher.Color.Field.Background", "#FFF0F6FD", "#FF1E2834"),
        ("Launcher.Color.Field.Border", "#FF788EA7", "#FF5E7494"),
    ];

    private static Color Blend(Color background, Color foreground, double opacity) =>
        Color.FromRgb(
            (byte)Math.Round(background.R + ((foreground.R - background.R) * opacity)),
            (byte)Math.Round(background.G + ((foreground.G - background.G) * opacity)),
            (byte)Math.Round(background.B + ((foreground.B - background.B) * opacity)));

    /// <summary>
    /// 预合成状态层需要 byte alpha，而令牌持有的是精确比例
    /// （<see cref="HoverStateOpacity"/> 0.08 / <see cref="PressedStateOpacity"/> 0.12）。
    /// 量化统一收敛到这里（8% → 0x14、12% → 0x1F），调用点不再各写一份十六进制字面量，
    /// 刻度调整时也不会漏改。
    /// </summary>
    private static byte ToAlphaByte(double opacity) => (byte)Math.Round(opacity * 255);
}
