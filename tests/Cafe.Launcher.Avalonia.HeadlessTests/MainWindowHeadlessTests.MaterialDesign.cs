using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
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

public sealed partial class MainWindowHeadlessTests
{
    [AvaloniaTheory]
    [InlineData(1300, 754, LauncherLanguages.English)]
    [InlineData(1024, 640, LauncherLanguages.English)]
    [InlineData(1024, 640, LauncherLanguages.SimplifiedChinese)]
    [InlineData(1024, 640, LauncherLanguages.Japanese)]
    public void SetupWizard_LongChoiceDescriptions_WrapWithinTheirRows(int width, int height, string language)
    {
        using var context = CreateContext();
        context.ViewModel.IsMotionReduced = true;
        context.Window.Width = width;
        context.Window.Height = height;
        context.Window.Show();
        context.ViewModel.Dialogs.ShowSetupWizard();
        context.ViewModel.Shell.ApplyLanguage(language, context.ViewModel.Settings, context.ViewModel.ResourcePanel, false);
        foreach (int step in new[] { 2, 3 })
        {
            context.ViewModel.Dialogs.SetupWizard.Step = step;
            Dispatcher.UIThread.RunJobs();
            var rows = context.Window.GetVisualDescendants().OfType<RadioButton>()
                .Where(control => control.Classes.Contains("wizard-option") && control.IsEffectivelyVisible).ToArray();
            Assert.NotEmpty(rows);
            foreach (var row in rows)
            {
                foreach (var text in row.GetVisualDescendants().OfType<TextBlock>().Where(control => control.Classes.Contains("caption")))
                {
                    Assert.Equal(TextWrapping.Wrap, text.TextWrapping);
                    Assert.True(text.TextLayout.Width <= text.Bounds.Width + 1);
                    Assert.True(text.TextLayout.Height <= text.Bounds.Height + 1);
                    var position = text.TranslatePoint(default, row)!.Value;
                    Assert.True(position.X + text.Bounds.Width <= row.Bounds.Width + 1);
                    Assert.True(position.Y + text.Bounds.Height <= row.Bounds.Height + 1);
                }
            }
        }
    }

    [AvaloniaFact]
    public void DebugPanel_LongDataDirectory_StaysWithinCardAndProvidesFullPath()
    {
        using var context = CreateContext();
        context.Window.Show();
        context.ViewModel.Debug.IsVisible = true;
        Dispatcher.UIThread.RunJobs();
        var text = context.Window.GetVisualDescendants().OfType<TextBlock>()
            .Single(control => control.Text == context.ViewModel.Debug.DataDirectoryPath);
        var card = text.GetVisualAncestors().OfType<Border>().First(control => control.Classes.Contains("dialog-card"));
        Assert.True(text.TranslatePoint(default, card)!.Value.X + text.Bounds.Width <= card.Bounds.Width - card.Padding.Right + 1);
        Assert.Equal(context.ViewModel.Debug.DataDirectoryPath, ToolTip.GetTip(text));
    }

    [AvaloniaFact]
    public void SetupWizard_CheckedOptionGlyph_UsesLauncherPrimary()
    {
        using var context = CreateContext();
        context.Window.Show();
        context.ViewModel.Dialogs.ShowSetupWizard();
        context.ViewModel.Dialogs.SetupWizard.Step = 2;
        Dispatcher.UIThread.RunJobs();
        var radio = context.Window.GetVisualDescendants().OfType<RadioButton>()
            .First(control => control.Classes.Contains("wizard-option"));
        radio.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        var glyph = Assert.IsType<Ellipse>(radio.GetVisualDescendants()
            .First(control => control is Ellipse && control.Name == "CheckGlyph"));
        var expected = Assert.IsType<SolidColorBrush>(Application.Current!.FindResource("Launcher.Color.Primary"));
        Assert.Equal(expected.Color, Assert.IsType<SolidColorBrush>(glyph.Fill).Color);
    }

