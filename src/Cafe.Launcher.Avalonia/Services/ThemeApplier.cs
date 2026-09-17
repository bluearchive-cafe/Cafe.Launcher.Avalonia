using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Services;

/// <summary>
/// 进程级主题应用器：全仓唯一在运行时改写 <c>Launcher.Color.*</c> 方案画刷与
/// <see cref="Application.RequestedThemeVariant"/> 的地方（设计令牌的静态声明在 App.axaml）。
/// </summary>
/// <remarks>
/// 本类从 <see cref="Features.Settings.SettingsAppearanceViewModel"/> 搬出（计划 D14）：设置外观
/// VM 只留草稿、色板与预览状态，进程级「当前已落地的主题是什么」由本类持有，于是无头用例可以
/// 直接构造它验证方案落色与变体订阅，不必先搭一整套窗口容器。搬移不改行为——写入的键、颜色与
/// 顺序与先前一致，且订阅仍在首次 <see cref="ApplyThemeMode"/> 时懒建，因为构造期
/// <see cref="Application.Current"/> 可能尚未就绪。
/// 状态居实例而非静态（AUD-MAINT-001）：与 VM 一样是 DI 单例，实例态即进程态，但对对象图与
/// 测试可见；静态版本曾让缓存跨测试实例存续且不可见。
/// </remarks>
public sealed class ThemeApplier : IDisposable
{
    private bool lastSchemeApplied;
    private string lastThemeMode = ThemeModes.System;
    private Color lastSchemeSeed = Color.Parse(LauncherConstants.DefaultThemeColor);
    private string lastSchemeVariant = ThemeColorVariants.TonalSpot;
    private string lastSchemeStrategy = NeutralColorStrategies.BrandBlue;
    private Application? themeApplication;

    /// <summary>
    /// 应用主题模式（<see cref="ThemeModes"/>），并按新的明暗把上次落地的方案重刷一遍。
    /// </summary>
    public void ApplyThemeMode(string themeMode)
    {
        var themeVariant = themeMode switch
        {
            ThemeModes.Light => ThemeVariant.Light,
            ThemeModes.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };

        if (Application.Current is { } application)
        {
            EnsureThemeSubscription(application);
            lastThemeMode = themeMode;
            application.RequestedThemeVariant = themeVariant;

            // M3: scheme roles are theme-dependent; re-apply the last scheme so a
            // theme-mode switch updates primary/secondary/tertiary and (optionally)
            // surface roles without requiring a separate colour edit.
            if (lastSchemeApplied)
            {
                ApplyScheme(
                    lastSchemeSeed,
                    lastSchemeVariant,
                    IsDarkTheme(themeMode),
                    lastSchemeStrategy);
            }
        }
    }

    /// <summary>取系统强调色；不可得时回落到声明式默认主题色。</summary>
    public static Color GetSystemAccentColor()
    {
        if (Application.Current?.TryGetResource(
                "SystemAccentColor",
                ThemeVariant.Default,
                out var value) == true
            && value is Color color)
        {
            return color;
        }

        return Color.Parse(LauncherConstants.DefaultThemeColor);
    }

    /// <summary>
    /// Applies the M3 dynamic scheme derived from <paramref name="seed"/> onto the
    /// <c>Launcher.Color.*</c> brush keys (spec §3.4). Replaces the pre-M3
    /// <c>ApplyAccentBrushes</c>; the previous accent-family override remains a
    /// subset of <see cref="Services.MaterialSchemeGenerator.BuildRoleBrushes"/>.
    /// </summary>
    public void ApplyScheme(
        Color seed,
        string variant = ThemeColorVariants.TonalSpot,
        bool isDark = false,
        string neutralStrategy = NeutralColorStrategies.BrandBlue)
    {
        if (Application.Current is not { } application)
        {
            return;
        }

        var scheme = MaterialSchemeGenerator.CreateScheme(seed, variant, isDark);
        var roleBrushes = MaterialSchemeGenerator.BuildRoleBrushes(
            scheme,
            seedFollowingNeutrals: neutralStrategy == NeutralColorStrategies.SeedFollowing,
            isDark: isDark);
        foreach (var (key, brush) in roleBrushes)
        {
            SetBrush(application, key, brush.Color);
        }

        lastSchemeApplied = true;
        lastSchemeSeed = seed;
        lastSchemeVariant = variant;
        lastSchemeStrategy = neutralStrategy;
    }

    /// <summary>Resolves whether the effective theme is dark for a theme mode.</summary>
    public static bool IsDarkTheme(string themeMode) =>
        themeMode == ThemeModes.Dark
        || (themeMode == ThemeModes.System
            && Application.Current is { } application
            && application.ActualThemeVariant == ThemeVariant.Dark);

    private void EnsureThemeSubscription(Application application)
    {
        if (ReferenceEquals(themeApplication, application))
        {
            return;
        }

        if (themeApplication is not null)
        {
            themeApplication.ActualThemeVariantChanged -= OnActualThemeVariantChanged;
        }

        themeApplication = application;
        themeApplication.ActualThemeVariantChanged += OnActualThemeVariantChanged;
    }

    private static void SetBrush(Application application, string key, Color color)
    {
        // Mutate in place where a brush already exists (root or per-theme
        // dictionaries), so {DynamicResource} consumers observe the change.
        bool mutated = false;
        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            if (application.Resources.TryGetResource(key, variant, out var themed)
                && themed is SolidColorBrush themedBrush)
            {
                themedBrush.Color = color;
                mutated = true;
            }
        }

        if (mutated)
        {
            return;
        }

        if (application.Resources.TryGetResource(
                key,
                ThemeVariant.Default,
                out var value)
            && value is SolidColorBrush brush)
        {
            brush.Color = color;
            return;
        }

        application.Resources[key] = new SolidColorBrush(color);
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs e)
    {
        if (lastThemeMode != ThemeModes.System || !lastSchemeApplied)
        {
            return;
        }

        ApplyScheme(
            lastSchemeSeed,
            lastSchemeVariant,
            IsDarkTheme(ThemeModes.System),
            lastSchemeStrategy);
    }

    /// <summary>
    /// 退订系统变体事件。系统变体只在系统模式下驱动方案重刷，退订后本实例不再改写任何全局资源。
    /// </summary>
    public void Dispose()
    {
        if (themeApplication is not null)
        {
            themeApplication.ActualThemeVariantChanged -= OnActualThemeVariantChanged;
            themeApplication = null;
        }
    }
}
