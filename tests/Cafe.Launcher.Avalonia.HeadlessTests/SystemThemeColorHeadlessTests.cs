using System;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Cafe.Launcher.Avalonia.Features.Settings;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Xunit;

namespace Cafe.Launcher.Avalonia.HeadlessTests;

public sealed class SystemThemeColorHeadlessTests
{
    [AvaloniaFact]
    public void ApplyPlatformColorValues_SystemThemeColor_ReappliesSchemeImmediately()
    {
        var application = Application.Current
            ?? throw new InvalidOperationException("Headless application is not initialised.");
        var editor = new SettingsEditor();
        editor.Current.ThemeMode = ThemeModes.Light;
        editor.Current.ThemeColorMode = ThemeColorModes.System;
        editor.Current.ThemeColorVariant = ThemeColorVariants.TonalSpot;
        using var viewModel = new SettingsAppearanceViewModel(editor);
        var accent = Color.Parse("#FF2E9E46");

        viewModel.ApplyPlatformColorValues(new PlatformColorValues
        {
            ThemeVariant = PlatformThemeVariant.Light,
            AccentColor1 = accent
        });

        var expectedScheme = MaterialSchemeGenerator.CreateScheme(
            accent,
            ThemeColorVariants.TonalSpot,
            isDark: false);
        var expectedPrimary = MaterialColorMapper.ToAvaloniaColor(expectedScheme.Primary);
        Assert.Equal(
            expectedPrimary,
            ReadThemedColor(application, "Launcher.Color.Primary", ThemeVariant.Light));
        Assert.Equal(
            expectedPrimary,
            Assert.IsType<SolidColorBrush>(viewModel.ThemeColorPreviewBrush).Color);
    }

    private static Color ReadThemedColor(Application application, string key, ThemeVariant variant)
    {
        Assert.True(
            application.Resources.TryGetResource(key, variant, out var value),
            $"Missing themed resource '{key}' ({variant}).");
        return Assert.IsType<SolidColorBrush>(value).Color;
    }
}
