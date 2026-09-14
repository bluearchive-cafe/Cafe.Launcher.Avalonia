using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Cafe.Launcher.Avalonia.Features.Settings;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cafe.Launcher.Avalonia.HeadlessTests;

/// <summary>
/// ADR-010 regression: toggling the neutral strategy rewrites the whole dialog
/// surface family in place, so switching seed-following off must restore the
/// declared App.axaml defaults instead of leaving seed-tinted brushes behind.
/// </summary>
public sealed class NeutralStrategyHeadlessTests
{
    [AvaloniaFact]
    public void ApplyScheme_TogglingSeedFollowingOff_RestoresDeclaredDialogSurfaceFamily()
    {
        var application = Application.Current
            ?? throw new InvalidOperationException("Headless application is not initialised.");
        var dialogDefaults = MaterialSchemeGenerator.DialogSurfaceDefaults
            .Concat(MaterialSchemeGenerator.NeutralContentDefaults).ToArray();

        // Snapshot the in-place brushes this test mutates; headless tests share
        // one Application and run sequentially.
        var variants = new[] { ThemeVariant.Light, ThemeVariant.Dark };
        var snapshot = dialogDefaults
            .SelectMany(row => variants.Select(variant => (row.Key, variant)))
            .Select(entry => (
                entry.Key,
                entry.variant,
                Color: ReadThemedColor(application, entry.Key, entry.variant)))
            .ToList();

        // ApplyScheme is an instance member of the DI-singleton appearance VM
        // (AUD-MAINT-001); scaffold the same provider the window tests use and
        // dispose it so the theme-variant subscription detaches afterwards.
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var provider = HeadlessTestHost.CreateServiceProvider(tempDir);
        var appearance = provider.GetRequiredService<MainWindowViewModel>().Settings.Appearance;
        var seed = Color.Parse("#FF2E9E46");
        try
        {
            appearance.ApplyScheme(
                seed,
                ThemeColorVariants.TonalSpot,
                isDark: false,
                NeutralColorStrategies.SeedFollowing);
            Assert.NotEqual(
                Color.Parse(dialogDefaults[0].Light),
                ReadThemedColor(application, "Launcher.Color.Dialog.Background", ThemeVariant.Light));

            appearance.ApplyScheme(
                seed,
                ThemeColorVariants.TonalSpot,
                isDark: false,
                NeutralColorStrategies.BrandBlue);
            foreach (var row in dialogDefaults)
            {
                Assert.Equal(
                    Color.Parse(row.Light),
                    ReadThemedColor(application, row.Key, ThemeVariant.Light));
            }

            // The reset also holds for the dark theme path.
            appearance.ApplyScheme(
                seed,
                ThemeColorVariants.TonalSpot,
                isDark: true,
                NeutralColorStrategies.BrandBlue);
            foreach (var row in dialogDefaults)
            {
                Assert.Equal(
                    Color.Parse(row.Dark),
                    ReadThemedColor(application, row.Key, ThemeVariant.Dark));
            }
        }
        finally
        {
            foreach (var (key, variant, color) in snapshot)
            {
                if (application.Resources.TryGetResource(key, variant, out var value)
                    && value is SolidColorBrush brush)
                {
                    brush.Color = color;
                }
            }

            provider.Dispose();
            Directory.Delete(tempDir, true);
        }
    }

    private static Color ReadThemedColor(Application application, string key, ThemeVariant variant)
    {
        Assert.True(
            application.Resources.TryGetResource(key, variant, out var value),
            $"Missing themed resource '{key}' ({variant}).");
        return Assert.IsType<SolidColorBrush>(value).Color;
    }
}
