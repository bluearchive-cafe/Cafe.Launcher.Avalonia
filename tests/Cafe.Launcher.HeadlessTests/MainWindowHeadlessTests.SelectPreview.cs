using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.UI.Constants;
using Cafe.Launcher.UI.Controls;
using Cafe.Launcher.UI.Helpers;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.UI.Views;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace Cafe.Launcher.HeadlessTests;

public sealed partial class MainWindowHeadlessTests
{
    /// <summary>使用生产设置样式验证真实设置行；可单独导出供视觉复核。
    /// ADR-070 第二批起下拉回归 FA 原生模板：部件名与状态断言按原生模板收口
    /// （Background/Pill/LayoutRoot/DescriptionText），实心档经资源键覆盖提供。</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectPreview_RealSettingsRows_PreserveGeometrySelectionAndColorDots(bool dark)
    {
        using var variant = ThemeVariantSnapshot.Capture(dark ? ThemeVariant.Dark : ThemeVariant.Light);
        using var context = CreateContext();
        PrepareGoldenWindow(context, dark ? ThemeVariant.Dark : ThemeVariant.Light);
        context.ViewModel.Shell.ApplyLanguage(LauncherLanguages.SimplifiedChinese, hasSnapshot: false);
        OpenSettings(context);
        context.ViewModel.Settings.SelectedCategory = SettingsCategoryCodes.Appearance;
        context.ViewModel.Settings.Editor.Current.ThemeMode = dark ? ThemeModes.Dark : ThemeModes.Light;
        context.Provider.GetRequiredService<ThemeApplier>().ApplyScheme(Color.Parse("#FF2E7DF6"), isDark: dark);
        Dispatcher.UIThread.RunJobs();

        var section = context.Window.GetVisualDescendants().OfType<SettingsAppearanceSection>().Single();
        var motionRow = section.GetVisualDescendants().OfType<SettingSelect>()
            .Single(row => row.Title == context.ViewModel.Shell.I18n[LocalizationKeys.MotionMode]);
        var sourceRow = section.GetVisualDescendants().OfType<SettingRow>()
            .Single(row => row.Title == context.ViewModel.Shell.I18n[LocalizationKeys.ThemeColor]);
        var referenceRow = section.GetVisualDescendants().OfType<SettingRow>()
            .Single(row => row.Title == context.ViewModel.Shell.I18n[LocalizationKeys.Theme]);
        var motion = motionRow.GetVisualDescendants().OfType<ComboBox>().Single();
        var source = Assert.IsType<ComboBox>(sourceRow.Action);
        var motionValue = motion.SelectedValue;
        var sourceValue = source.SelectedValue;
        Assert.NotNull(motion.SelectedItem);
        Assert.NotNull(source.SelectedItem);

        // Move the production controls themselves, retaining bindings and ItemTemplate.
        // Pin the inherited data context before detaching, so two-way bindings never see a null draft.
        foreach (var row in new Control[] { referenceRow, motionRow, sourceRow })
        {
            row.DataContext = context.ViewModel;
            Assert.IsType<StackPanel>(row.Parent).Children.Remove(row);
        }

        context.Window.Hide();
        var content = new StackPanel
        {
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = "外观 · 下拉菜单 A", FontSize = 20, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = dark ? "深色主题 · 真实 Avalonia 控件" : "浅色主题 · 真实 Avalonia 控件", Classes = { "caption" } },
                referenceRow, motionRow, sourceRow
            }
        };
        var window = new Window
        {
            Width = 680, Height = 480,
            FontFamily = new FontFamily("Segoe UI"),
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light,
            Content = new UserControl { Classes = { "settings-section" }, Content = new Border { Padding = new Thickness(24), Child = content } }
        };
        window.Bind(Window.BackgroundProperty, window.GetResourceObservable("Launcher.Color.Dialog.Background"));
        window.Bind(Window.ForegroundProperty, window.GetResourceObservable("Launcher.Text.Primary"));
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(motionValue, motion.SelectedValue);
            Assert.Equal(sourceValue, source.SelectedValue);
            ExportSelectPreview(window, dark, "closed");
            var sourceTemplate = source.ItemTemplate;
            foreach (var (combo, name) in new[] { (motion, "motion"), (source, "source") })
            {
                var bounds = combo.Bounds;
                var selection = PreviewPart<ContentPresenter>(combo, "ContentPresenter");
                var textBounds = selection.Bounds;
                var background = PreviewPart<Border>(combo, "Background");
                var restingFill = background.Background;
                Assert.True(ColorUtils.GetContrastRatio(PreviewColor(combo.BorderBrush), PreviewColor(restingFill)) >= 3);
                window.MouseMove(combo.TranslatePoint(new Point(10, 10), window)!.Value);
                Dispatcher.UIThread.RunJobs();
                // 悬停不换底：状态由描边与箭头表达（ADR-070 资源键覆盖）；同色不同实例不算换底。
                Assert.Equal(PreviewColor(restingFill), PreviewColor(background.Background));
                Assert.Equal(textBounds, selection.Bounds);
                combo.Focus(NavigationMethod.Tab);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(1, PreviewPart<Border>(combo, "Pill").Opacity);
                Assert.Equal(textBounds, selection.Bounds);
                var popup = PreviewPart<Popup>(combo, "PART_Popup");
                popup.ShouldUseOverlayLayer = true;
                combo.IsDropDownOpen = true;
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(PreviewColor(restingFill), PreviewColor(background.Background));
                Assert.Equal(bounds, combo.Bounds);
                Assert.Equal(textBounds, selection.Bounds);
                // 焦点此刻合法地移入弹出列表：组合框本体指示让位给选中项的 Pill（下断言覆盖）。
                var items = popup.Child!.GetVisualDescendants().OfType<ComboBoxItem>().ToArray();
                Assert.Equal(combo.ItemCount, items.Length);
                foreach (var item in items)
                {
                    // 选中指示由原生 Pill 承担：非颜色线索保留。
                    Assert.Equal(item.IsSelected ? 1 : 0, PreviewPart<Rectangle>(item, "Pill").Opacity);
                    Assert.True(item.Bounds.Height >= 32, $"item hit height {item.Bounds.Height}");
                }

                if (ReferenceEquals(combo, source))
                {
                    Assert.Same(sourceTemplate, combo.ItemTemplate);
                    Assert.All(items, item => Assert.Single(item.GetVisualDescendants().OfType<Border>(), border => border.Classes.Contains("theme-color-dot")));
                }

                window.MouseMove(new Point(16, 450));
                ExportSelectPreview(window, dark, name);
                var selected = Assert.IsType<ComboBoxItem>(combo.ContainerFromIndex(combo.SelectedIndex));
                var selectedFill = PreviewComposite(
                    PreviewPart<Border>(selected, "LayoutRoot").Background,
                    PreviewColor(restingFill));
                Assert.True(ColorUtils.GetContrastRatio(
                    PreviewColor(PreviewPart<ContentPresenter>(selected, "ContentPresenter").Foreground), selectedFill) >= 4.5);
                items[(combo.SelectedIndex + 1) % items.Length].Focus(NavigationMethod.Unspecified);
                selected.Focus(NavigationMethod.Tab);
                Dispatcher.UIThread.RunJobs();
                Assert.True(selected.IsKeyboardFocusWithin);
                Assert.Equal(1, PreviewPart<Rectangle>(selected, "Pill").Opacity);
                ExportSelectPreview(window, dark, name + "-focus");
                window.MouseMove(selected.TranslatePoint(new Point(20, 20), window)!.Value);
                Dispatcher.UIThread.RunJobs();
                var hoveredFill = PreviewComposite(PreviewPart<Border>(selected, "LayoutRoot").Background,
                    PreviewColor(restingFill));
                Assert.True(ColorUtils.GetContrastRatio(
                    PreviewColor(PreviewPart<ContentPresenter>(selected, "ContentPresenter").Foreground), hoveredFill) >= 4.5);
                Assert.Equal(bounds, combo.Bounds);
                ExportSelectPreview(window, dark, name + "-hover");
                window.MouseMove(new Point(16, 450));
                var previous = combo.SelectedValue;
                window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, string.Empty);
                window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, string.Empty);
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, string.Empty);
                window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, string.Empty);
                Dispatcher.UIThread.RunJobs();
                Assert.False(combo.IsDropDownOpen);
                Assert.Equal(previous, combo.SelectedValue);

                // Commit through the native keyboard path, then check the original two-way settings binding.
                combo.IsDropDownOpen = true;
                Dispatcher.UIThread.RunJobs();
                Assert.IsType<ComboBoxItem>(combo.ContainerFromIndex(combo.SelectedIndex)).Focus(NavigationMethod.Tab);
                window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, string.Empty);
                window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, string.Empty);
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, string.Empty);
                window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, string.Empty);
                Dispatcher.UIThread.RunJobs();
                Assert.NotEqual(previous, combo.SelectedValue);
                Assert.Equal(combo.SelectedValue, ReferenceEquals(combo, motion)
                    ? context.ViewModel.Settings.Editor.Current.MotionMode
                    : context.ViewModel.Settings.Editor.Current.ThemeColorMode);
                combo.SetCurrentValue(SelectingItemsControl.SelectedValueProperty, previous);
                combo.IsEnabled = false;
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(bounds, combo.Bounds);
                Assert.Equal(0, PreviewPart<Border>(combo, "Pill").Opacity);
                combo.IsEnabled = true;
                Dispatcher.UIThread.RunJobs();
                ((IPseudoClasses)combo.Classes).Set(":error", true);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(
                    Assert.IsAssignableFrom<ISolidColorBrush>(window.FindResource(window.ActualThemeVariant, "Launcher.Color.Error")).Color,
                    Assert.IsAssignableFrom<ISolidColorBrush>(background.BorderBrush).Color);
                Assert.Equal(bounds, combo.Bounds);
                ((IPseudoClasses)combo.Classes).Set(":error", false);
            }
        }
        finally
        {
            motion.IsDropDownOpen = false;
            source.IsDropDownOpen = false;
            window.Close();
        }
    }

    private static T PreviewPart<T>(Control control, string name) where T : Control =>
        Assert.Single(control.GetVisualDescendants().OfType<T>(), part => part.Name == name);

    private static Color PreviewColor(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    private static Color PreviewComposite(IBrush? brush, Color background)
    {
        var solid = Assert.IsAssignableFrom<ISolidColorBrush>(brush);
        var alpha = solid.Color.A / 255d * solid.Opacity;
        byte Mix(byte foreground, byte underneath) => (byte)Math.Round(foreground * alpha + underneath * (1 - alpha));
        return Color.FromRgb(Mix(solid.Color.R, background.R), Mix(solid.Color.G, background.G), Mix(solid.Color.B, background.B));
    }

    private static void ExportSelectPreview(Window window, bool dark, string state)
    {
        if (Environment.GetEnvironmentVariable("CAFE_SELECT_PREVIEW_EXPORT") != "1")
        {
            return;
        }

        Dispatcher.UIThread.RunJobs();
        var directory = System.IO.Path.Combine(GoldenScreenshot.FindRepositoryRoot(), ".artifacts", "settings-select-md3");
        Directory.CreateDirectory(directory);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height));
        bitmap.Render(window);
        using var stream = File.Create(System.IO.Path.Combine(directory, $"{(dark ? "dark" : "light")}-{state}.png"));
        bitmap.Save(stream, new PngBitmapEncoderOptions());
    }
}
