using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.UI.Helpers;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.UI.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Cafe.Launcher.HeadlessTests;

public sealed partial class MainWindowHeadlessTests
{
    [AvaloniaTheory]
    [InlineData(false, "#FF0000")]
    [InlineData(true, "#00FF00")]
    public void Segments_SelectedStates_KeepPaintedContrast(bool dark, string seed)
    {
        using var theme = ThemeVariantSnapshot.Capture(dark ? ThemeVariant.Dark : ThemeVariant.Light);
        using var context = CreateContext();
        PrepareGoldenWindow(context, dark ? ThemeVariant.Dark : ThemeVariant.Light);
        OpenSettings(context);
        var applier = context.Provider.GetRequiredService<ThemeApplier>();
        applier.ApplyThemeMode(dark ? ThemeModes.Dark : ThemeModes.Light);
        foreach (var variant in new[] { ThemeColorVariants.TonalSpot, ThemeColorVariants.Fidelity, ThemeColorVariants.Content })
        {
            applier.ApplyScheme(Color.Parse(seed), variant, dark, NeutralColorStrategies.SeedFollowing);
            var background = PaintColor((IBrush)context.Window.FindResource(context.Window.ActualThemeVariant, "Launcher.Color.Dialog.Background")!);
            var segments = context.Window.GetVisualDescendants().OfType<RadioButton>()
                .Where(button => button.Classes.Contains("segment-option") && button.IsChecked == true && button.IsEffectivelyVisible).ToArray();
            Assert.NotEmpty(segments);
            foreach (var segment in segments)
            {
                foreach (var state in new[] { ":checked", ":pointerover", ":pressed" })
                {
                    ((IPseudoClasses)segment.Classes).Set(state, true);
                    Dispatcher.UIThread.RunJobs();
                    var fill = PreviewComposite(segment.Background, background);
                    var text = Assert.Single(segment.GetVisualDescendants().OfType<TextBlock>());
                    Assert.True(ColorUtils.GetContrastRatio(PaintColor(text.Foreground), fill) >= 4.5,
                        $"{variant}/{dark}/{state}: selected segment text contrast");
                    if (state != ":checked")
                        ((IPseudoClasses)segment.Classes).Set(state, false);
                }
            }
        }
    }

