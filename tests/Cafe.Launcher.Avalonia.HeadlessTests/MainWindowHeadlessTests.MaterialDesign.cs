using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cafe.Launcher.Avalonia.Features.Settings;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.HeadlessTests;

// M3 color-role contract in the rendered tree: the error text drawn on a dialog
// surface must stay readable in both themes and under both neutral strategies,
// and the token focus ring must land on a CheckBox's visible box rather than on
// its empty-label layout box.
public sealed partial class MainWindowHeadlessTests
{
    [AvaloniaFact]
    public void FocusedCheckBox_DrawsTokenRingOnGlyphInsteadOfLayoutBox()
    {
        using var context = CreateContext();
        context.Window.Show();
        context.ViewModel.ResourcePanel.IsResourcePanelVisible = true;
        Dispatcher.UIThread.RunJobs();

        // 面板的条目会异步重载，故在取到目标控件后再把它的条目置为可用（否则禁用态不可聚焦）。
        var checkBox = context.Window.GetVisualDescendants().OfType<CheckBox>()
            .First(control => control.DataContext is ResourcePanelItem);
        Assert.IsType<ResourcePanelItem>(checkBox.DataContext).Status = ResourcePanelItemStatus.Ready;
        Dispatcher.UIThread.RunJobs();

        checkBox.Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();
        Assert.True(checkBox.IsFocused, "The enable-row checkbox must be focusable for this contract.");

        // `:empty` 分支：焦点环铺在字形上——Fluent 的布局盒因 MinHeight 32 与空标签预留的
        // 8px 间距比字形大（28×32 vs 20×20），默认装饰器铺满布局盒时会相对勾选框偏右下。
        var glyph = checkBox.GetVisualDescendants().OfType<Border>()
            .Single(border => border.Name == "NormalRectangle");
        Assert.True(
            glyph.Bounds.Width < checkBox.Bounds.Width || glyph.Bounds.Height < checkBox.Bounds.Height,
            $"Template assumption changed: glyph {glyph.Bounds} vs control {checkBox.Bounds}.");
        var expected = Assert.IsType<SolidColorBrush>(
            Application.Current!.FindResource("Launcher.Color.FocusRing"));
        Assert.Equal(expected.Color, Assert.IsType<SolidColorBrush>(glyph.BorderBrush).Color);
        Assert.Equal(new Thickness(2), glyph.BorderThickness);

        // 且不得再有覆盖整个布局盒的装饰器——那正是本契约要挡住的形态。
        var oversized = AdornerLayer.GetAdornerLayer(checkBox)?.GetVisualChildren()
            .Where(child => child.Bounds.Width >= checkBox.Bounds.Width
                && child.Bounds.Height >= checkBox.Bounds.Height)
            .ToList() ?? [];
        Assert.Empty(oversized);
    }

    [AvaloniaFact]
    public void FocusedCheckBoxWithNativeLabel_DrawsTokenRingOnControlBox()
    {
        // 当前没有任何带原生标签的 CheckBox，故运行时填上内容槽来覆盖另一半分支：
        // `:empty` 消失后焦点环应改铺控件盒（此时盒宽恰为字形+间距+标签），不再铺字形。
        using var context = CreateContext();
        context.Window.Show();
        context.ViewModel.ResourcePanel.IsResourcePanelVisible = true;
        Dispatcher.UIThread.RunJobs();

        var checkBox = context.Window.GetVisualDescendants().OfType<CheckBox>()
            .First(control => control.DataContext is ResourcePanelItem);
        Assert.IsType<ResourcePanelItem>(checkBox.DataContext).Status = ResourcePanelItemStatus.Ready;
        checkBox.Content = "native label";
        Dispatcher.UIThread.RunJobs();

        checkBox.Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();
        Assert.True(checkBox.IsFocused, "A labelled checkbox must be focusable for this contract.");

        var expected = Assert.IsType<SolidColorBrush>(
            Application.Current!.FindResource("Launcher.Color.FocusRing"));
        var box = checkBox.GetVisualDescendants().OfType<Border>()
            .Single(border => border.Name == "PART_Border");
        Assert.Equal(expected.Color, Assert.IsType<SolidColorBrush>(box.BorderBrush).Color);
        Assert.Equal(new Thickness(2), box.BorderThickness);

        var glyph = checkBox.GetVisualDescendants().OfType<Border>()
            .Single(border => border.Name == "NormalRectangle");
        Assert.NotEqual(expected.Color, Assert.IsType<SolidColorBrush>(glyph.BorderBrush).Color);
        Assert.True(
            box.Bounds.Width > glyph.Bounds.Width,
            $"The label must widen the control box: box {box.Bounds} vs glyph {glyph.Bounds}.");
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SetupWizard_EmptyPathInEitherTheme_KeepsErrorTextReadable(bool isDark, bool seedFollowing)
    {
        using var context = CreateContext();
        var application = Application.Current!;
        var previousTheme = application.RequestedThemeVariant;
        try
        {
            context.ViewModel.IsMotionReduced = true;
            context.Window.Show();
            context.ViewModel.Dialogs.ShowSetupWizard();
            context.ViewModel.Dialogs.SetupWizard.Step = 1;
            context.ViewModel.Dialogs.SetupWizard.GamePath = "";
            Dispatcher.UIThread.RunJobs();
            context.Window.RequestedThemeVariant = isDark ? ThemeVariant.Dark : ThemeVariant.Light;
            application.RequestedThemeVariant = context.Window.RequestedThemeVariant;
            SettingsAppearanceViewModel.ApplyScheme(
                Color.Parse("#6750A4"),
                isDark: isDark,
                neutralStrategy: seedFollowing ? NeutralColorStrategies.SeedFollowing : NeutralColorStrategies.BrandBlue);
            Dispatcher.UIThread.RunJobs();

            var error = context.Window.GetVisualDescendants().OfType<TextBlock>()
                .Single(control => control.IsEffectivelyVisible
                    && control.Text == context.ViewModel.Shell.I18n["setupWizardGamePathEmpty"]);
            var surface = error.GetVisualAncestors().OfType<global::Cafe.Launcher.Avalonia.Controls.DialogSurface>().First();
            var foreground = Assert.IsType<SolidColorBrush>(error.Foreground).Color;
            var background = Assert.IsType<SolidColorBrush>(surface.Background).Color;
            double contrast = ColorUtils.GetContrastRatio(foreground, background);
            Assert.True(contrast >= 4.5, $"Error text contrast is {contrast:F2}:1.");
        }
        finally
        {
            application.RequestedThemeVariant = previousTheme;
            SettingsAppearanceViewModel.ApplyScheme(Color.Parse("#FF2E7DF6"));
        }
    }
}