    [AvaloniaFact]
    public void SetupWizard_UnselectedOption_FollowsM3RadioGlyphTokens()
    {
        using var context = CreateContext();
        context.Window.Show();
        context.ViewModel.Dialogs.ShowSetupWizard();
        context.ViewModel.Dialogs.SetupWizard.Step = 2;
        Dispatcher.UIThread.RunJobs();
        var radio = context.Window.GetVisualDescendants().OfType<RadioButton>()
            .First(control => control.Classes.Contains("wizard-option") && control.IsChecked != true);
        var ring = Assert.IsType<Ellipse>(radio.GetVisualDescendants()
            .First(control => control is Ellipse && control.Name == "OuterEllipse"));
        var application = Application.Current!;
        Assert.True(
            application.TryGetResource("Launcher.Text.Secondary", application.ActualThemeVariant, out var ringResource),
            "Launcher.Text.Secondary resource missing.");
        var expectedRing = Assert.IsType<SolidColorBrush>(ringResource);

        // M3 radio：未选中环 onSurfaceVariant、2px 描边（内点 10dp 仅选中态可见，
        // 尺寸断言见 SelectedOption 用例）。
        Assert.Equal(expectedRing.Color, Assert.IsType<SolidColorBrush>(ring.Stroke).Color);
        Assert.Equal(2, ring.StrokeThickness);
    }

    [AvaloniaFact]
    public void SetupWizard_SelectedOption_UsesRingAndDotWithoutAccentDisc()
    {
        using var context = CreateContext();
        context.Window.Show();
        context.ViewModel.Dialogs.ShowSetupWizard();
        context.ViewModel.Dialogs.SetupWizard.Step = 2;
        Dispatcher.UIThread.RunJobs();
        var radio = context.Window.GetVisualDescendants().OfType<RadioButton>()
            .First(control => control.Classes.Contains("wizard-option"));
        radio.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        var ring = Assert.IsType<Ellipse>(radio.GetVisualDescendants()
            .First(control => control is Ellipse && control.Name == "CheckOuterEllipse"));
        var glyph = Assert.IsType<Ellipse>(radio.GetVisualDescendants()
            .First(control => control is Ellipse && control.Name == "CheckGlyph"));
        var application = Application.Current!;
        var expected = Assert.IsType<SolidColorBrush>(application.FindResource("Launcher.Color.Primary"));
        Assert.True(
            application.TryGetResource("Launcher.Color.Transparent", application.ActualThemeVariant, out var transparentResource),
            "Launcher.Color.Transparent resource missing.");
        var expectedTransparent = Assert.IsType<SolidColorBrush>(transparentResource).Color;

        // Fluent 选中盘默认整面填充系统 Accent；M3 为 2px 环 + 10dp 点，盘须透明。
        Assert.Equal(expected.Color, Assert.IsType<SolidColorBrush>(ring.Stroke).Color);
        Assert.Equal(expectedTransparent, Assert.IsType<SolidColorBrush>(ring.Fill).Color);
        Assert.Equal(10, glyph.Width);
        Assert.Equal(10, glyph.Height);
        Assert.Equal(expected.Color, Assert.IsType<SolidColorBrush>(glyph.Fill).Color);
    }

    [AvaloniaFact]
    public void SetupWizard_OptionRowHover_PaintsRowStateLayerOnTemplateRoot()
    {
        using var context = CreateContext();
        context.Window.Show();
        context.ViewModel.Dialogs.ShowSetupWizard();
        context.ViewModel.Dialogs.SetupWizard.Step = 2;
        Dispatcher.UIThread.RunJobs();
        var radio = context.Window.GetVisualDescendants().OfType<RadioButton>()
            .First(control => control.Classes.Contains("wizard-option"));
        var root = Assert.IsType<Border>(radio.GetVisualDescendants()
            .First(control => control is Border && control.Name == "RootBorder"));
        var center = radio.TranslatePoint(
            new Point(radio.Bounds.Width / 2, radio.Bounds.Height / 2),
            context.Window)!.Value;
        context.Window.MouseMove(center);
        Dispatcher.UIThread.RunJobs();
        Assert.True(radio.IsPointerOver, "wizard option row did not register pointer hover.");
        var application = Application.Current!;
        Assert.True(
            application.TryGetResource("Launcher.Color.Content.Row.Hover", application.ActualThemeVariant, out var hoverResource),
            "Launcher.Color.Content.Row.Hover resource missing.");
        var expected = Assert.IsType<SolidColorBrush>(hoverResource);

        // Fluent 模板 hover 会以透明 Setter 覆盖 TemplateBinding；行级状态层
        // 须由显式 RootBorder 样式接管后上屏。
        Assert.Equal(expected.Color, Assert.IsType<SolidColorBrush>(root.Background).Color);
    }