    [AvaloniaTheory]
    [InlineData(false, "#2E7DF6", ThemeColorVariants.Fidelity)]
    [InlineData(false, "#FF0000", ThemeColorVariants.Content)]
    [InlineData(true, "#00FF00", ThemeColorVariants.Fidelity)]
    [InlineData(true, "#FFFF00", ThemeColorVariants.Content)]
    public void SelectedChoice_WhenFocusedAndHovered_PreservesRenderedContrast(bool dark, string seed, string variant)
    {
        using var theme = ThemeVariantSnapshot.Capture(dark ? ThemeVariant.Dark : ThemeVariant.Light);
        using var context = CreateContext();
        PrepareGoldenWindow(context, dark ? ThemeVariant.Dark : ThemeVariant.Light);
        var combo = new ComboBox { ItemsSource = new[] { "Selected", "Other" }, SelectedIndex = 0, Width = 280, Classes = { "setting-control" } };
        var window = new Window { Width = 600, Height = 400, Content = combo };
        window.Bind(Window.BackgroundProperty, window.GetResourceObservable("Launcher.Color.Dialog.Background"));
        try
        {
            window.Show();
            foreach (var strategy in new[] { NeutralColorStrategies.BrandBlue, NeutralColorStrategies.SeedFollowing })
            {
                var applier = context.Provider.GetRequiredService<ThemeApplier>();
                applier.ApplyThemeMode(dark ? ThemeModes.Dark : ThemeModes.Light);
                applier.ApplyScheme(Color.Parse(seed), variant, dark, strategy);
                var popup = Assert.Single(combo.GetVisualDescendants().OfType<Popup>());
                popup.ShouldUseOverlayLayer = true;
                combo.IsDropDownOpen = true;
                Dispatcher.UIThread.RunJobs();
                var item = Assert.IsType<ComboBoxItem>(combo.ContainerFromIndex(0));
                // Reopening can restore this same container with programmatic focus;
                // move away first so the next focus is genuinely keyboard-originated.
                Assert.IsType<ComboBoxItem>(combo.ContainerFromIndex(1)).Focus(NavigationMethod.Unspecified);
                item.Focus(NavigationMethod.Tab);
                Dispatcher.UIThread.RunJobs();
                Assert.Contains(":focus-visible", item.Classes);
                var painted = Assert.Single(item.GetVisualDescendants().OfType<ContentPresenter>(), part => part.Name == "PART_ContentPresenter");
                var focus = Assert.Single(item.GetVisualDescendants().OfType<Border>(), part => part.Name == "ChoiceFocus");
                AssertSelectedPaint(painted, focus);
                var bounds = item.Bounds;
                window.MouseMove(item.TranslatePoint(new Point(25, 15), window)!.Value);
                Dispatcher.UIThread.RunJobs();
                Assert.Contains(":pointerover", item.Classes);
                AssertSelectedPaint(painted, focus);
                // Exercise the style's pressed branch without committing/closing the native popup.
                ((IPseudoClasses)item.Classes).Set(":pressed", true);
                Dispatcher.UIThread.RunJobs();
                AssertSelectedPaint(painted, focus);
                Assert.Equal(bounds, item.Bounds);
                ((IPseudoClasses)item.Classes).Set(":pressed", false);
                // A live theme switch must refresh the already focused popup row as well.
                applier.ApplyThemeMode(dark ? ThemeModes.Light : ThemeModes.Dark);
                Dispatcher.UIThread.RunJobs();
                AssertSelectedPaint(painted, focus);
                combo.IsDropDownOpen = false;
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void SettingsNavigation_WhenSelectedAndHovered_PreservesRenderedTextContrast(bool dark)
    {
        using var theme = ThemeVariantSnapshot.Capture(dark ? ThemeVariant.Dark : ThemeVariant.Light);
        using var context = CreateContext();
        PrepareGoldenWindow(context, dark ? ThemeVariant.Dark : ThemeVariant.Light);
        OpenSettings(context);
        var selected = Assert.Single(context.Window.GetVisualDescendants().OfType<ListBoxItem>(), item => item.IsSelected && item.IsEffectivelyVisible);
        var painted = Assert.Single(selected.GetVisualDescendants().OfType<ContentPresenter>(), part => part.Name == "PART_ContentPresenter");
        foreach (var variant in new[] { ThemeColorVariants.Fidelity, ThemeColorVariants.Content })
        foreach (var strategy in new[] { NeutralColorStrategies.BrandBlue, NeutralColorStrategies.SeedFollowing })
        {
            context.Provider.GetRequiredService<ThemeApplier>().ApplyScheme(Color.Parse(dark ? "#FFFF00" : "#2E7DF6"), variant, dark, strategy);
            context.Window.MouseMove(selected.TranslatePoint(new Point(35, 15), context.Window)!.Value);
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(":pointerover", selected.Classes);
            Assert.True(ColorUtils.GetContrastRatio(PaintColor(painted.Foreground), PaintColor(painted.Background)) >= 4.5);
            ((IPseudoClasses)selected.Classes).Set(":pressed", true);
            Dispatcher.UIThread.RunJobs();
            Assert.True(ColorUtils.GetContrastRatio(PaintColor(painted.Foreground), PaintColor(painted.Background)) >= 4.5);
            ((IPseudoClasses)selected.Classes).Set(":pressed", false);
        }
    }

    [AvaloniaTheory]
    [InlineData(LauncherLanguages.English, false)]
    [InlineData(LauncherLanguages.SimplifiedChinese, false)]
    [InlineData(LauncherLanguages.TraditionalChinese, false)]
    [InlineData(LauncherLanguages.Japanese, false)]
    [InlineData(LauncherLanguages.English, true)]
    [InlineData(LauncherLanguages.SimplifiedChinese, true)]
    [InlineData(LauncherLanguages.TraditionalChinese, true)]
    [InlineData(LauncherLanguages.Japanese, true)]
    public void ResourceSegments_WhenKeyboardFocusMoves_KeepGeometryAcrossTextScales(string language, bool dark)
    {
        using var theme = ThemeVariantSnapshot.Capture(dark ? ThemeVariant.Dark : ThemeVariant.Light);
        using var context = CreateContext();
        PrepareGoldenWindow(context, dark ? ThemeVariant.Dark : ThemeVariant.Light);
        context.ViewModel.Shell.ApplyLanguage(language, hasSnapshot: false);
        context.Window.Show();
        var actions = ShowResourcePanel(context);
        // 设置页也常驻在窗口里，分段按钮查询必须限定在资源面板子树内。
        var resourcePanel = Assert.Single(context.Window.GetVisualDescendants().OfType<ResourcePanelOverlay>());
        var segments = resourcePanel.GetVisualDescendants().OfType<RadioButton>().Where(item => item.Classes.Contains("segment-option")).ToArray();
        Assert.Equal(2, segments.Length);
        var group = Assert.Single(resourcePanel.GetVisualDescendants().OfType<Border>(), border => border.Classes.Contains("segmented"));
        foreach (var scale in new[] { 1.0, 1.5, 2.0 })
        {
            actions[0].Focus(NavigationMethod.Unspecified);
            using var typography = new TypographyScaleSnapshot(context.Window, scale);
            Dispatcher.UIThread.RunJobs();
            var bounds = segments.Select(segment => segment.Bounds).ToArray();
            var groupBounds = group.Bounds;
            var values = segments.Select(segment => segment.IsChecked).ToArray();
            foreach (var segment in segments.Concat(segments.Reverse()))
            {
                Assert.True(segment.Focus(NavigationMethod.Tab));
                Dispatcher.UIThread.RunJobs();
                Assert.Contains(":focus-visible", segment.Classes);
                Assert.Equal(bounds, segments.Select(item => item.Bounds).ToArray());
                Assert.Equal(groupBounds, group.Bounds);
                Assert.Equal(values, segments.Select(item => item.IsChecked).ToArray());
                var focus = Assert.Single(segment.GetVisualDescendants().OfType<Border>(), border => border.Name == "SegmentFocus");
                Assert.Equal(255, PaintColor(focus.BorderBrush).A);
                Assert.Equal(new Thickness(2), focus.BorderThickness);
                Assert.Equal(new CornerRadius(999), focus.CornerRadius);
                Assert.False(focus.IsHitTestVisible);
                Assert.All(segments.Where(item => item != segment), other =>
                    Assert.Equal(0, PaintColor(Assert.Single(other.GetVisualDescendants().OfType<Border>(), border => border.Name == "SegmentFocus").BorderBrush).A));
            }
        }
    }

    private static void AssertSelectedPaint(ContentPresenter painted, Border focus)
    {
        Assert.True(ColorUtils.GetContrastRatio(PaintColor(painted.Foreground), PaintColor(painted.Background)) >= 4.5);
        var focusColor = PaintColor(focus.BorderBrush);
        Assert.Equal(255, focusColor.A);
        Assert.True(ColorUtils.GetContrastRatio(focusColor, PaintColor(painted.Background)) >= 3);
        Assert.Equal(new Thickness(2), focus.Margin);
        Assert.Equal(new Thickness(2), focus.BorderThickness);
        Assert.False(focus.IsHitTestVisible);
    }

    private static Color PaintColor(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;
}
