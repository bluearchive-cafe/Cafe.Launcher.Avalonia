using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.UI.Services;

namespace Cafe.Launcher.HeadlessTests;

/// <summary>Guard generated brush alpha against multiplication by a stale resource opacity.</summary>
public sealed class ThemeApplierHeadlessTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ApplyScheme_ExistingTranslucentBrush_PreservesIdentityAndGeneratedOpacity(bool dark)
    {
        using var theme = ThemeVariantSnapshot.Capture(dark ? ThemeVariant.Dark : ThemeVariant.Light);
        var application = Application.Current!;
        using var applier = new ThemeApplier();
        foreach (var seed in new[] { "#2E7DF6", "#FF0000" })
        {
            var scheme = MaterialSchemeGenerator.CreateScheme(Color.Parse(seed), ThemeColorVariants.TonalSpot, dark);
            var expected = MaterialSchemeGenerator.BuildRoleBrushes(scheme, false, dark);
            foreach (var key in new[] { "Launcher.Color.Primary.Soft", "Launcher.Color.Primary.Border", "Launcher.Color.FocusRing", "Launcher.Color.Button.Secondary.Background" })
            {
                Assert.True(application.TryGetResource(key, application.ActualThemeVariant, out var existing));
                var brush = Assert.IsType<SolidColorBrush>(existing);
                brush.Opacity = 0.1;
                try
                {
                    applier.ApplyScheme(Color.Parse(seed), isDark: dark);
                    Assert.True(application.TryGetResource(key, application.ActualThemeVariant, out var updated));
                    Assert.Same(brush, updated);
                    Assert.Equal(expected[key].Color, brush.Color);
                    Assert.Equal(expected[key].Opacity, brush.Opacity);
                }
                finally
                {
                    brush.Opacity = 1;
                }
            }
        }
    }
}