    [AvaloniaFact]
    public void SetupWizard_OptionRowPressed_PaintsPressedStateLayerOnTemplateRoot()
    {
        using var context = CreateContext();
        context.Window.Show();
        context.ViewModel.Dialogs.ShowSetupWizard();
        context.ViewModel.Dialogs.SetupWizard.Step = 2;
        Dispatcher.UIThread.RunJobs();
        var radio = context.Window.GetVisualDescendants().OfType<RadioButton>()
            .First(control => control.Classes.Contains("wizard-option"));
        var root = Assert.IsType<Border>(radio.GetVisualDescendants()
            .First(control => control is Border && control.Name == "RootBorder"));
        var center = radio.TranslatePoint(
            new Point(radio.Bounds.Width / 2, radio.Bounds.Height / 2),
            context.Window)!.Value;
        context.Window.MouseMove(center);
        context.Window.MouseDown(center, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.True(radio.IsPressed, "wizard option row did not register pointer press.");
        var application = Application.Current!;
        Assert.True(
            application.TryGetResource(
                "Launcher.Color.StateLayer.OnSurface.Pressed", application.ActualThemeVariant, out var pressedResource),
            "Launcher.Color.StateLayer.OnSurface.Pressed resource missing.");
        var expected = Assert.IsType<SolidColorBrush>(pressedResource);

        // 按压态强于悬停（M3 pressed 12% onSurface）：RootBorder 须切换到
        // pressed 状态层，不得回落到与 hover 相同的行悬停底色。
        Assert.Equal(expected.Color, Assert.IsType<SolidColorBrush>(root.Background).Color);
    }

    [AvaloniaFact]
    public void SetupWizard_OptionRowIcon_VerticallyCentersWithContent()
    {
        using var context = CreateContext();
        context.Window.Show();
        context.ViewModel.Dialogs.ShowSetupWizard();
        context.ViewModel.Dialogs.SetupWizard.Step = 2;
        Dispatcher.UIThread.RunJobs();
        var radio = context.Window.GetVisualDescendants().OfType<RadioButton>()
            .First(control => control.Classes.Contains("wizard-option") && control.IsChecked != true);
        var ring = Assert.IsType<Ellipse>(radio.GetVisualDescendants()
            .First(control => control is Ellipse && control.Name == "OuterEllipse"));
        var content = Assert.IsType<ContentPresenter>(radio.GetVisualDescendants()
            .First(control => control is ContentPresenter && control.Name == "PART_ContentPresenter"));

        // Fluent 模板把图标栅格钉在行顶（Height=32、Top），多行内容时图标偏高；
        // 图标中心须与右侧内容块中心对齐（M3 列表行规范）。
        var iconCenter = ring.TranslatePoint(new Point(ring.Bounds.Width / 2, ring.Bounds.Height / 2), radio)!.Value;
        var contentCenter = content.TranslatePoint(new Point(content.Bounds.Width / 2, content.Bounds.Height / 2), radio)!.Value;
        Assert.True(
            Math.Abs(iconCenter.Y - contentCenter.Y) <= 0.5,
            $"icon center Y {iconCenter.Y:F1} vs content center Y {contentCenter.Y:F1}.");
    }

    [AvaloniaFact]
    public void ResourcePanel_CheckedRowCheckBox_UsesLauncherPrimary()
    {
        using var context = CreateContext();
        context.Window.Show();
        ShowResourcePanel(context);
        Dispatcher.UIThread.RunJobs();
        var checkBox = context.Window.GetVisualDescendants().OfType<CheckBox>()
            .First(control => control.IsEffectivelyVisible);
        checkBox.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        var rectangle = Assert.IsType<Border>(checkBox.GetVisualDescendants()
            .First(control => control is Border && control.Name == "NormalRectangle"));
        var expected = Assert.IsType<SolidColorBrush>(Application.Current!.FindResource("Launcher.Color.Primary"));
        Assert.Equal(expected.Color, Assert.IsType<SolidColorBrush>(rectangle.Background).Color);
    }

    [AvaloniaFact]
    public void DialogCloseButton_WhenKeyboardFocused_ShowsRoundedFocusRingAdorner()
    {
        using var context = CreateContext();
        OpenSettings(context);
        var navigation = GetSettingsNavigation(context.Window);
        context.Window.Activate();
        navigation.Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();
        context.Window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, "");
        context.Window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, "");
        Dispatcher.UIThread.RunJobs();
        var closeButton = context.Window.GetVisualDescendants()
            .OfType<Button>()
            .Single(control => control.Classes.Contains("content-header-action"));
        Assert.True(closeButton.IsKeyboardFocusWithin);

        var adornerLayer = Assert.IsAssignableFrom<AdornerLayer>(context.Window.GetVisualDescendants()
            .First(control => control is AdornerLayer));
        var adorner = Assert.IsType<Border>(Assert.Single(adornerLayer.GetVisualChildren()));
        var application = Application.Current!;
        var expectedRing = Assert.IsType<SolidColorBrush>(application.FindResource("Launcher.Color.FocusRing"));
        Assert.Equal(expectedRing.Color, Assert.IsType<SolidColorBrush>(adorner.BorderBrush).Color);
        Assert.Equal(
            (CornerRadius)application.FindResource("Launcher.Radius.Sm")!,
            adorner.CornerRadius);
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

