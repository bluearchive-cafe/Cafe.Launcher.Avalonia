using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cafe.Launcher.Core.Constants;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.UI.Services;

namespace Cafe.Launcher.HeadlessTests;

public sealed class ToolTipThemeHeadlessTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Golden_ToolTip_WrapsLongTextAndUpdatesWhileOpen(bool dark)
    {
        const string url = "https://wallpapers.example.com/" +
            "preview/89b00b149b2147ed885a2b1ad18bfc2a26ef8845a9d8ba72b36b0b7fafed7c01.webp";
        var trigger = new Button
        {
            Content = "Current background",
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(24)
        };
        ToolTip.SetTip(trigger, url);
        ToolTip.SetShouldUseOverlayLayer(trigger, true);
        ToolTip.SetPlacement(trigger, PlacementMode.BottomEdgeAlignedLeft);
        var window = new Window
        {
            Content = trigger,
            Width = 400,
            Height = 160,
            FontFamily = new FontFamily("Segoe UI"),
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
        };
        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            var isDark = variant == ThemeVariant.Dark;
            var scheme = MaterialSchemeGenerator.CreateScheme(
                Color.Parse(LauncherConstants.DefaultThemeColor), ThemeColorVariants.TonalSpot, isDark);
            var brushes = MaterialSchemeGenerator.BuildRoleBrushes(scheme, seedFollowingNeutrals: false, isDark);
            window.Resources.ThemeDictionaries[variant] = new ResourceDictionary
            {
                ["Launcher.Color.Dialog.Background"] = brushes["Launcher.Color.Dialog.Background"],
                ["Launcher.Color.InverseSurface"] = brushes["Launcher.Color.InverseSurface"],
                ["Launcher.Color.InverseOnSurface"] = brushes["Launcher.Color.InverseOnSurface"]
            };
        }

        window.Bind(Window.BackgroundProperty, window.GetResourceObservable("Launcher.Color.Dialog.Background"));
        window.Show();
        try
        {
            ToolTip.SetIsOpen(trigger, true);
            Dispatcher.UIThread.RunJobs();
            var tip = Assert.Single(window.GetVisualDescendants().OfType<ToolTip>());
            var surface = Assert.Single(tip.GetVisualDescendants().OfType<Border>(), part => part.Name == "PART_LayoutRoot");
            var text = Assert.Single(tip.GetVisualDescendants().OfType<TextBlock>());

            Assert.Equal(tip.CornerRadius, surface.CornerRadius);
            Assert.Equal(tip.BorderThickness, surface.BorderThickness);
            Assert.Equal(tip.Padding, surface.Padding);
            Assert.Equal(12, text.FontSize);
            Assert.Equal(FontWeight.Normal, text.FontWeight);
            Assert.Equal(url, text.Text);
            Assert.Equal(TextWrapping.Wrap, text.TextWrapping);
            Assert.InRange(surface.Bounds.Width, 1, 320);
            Assert.True(surface.Bounds.Height > 24);

            var background = Assert.IsAssignableFrom<ISolidColorBrush>(surface.Background);
            var foreground = Assert.IsAssignableFrom<ISolidColorBrush>(text.Foreground);
            Assert.Same(window.FindResource(window.ActualThemeVariant, "ToolTipBackgroundBrush"), background);
            Assert.Same(window.FindResource(window.ActualThemeVariant, "ToolTipForegroundBrush"), foreground);
            await HeadlessTestHost.WaitUntilAsync(() => tip.Opacity == 1, TimeSpan.FromSeconds(2));
            if (dark)
            {
                GoldenScreenshot.Compare(window, "tooltip-dark");
            }
            else
            {
                GoldenScreenshot.Compare(window, "tooltip-light");
            }

            window.RequestedThemeVariant = dark ? ThemeVariant.Light : ThemeVariant.Dark;
            Dispatcher.UIThread.RunJobs();
            Assert.True(ToolTip.GetIsOpen(trigger));
            Assert.NotEqual(background.Color, Assert.IsAssignableFrom<ISolidColorBrush>(surface.Background).Color);
            Assert.NotEqual(foreground.Color, Assert.IsAssignableFrom<ISolidColorBrush>(text.Foreground).Color);
            Assert.Same(window.FindResource(window.ActualThemeVariant, "ToolTipBackgroundBrush"), surface.Background);
            Assert.Same(window.FindResource(window.ActualThemeVariant, "ToolTipForegroundBrush"), text.Foreground);

            ToolTip.SetIsOpen(trigger, false);
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(window.GetVisualDescendants().OfType<ToolTip>());
        }
        finally
        {
            window.Close();
        }
    }
}
