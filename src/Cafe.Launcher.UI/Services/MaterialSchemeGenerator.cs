using System;
using System.Collections.Generic;
using Avalonia.Media;
using Cafe.Launcher.UI.Constants;
using Cafe.Launcher.UI.Helpers;
using Cafe.Launcher.UI.Models;
using MaterialColorUtilities.DynamicColors;
using MaterialColorUtilities.HCT;
using MaterialColorUtilities.Scheme;
using MaterialColorUtilities.Utils;
using Cafe.Launcher.Core.Constants;
using Cafe.Launcher.Core.Models;
using CafeColorUtils = Cafe.Launcher.UI.Helpers.ColorUtils;

namespace Cafe.Launcher.UI.Services;

/// <summary>
/// M3 dynamic-scheme generation (Q13/Q17/Q23, spec §2.4). Consumes the
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
    /// "Info" stays a fixed business colour and is never overridden (spec §2.4) —
    /// including Info.Background, which keeps its static Light/Dark values and is
    /// not tinted by the accent. The neutral roles and the whole dialog surface
    /// family (<c>Dialog.Background/Header/Footer/Close.Hover/Close.Pressed</c>)
    /// are always written for every strategy, so toggling seed-following can
    /// never leave stale in-place brush overrides behind (ADR-010): Brand Blue
    /// resets the family to the declared App.axaml defaults
    /// (<see cref="DialogSurfaceDefaults"/>), seed-following dyes it from the
    /// scheme's neutral surface ladder.
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
        // The active banner indicator is a fixed over-image chrome color, not a
        // dynamic accent role. Keep it white across theme and seed changes.
        result["Launcher.Color.Carousel.Dot.Active"] = new SolidColorBrush(Colors.White);
        // Fluent 2 interaction states are neutral black/white layers, never an
        // accent wash; flat rows must not pick up a seed hue on hover.
        var neutralState = isDark ? Colors.White : Colors.Black;
        result["Launcher.Color.Button.Flat.Hover"] = new SolidColorBrush(
            Color.FromArgb(0x0F, neutralState.R, neutralState.G, neutralState.B));
        result["Launcher.Color.Button.Flat.Pressed"] = new SolidColorBrush(
            Color.FromArgb(isDark ? (byte)0x17 : (byte)0x18, neutralState.R, neutralState.G, neutralState.B));
        // Social chips float directly on the wallpaper, so translucent state
        // washes would show the backdrop through them. Pre-composite their state
        // layers onto the opaque Content.Row base (defaults in App.axaml) instead.
        var socialChipBase = Color.Parse(isDark ? "#FF202833" : "#FFF1F5F9");
        result["Launcher.Color.SocialChip.Hover"] = new SolidColorBrush(Blend(socialChipBase, primary, 0.08));
        result["Launcher.Color.SocialChip.Pressed"] = new SolidColorBrush(Blend(socialChipBase, primary, 0.19));

        var error = MaterialColorMapper.ToAvaloniaColor(scheme.Error);
        var onError = MaterialColorMapper.ToAvaloniaColor(scheme.OnError);
        result["Launcher.Color.Error"] = new SolidColorBrush(error);
        result["Launcher.Color.Error.Hover"] = new SolidColorBrush(Blend(error, onError, 0.08));
        result["Launcher.Color.Error.Pressed"] = new SolidColorBrush(Blend(error, onError, 0.16));
        result["Launcher.Color.OnError"] = new SolidColorBrush(onError);
        // FA 原生 ComboBox 模板的 :error 描边引用系统关键色；桥接到应用错误档。
        result["SystemFillColorCriticalBrush"] = result["Launcher.Color.Error"];

        // M3 scheme roles.
        result["Launcher.Color.Secondary"] = MaterialColorMapper.ToBrush(scheme.Secondary);
        result["Launcher.Color.OnSecondary"] = MaterialColorMapper.ToBrush(scheme.OnSecondary);
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
        result["Launcher.Color.InverseSurface"] = MaterialColorMapper.ToBrush(neutralScheme.InverseSurface);
        result["Launcher.Color.InverseOnSurface"] = MaterialColorMapper.ToBrush(neutralScheme.InverseOnSurface);
        result["Launcher.Color.Outline"] = MaterialColorMapper.ToBrush(neutralScheme.Outline);

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
            result["Launcher.Color.Dialog.Close.Hover"] = new SolidColorBrush(Blend(container, onSurface, 0.08));
            result["Launcher.Color.Dialog.Close.Pressed"] = new SolidColorBrush(Blend(container, onSurface, 0.12));
            result["Launcher.Text.Primary"] = MaterialColorMapper.ToBrush(neutralScheme.OnSurface);
            result["Launcher.Text.Secondary"] = MaterialColorMapper.ToBrush(neutralScheme.OnSurfaceVariant);
            result["Launcher.Text.Body"] = MaterialColorMapper.ToBrush(neutralScheme.OnSurfaceVariant);
            result["Launcher.Color.Card.Background"] = MaterialColorMapper.ToBrush(neutralScheme.SurfaceContainerHighest);
            result["Launcher.Color.Field.Background"] = MaterialColorMapper.ToBrush(neutralScheme.SurfaceContainerHighest);
            result["Launcher.Color.Field.Border"] = MaterialColorMapper.ToBrush(neutralScheme.Outline);
            result["Launcher.Color.Button.Secondary.Background"] = MaterialColorMapper.ToBrush(neutralScheme.SurfaceContainerHighest);
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
        }

        // 壁纸上的半透明表面沿用各自宿主色的明暗，只把不透明度降到表面档；
        // 主界面边框与浮层取对话框表面，嵌套卡片取内容卡表面。
        result["Launcher.Color.Glass.Background"] = new SolidColorBrush(
            WithAlpha(result["Launcher.Color.Dialog.Background"].Color, GlassBackgroundAlpha));
        result["Launcher.Color.Glass.Card.Background"] = new SolidColorBrush(
            WithAlpha(result["Launcher.Color.Card.Background"].Color, GlassCardAlpha));
        // 首页左侧图标栏使用对话框表面的高不透明度档；任务卡、菜单与工具提示
        // 浮在壁纸最上层，取内容卡表面并提高一档不透明度。
        result["Launcher.Color.Glass.Rail.Background"] = new SolidColorBrush(
            WithAlpha(result["Launcher.Color.Dialog.Background"].Color, GlassRailAlpha));
        result["Launcher.Color.Glass.Strong.Background"] = new SolidColorBrush(
            WithAlpha(result["Launcher.Color.Card.Background"].Color, isDark ? GlassStrongDarkAlpha : GlassStrongLightAlpha));

        // 标准次要操作在设置、向导和对话框中共用同一不透明表面。状态层使用
        // 中性前景预混，避免旧的透明 accent wash 在不同宿主上显出不同底色。
        var secondaryBackground = result["Launcher.Color.Button.Secondary.Background"].Color;
        var secondaryForeground = result["Launcher.Text.Primary"].Color;
        result["Launcher.Color.Button.Secondary.Hover"] = new SolidColorBrush(Blend(secondaryBackground, secondaryForeground, 0.08));
        result["Launcher.Color.Button.Secondary.Pressed"] = new SolidColorBrush(Blend(secondaryBackground, secondaryForeground, 0.16));
        // 旧 token 保留为兼容面，每次方案应用都同步，不能残留上一次策略的底色。
        result["Launcher.Color.Dialog.Action.Background"] = new SolidColorBrush(secondaryBackground);

        // ADR-070 第二批：设置下拉与折叠区走 FA 原生模板，状态色必须经键覆盖
        // 跟随方案。下拉复用 Field 实心底与同色描边（配对对比度由测试锁定），
        // 悬停/按压不换底；折叠区头部静止面保持透明，悬停/按压描边取 Field.Border。
        result["ComboBoxBackground"] = result["Launcher.Color.Field.Background"];
        result["ComboBoxBackgroundPointerOver"] = result["Launcher.Color.Field.Background"];
        result["ComboBoxBackgroundPressed"] = result["Launcher.Color.Field.Background"];
        result["ComboBoxBorderBrush"] = result["Launcher.Color.Field.Border"];
        result["ComboBoxBorderBrushPointerOver"] = result["Launcher.Color.Field.Border"];
        result["ExpanderHeaderBorderPointerOverBrush"] = result["Launcher.Color.Field.Border"];
        result["ExpanderBorderPressedBrush"] = result["Launcher.Color.Field.Border"];
        result["ExpanderHeaderBorderPressedBrush"] = result["Launcher.Color.Field.Border"];

        // Fluent 2 selection is a neutral state fill composited onto the dialog
        // surface (opaque, so it reads the same on every host), with primary text.
        // The historical M3 role names stay as the key surface for existing styles.
        var dialogSurface = result["Launcher.Color.Dialog.Background"].Color;
        var selectionState = isDark ? Colors.White : Colors.Black;
        var selectionFill = Blend(dialogSurface, selectionState, isDark ? 0.08 : 0.06);
        var selectionHover = Blend(dialogSurface, selectionState, isDark ? 0.11 : 0.09);
        var selectionPressed = Blend(dialogSurface, selectionState, isDark ? 0.14 : 0.12);
        var selectionForeground = result["Launcher.Text.Primary"].Color;
        result["Launcher.Color.SecondaryContainer"] = new SolidColorBrush(selectionFill);
        result["Launcher.Color.SecondaryContainer.Hover"] = new SolidColorBrush(selectionHover);
        result["Launcher.Color.SecondaryContainer.Pressed"] = new SolidColorBrush(selectionPressed);
        result["Launcher.Color.OnSecondaryContainer"] = new SolidColorBrush(
            ReadableContainerForeground(selectionForeground, selectionFill, selectionForeground));
        result["Launcher.Color.OnSecondaryContainer.Hover"] = new SolidColorBrush(
            ReadableContainerForeground(selectionForeground, selectionHover, selectionForeground));
        result["Launcher.Color.OnSecondaryContainer.Pressed"] = new SolidColorBrush(
            ReadableContainerForeground(selectionForeground, selectionPressed, selectionForeground));

        // 焦点是独立于交互填充的标识。强调色在浅色种子或深色表面上可能不足
        // 3:1，此时使用同一中性方案的 OnSurface；实心按钮另用 OnPrimary/OnError。
        var focusColor = primary;
        foreach (var backgroundKey in new[]
                 {
                     "Launcher.Color.Dialog.Background", "Launcher.Color.Field.Background",
                     "Launcher.Color.Button.Secondary.Background", "Launcher.Color.Button.Secondary.Hover",
                     "Launcher.Color.Button.Secondary.Pressed"
                 })
        {
            if (CafeColorUtils.GetContrastRatio(focusColor, result[backgroundKey].Color) < 3)
            {
                focusColor = MaterialColorMapper.ToAvaloniaColor(neutralScheme.OnSurface);
                break;
            }
        }

        result["Launcher.Color.FocusRing"] = new SolidColorBrush(focusColor);

        // Settings selection retains the accent wash. Saturated Fidelity/Content
        // schemes may need a neutral foreground on the actually composited fill.
        var selectedWash = result["Launcher.Color.Primary.Soft"].Color;
        foreach (var (suffix, backgroundKey) in new[]
                 {
                     ("", "Launcher.Color.Dialog.Background"),
                     (".Hover", "Launcher.Color.Button.Secondary.Hover"),
                     (".Pressed", "Launcher.Color.Button.Secondary.Pressed")
                 })
        {
            var selectedFill = Blend(result[backgroundKey].Color, primary, selectedWash.A / 255d);
            result[$"Launcher.Color.Settings.Select.Selected.Foreground{suffix}"] = new SolidColorBrush(
                ReadableContainerForeground(primary, selectedFill, result["Launcher.Text.Primary"].Color));
        }

        return result;
    }

    /// <summary>
    /// Declared App.axaml dialog-surface defaults as (key, light, dark) rows.
    /// The Brand Blue strategy resets the dialog family to these values, and
    /// UiStyleContractTests asserts the XAML declarations against this table so
    /// the two cannot drift apart.
    /// </summary>
    /// <summary>半透明表面 alpha：边框/浮层 90%，嵌套卡片 94%；与 App.axaml 声明值同步。</summary>
    internal const byte GlassBackgroundAlpha = 0xE6;
    internal const byte GlassCardAlpha = 0xF0;
    internal const byte GlassRailAlpha = 0xEB;
    internal const byte GlassStrongLightAlpha = 0xC9;
    internal const byte GlassStrongDarkAlpha = 0xD1;

    /// <summary>
    /// App.axaml 中半透明表面声明的 (key, light, dark) 行；Brand Blue 与 XAML
    /// 契约测试共用，防止运行时合成值与声明值漂移。
    /// </summary>
    internal static readonly (string Key, string Light, string Dark)[] GlassSurfaceDefaults =
    [
        ("Launcher.Color.Glass.Background", "#E6F3F3F3", "#E6202020"),
        ("Launcher.Color.Glass.Card.Background", "#F0FFFFFF", "#F02B2B2B"),
        ("Launcher.Color.Glass.Rail.Background", "#EBF3F3F3", "#EB202020"),
        ("Launcher.Color.Glass.Strong.Background", "#C9FFFFFF", "#D12B2B2B"),
    ];

    internal static readonly (string Key, string Light, string Dark)[] DialogSurfaceDefaults =
    [
        ("Launcher.Color.Dialog.Background", "#FFF3F3F3", "#FF202020"),
        ("Launcher.Color.Dialog.Header", "#FFF3F3F3", "#FF202020"),
        ("Launcher.Color.Dialog.Footer", "#FFF3F3F3", "#FF202020"),
        ("Launcher.Color.Dialog.Close.Hover", "#FFE9E9E9", "#FF2E2E2E"),
        ("Launcher.Color.Dialog.Close.Pressed", "#FFDEDEDE", "#FF383838"),
    ];

    internal static readonly (string Key, string Light, string Dark)[] NeutralContentDefaults =
    [
        ("Launcher.Color.Card.Background", "#FFFFFFFF", "#FF2B2B2B"),
        ("Launcher.Text.Primary", "#FF1A1A1A", "#FFFFFFFF"),
        ("Launcher.Text.Secondary", "#FF5F5F5F", "#FFC5C5C5"),
        ("Launcher.Text.Body", "#FF454545", "#FFD0D0D0"),
        ("Launcher.Color.Field.Background", "#FFFFFFFF", "#FF2D2D2D"),
        ("Launcher.Color.Field.Border", "#FF8A8A8A", "#FF9A9A9A"),
        ("Launcher.Color.Button.Secondary.Background", "#FFFBFBFB", "#FF2D2D2D"),
    ];

    // Keep the scheme's on-container hue wherever it meets AA. State layers can
    // cross the threshold in Fidelity/Content; even OnSurface is insufficient for
    // some middle-tone fills, so compare black/white as the final readable pair.
    private static Color ReadableContainerForeground(Color preferred, Color background, Color onSurface)
    {
        if (CafeColorUtils.GetContrastRatio(preferred, background) >= 4.5)
        {
            return preferred;
        }

        if (CafeColorUtils.GetContrastRatio(onSurface, background) >= 4.5)
        {
            return onSurface;
        }

        return CafeColorUtils.GetContrastRatio(Colors.Black, background) > CafeColorUtils.GetContrastRatio(Colors.White, background)
            ? Colors.Black
            : Colors.White;
    }

    private static Color Blend(Color background, Color foreground, double opacity) =>
        Color.FromRgb(
            (byte)Math.Round(background.R + ((foreground.R - background.R) * opacity)),
            (byte)Math.Round(background.G + ((foreground.G - background.G) * opacity)),
            (byte)Math.Round(background.B + ((foreground.B - background.B) * opacity)));

    private static Color WithAlpha(Color color, byte alpha) =>
        Color.FromArgb(alpha, color.R, color.G, color.B);
}