    [AvaloniaFact]
    public void LogViewer_FilterTabs_ApplyChipStylesToRadioButtons()
    {
        using var context = CreateContext();
        context.Window.Show();
        ShowLogViewer(context);

        var tabs = context.Window.GetVisualDescendants().OfType<RadioButton>()
            .Where(control => control.Classes.Contains("filter-tab") && control.IsEffectivelyVisible)
            .ToArray();
        Assert.Equal(7, tabs.Length);
        Assert.Equal(1, tabs.Count(tab => tab.IsChecked == true));
        var application = Application.Current!;
        var settingHeight = Assert.IsType<double>(
            application.FindResource("Launcher.Control.Height.Setting"));
        Assert.True(
            application.TryGetResource("Launcher.Color.OnPrimary", application.ActualThemeVariant, out var onPrimaryResource),
            "Launcher.Color.OnPrimary resource missing.");
        var expectedOnPrimary = Assert.IsType<SolidColorBrush>(onPrimaryResource).Color;
        Assert.True(
            application.TryGetResource("Launcher.Text.Primary", application.ActualThemeVariant, out var textPrimaryResource),
            "Launcher.Text.Primary resource missing.");
        var expectedTextPrimary = Assert.IsType<SolidColorBrush>(textPrimaryResource).Color;

        foreach (var tab in tabs)
        {
            // 复核回归探针：RadioButton.filter-tab* 样式必须命中实际控件
            // （Avalonia 类型选择器不匹配派生类型，Button.* 不会命中 RadioButton）。
            Assert.Equal(settingHeight, tab.Height);
            Assert.Equal(settingHeight, tab.Bounds.Height);
            Assert.Empty(tab.GetVisualDescendants().OfType<Ellipse>());
            var foreground = Assert.IsType<SolidColorBrush>(tab.Foreground);
            Assert.Equal(
                tab.IsChecked == true ? expectedOnPrimary : expectedTextPrimary,
                foreground.Color);
        }
    }

    [AvaloniaFact]
    public async Task AppearanceSwatch_AppliesFullHitAreaStylesToRadioButton()
    {
        using var context = CreateContext();
        OpenSettings(context);
        context.ViewModel.Settings.SelectedCategory = SettingsCategoryCodes.Appearance;
        // 走真实开关路径：ThemeColorMode 变更经 OnCurrentSettingChanged 同步可见性；
        // 先排空壁纸取色任务，避免迟到的提取清空手动补种的色板项。
        var appearance = context.ViewModel.Settings.Appearance;
        context.ViewModel.Settings.Editor.Current.ThemeColorMode = ThemeColorModes.Wallpaper;
        await appearance.PendingThemeRefresh;
        Dispatcher.UIThread.RunJobs();
        if (appearance.ThemeColorPaletteItems.Count == 0)
        {
            appearance.ThemeColorPaletteItems.Add(new ThemeColorPaletteItem
            {
                Index = 0,
                ColorHex = "#FFD82038",
                Brush = new SolidColorBrush(Color.FromRgb(0xD8, 0x20, 0x38)),
                IsSelected = true
            });
        }

        Dispatcher.UIThread.RunJobs();
        Assert.True(
            appearance.IsThemeColorPaletteVisible,
            $"paletteVisible=false, wallpaperFlag={appearance.IsWallpaperThemeColorSelected}"
            + $", mode={context.ViewModel.Settings.Editor.Current.ThemeColorMode}");

        var swatches = context.Window.GetVisualDescendants().OfType<RadioButton>()
            .Where(control => control.Classes.Contains("color-swatch-button") && control.IsEffectivelyVisible)
            .ToArray();
        Assert.NotEmpty(swatches);
        var application = Application.Current!;
        var settingSize = Assert.IsType<double>(
            application.FindResource("Launcher.Control.Height.Setting"));

        foreach (var swatch in swatches)
        {
            // 复核回归探针：色板命中区必须恢复完整 36×36，且模板替换 Fluent 圆点视觉。
            Assert.Equal(settingSize, swatch.Width);
            Assert.Equal(settingSize, swatch.Height);
            Assert.Equal(settingSize, swatch.Bounds.Width);
            Assert.Equal(settingSize, swatch.Bounds.Height);
            Assert.Empty(swatch.GetVisualDescendants().OfType<Ellipse>());
        }
    }
}
