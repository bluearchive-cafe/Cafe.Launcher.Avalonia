using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.UI.Services;
using Material.Icons;
using Material.Icons.Avalonia;

namespace Cafe.Launcher.HeadlessTests;

/// <summary>验证原生选择与菜单模板的实际绘制状态和键盘行为，避免只覆盖控件属性。</summary>
public sealed class SelectionMenuThemeHeadlessTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChoiceField_PointerFocusAndDisabledStates_KeepGeometryAndSharedPaint(bool dark)
    {
        var combo = CreateChoice();
        using var host = CreateHost(combo, dark);
        var surface = Part<Border>(combo, "Background");
        var content = Part<ContentControl>(combo, "ContentPresenter");
        var glyph = Part<PathIcon>(combo, "DropDownGlyph");
        var chevron = Assert.Single(glyph.GetVisualDescendants().OfType<MaterialIcon>());
        Assert.Equal(MaterialIconKind.ChevronDown, chevron.Kind);
        Assert.Equal(new Size(16, 16), chevron.Bounds.Size);
        Assert.False(chevron.IsHitTestVisible);
        var bounds = combo.Bounds;
        var textBounds = content.Bounds;
        var point = combo.TranslatePoint(new Point(10, 10), host.Window)!.Value;

        host.Window.MouseMove(point);
        Dispatcher.UIThread.RunJobs();
        AssertPaint(host.Window, "Launcher.Color.Button.Secondary.Hover", surface.Background);
        Assert.Equal(combo.BorderBrush, surface.BorderBrush);

        combo.Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(":focus-visible", combo.Classes);
        AssertPaint(host.Window, "Launcher.Color.FocusRing", surface.BorderBrush);
        Assert.False(Part<Border>(combo, "HighlightBackground").IsVisible);
        Assert.Equal(combo.Foreground, content.Foreground);
        Assert.Equal(combo.Foreground, glyph.Foreground);
        Assert.Equal(combo.Foreground, chevron.Foreground);
        Assert.Equal(bounds, combo.Bounds);
        Assert.Equal(textBounds, content.Bounds);

        combo.IsEnabled = false;
        Dispatcher.UIThread.RunJobs();
        AssertPaint(host.Window, "Launcher.Color.Content.Row", surface.Background);
        Assert.Equal(combo.Foreground, content.Foreground);
        Assert.Equal(combo.Foreground, glyph.Foreground);
        Assert.Equal(combo.Foreground, chevron.Foreground);
        Assert.Equal(bounds, combo.Bounds);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChoicePopup_UsesSharedRowsAndSurface_AndPreservesKeyboardSelection(bool dark)
    {
        var combo = CreateChoice();
        using var host = CreateHost(combo, dark);
        var popup = OpenChoice(combo);
        var surface = Assert.IsType<Border>(popup.Child);
        AssertPopupSurface(host.Window, surface);
        var items = surface.GetVisualDescendants().OfType<ComboBoxItem>().ToArray();
        Assert.Equal(3, items.Length);
        foreach (var item in items)
        {
            Assert.Equal(40, item.Bounds.Height);
            Assert.Equal(new CornerRadius(8), item.CornerRadius);
            var painted = Part<ContentPresenter>(item, "PART_ContentPresenter");
            Assert.Equal(item.Background, painted.Background);
            Assert.Equal(item.Foreground, painted.Foreground);
        }

        AssertPaint(host.Window, "Launcher.Color.SecondaryContainer", items[0].Background);
        AssertPaint(host.Window, "Launcher.Color.OnSecondaryContainer", items[0].Foreground);
        Assert.False(items[1].IsEffectivelyEnabled);
        Assert.True(items[1].Opacity < 1);

        host.Window.MouseMove(items[2].TranslatePoint(new Point(10, 10), host.Window)!.Value);
        Dispatcher.UIThread.RunJobs();
        AssertPaint(host.Window, "Launcher.Color.Button.Secondary.Hover", Part<ContentPresenter>(items[2], "PART_ContentPresenter").Background);
        host.Window.MouseMove(items[0].TranslatePoint(new Point(10, 10), host.Window)!.Value);
        Dispatcher.UIThread.RunJobs();
        AssertPaint(host.Window, "Launcher.Color.SecondaryContainer.Hover", Part<ContentPresenter>(items[0], "PART_ContentPresenter").Background);
        host.Window.MouseMove(new Point(440, 300));
        items[0].Focus(NavigationMethod.Tab);

        // 方向键跳过禁用项，Enter 提交并关闭；Escape 不提交另一项。
        Press(host.Window, Key.Down, PhysicalKey.ArrowDown);
        Press(host.Window, Key.Enter, PhysicalKey.Enter);
        Assert.Equal(2, combo.SelectedIndex);
        Assert.False(combo.IsDropDownOpen);
        Assert.True(combo.IsFocused);
        OpenChoice(combo);
        Press(host.Window, Key.Up, PhysicalKey.ArrowUp);
        Press(host.Window, Key.Escape, PhysicalKey.Escape);
        Assert.False(combo.IsDropDownOpen);
        Assert.Equal(2, combo.SelectedIndex);
    }

    [AvaloniaFact]
    public void PlainChoicePopup_WithManyItemsAndLargeText_RemainsScrollable()
    {
        var combo = new ComboBox { Width = 280, ItemsSource = Enumerable.Range(0, 30).Select(index => $"Option {index}").ToArray(), SelectedIndex = 0, MaxDropDownHeight = 160 };
        using var host = CreateHost(combo, dark: true);
        Assert.Equal(40, combo.Bounds.Height);
        var popup = OpenChoice(combo);
        var surface = Assert.IsType<Border>(popup.Child);
        AssertPopupSurface(host.Window, surface);
        var scroller = Assert.Single(surface.GetVisualDescendants().OfType<ScrollViewer>());
        Assert.True(scroller.Extent.Height > scroller.Viewport.Height);
        Assert.True(surface.Bounds.Height <= 160);
        var first = Assert.IsType<ComboBoxItem>(combo.ContainerFromIndex(0));
        first.FontSize = 24;
        Dispatcher.UIThread.RunJobs();
        var text = Assert.Single(first.GetVisualDescendants().OfType<TextBlock>());
        Assert.True(first.Bounds.Height >= text.DesiredSize.Height + first.Padding.Top + first.Padding.Bottom + first.BorderThickness.Top + first.BorderThickness.Bottom);
        Press(host.Window, Key.End, PhysicalKey.End);
        Press(host.Window, Key.Enter, PhysicalKey.Enter);
        Assert.Equal(29, combo.SelectedIndex);
        Assert.False(combo.IsDropDownOpen);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void MenuFlyoutAndSubmenu_UseSharedPaint_AndRetainNativeContent(bool dark)
    {
        var command = new MenuItem { Header = "Open folder", InputGesture = new KeyGesture(Key.O, KeyModifiers.Control), Icon = new TextBlock { Text = "+" } };
        var disabled = new MenuItem { Header = "Unavailable", IsEnabled = false };
        var child = new MenuItem { Header = "Checked option", ToggleType = MenuItemToggleType.CheckBox, IsChecked = true };
        var parent = new MenuItem { Header = "More", Items = { child } };
        var danger = new MenuItem { Header = "Uninstall", Classes = { "danger-menu-item" } };
        var separator = new Separator();
        var menu = new MenuFlyout { Items = { command, disabled, parent, separator, danger } };
        var trigger = new Button { Content = "Manage", Flyout = menu };
        using var host = CreateHost(trigger, dark);
        menu.Popup.ShouldUseOverlayLayer = true;
        menu.ShowAt(trigger);
        Dispatcher.UIThread.RunJobs();
        try
        {
            var presenter = Assert.IsType<MenuFlyoutPresenter>(menu.Popup.Child);
            AssertPopupSurface(host.Window, Part<Border>(presenter, "LayoutRoot"));
            Assert.Equal(1, separator.Height);
            AssertPaint(host.Window, "Launcher.Color.Card.Border", separator.Background);
            foreach (var item in new[] { command, disabled, parent, danger })
            {
                Assert.Equal(40, item.Bounds.Height);
                Assert.Equal(new CornerRadius(8), item.CornerRadius);
                Assert.Equal(item.Background, Part<Border>(item, "PART_LayoutRoot").Background);
                Assert.Equal(item.Foreground, Part<ContentPresenter>(item, "PART_HeaderPresenter").Foreground);
            }

            Assert.True(Part<ContentControl>(command, "PART_IconPresenter").IsVisible);
            Assert.False(string.IsNullOrWhiteSpace(Part<TextBlock>(command, "PART_InputGestureText").Text));
            AssertPaint(host.Window, "Launcher.Color.Error", danger.Foreground);
            Assert.True(disabled.Opacity < 1);
            parent.Focus(NavigationMethod.Unspecified);
            Assert.True(command.Focus(NavigationMethod.Tab));
            command.IsSelected = true;
            Dispatcher.UIThread.RunJobs();
            AssertPaint(host.Window, "Launcher.Color.Button.Secondary.Hover", Part<Border>(command, "PART_LayoutRoot").Background);
            AssertPaint(host.Window, "Launcher.Color.FocusRing", Part<Border>(command, "PART_LayoutRoot").BorderBrush);

            var submenu = Part<Popup>(parent, "PART_Popup");
            submenu.ShouldUseOverlayLayer = true;
            parent.IsSubMenuOpen = true;
            Dispatcher.UIThread.RunJobs();
            AssertPopupSurface(host.Window, Assert.IsType<Border>(submenu.Child));
            Assert.True(child.IsChecked);
            Assert.True(Part<ContentControl>(child, "PART_ToggleIconPresenter").IsVisible);

            // 打开期间切换主题也必须更新弹出表面及项目的实际绘制色。
            var previousBackground = presenter.Background;
            host.Window.RequestedThemeVariant = dark ? ThemeVariant.Light : ThemeVariant.Dark;
            Dispatcher.UIThread.RunJobs();
            Assert.NotEqual(previousBackground, presenter.Background);
            AssertPopupSurface(host.Window, Part<Border>(presenter, "LayoutRoot"));
            AssertPopupSurface(host.Window, Assert.IsType<Border>(submenu.Child));
        }
        finally
        {
            menu.Hide();
        }
    }

    [AvaloniaFact]
    public void ContextMenu_UsesTheSameSurfaceAndItemGeometry()
    {
        var item = new MenuItem { Header = "Open" };
        var menu = new ContextMenu { Items = { item } };
        var trigger = new Border { Width = 220, Height = 40, ContextMenu = menu };
        using var host = CreateHost(trigger, dark: true);
        menu.Open(trigger);
        Dispatcher.UIThread.RunJobs();
        try
        {
            AssertPopupSurface(host.Window, Assert.Single(menu.GetVisualChildren().OfType<Border>()));
            Assert.Equal(40, item.Bounds.Height);
            Assert.Equal(new CornerRadius(8), item.CornerRadius);
        }
        finally
        {
            menu.Close();
        }
    }

    [AvaloniaFact]
    public void Golden_ChoicePopupLight_MatchesBaseline()
    {
        var combo = CreateChoice();
        using var host = CreateHost(combo, dark: false);
        OpenChoice(combo);
        GoldenScreenshot.Compare(host.Window, "choice-popup-light");
    }

    [AvaloniaFact]
    public void Golden_ChoicePopupDark_MatchesBaseline()
    {
        var combo = CreateChoice();
        using var host = CreateHost(combo, dark: true);
        OpenChoice(combo);
        GoldenScreenshot.Compare(host.Window, "choice-popup-dark");
    }

    [AvaloniaFact]
    public void Golden_SelectedChoiceFidelityFocus_MatchesBaseline()
    {
        var combo = CreateChoice();
        using var host = CreateHost(combo, dark: false, "#FF0000", ThemeColorVariants.Fidelity);
        FocusSelectedChoice(combo, host.Window, hover: false);
        GoldenScreenshot.Compare(host.Window, "choice-popup-fidelity-focus");
    }

    [AvaloniaFact]
    public void Golden_SelectedChoiceFidelityHover_MatchesBaseline()
    {
        var combo = CreateChoice();
        using var host = CreateHost(combo, dark: false, "#2E7DF6", ThemeColorVariants.Fidelity);
        FocusSelectedChoice(combo, host.Window, hover: true);
        GoldenScreenshot.Compare(host.Window, "choice-popup-fidelity-hover");
    }

    private static void FocusSelectedChoice(ComboBox combo, Window window, bool hover)
    {
        OpenChoice(combo);
        var selected = Assert.IsType<ComboBoxItem>(combo.ContainerFromIndex(0));
        Assert.IsType<ComboBoxItem>(combo.ContainerFromIndex(2)).Focus(NavigationMethod.Unspecified);
        window.MouseMove(new Point(440, 300));
        selected.Focus(NavigationMethod.Tab);
        if (hover)
        {
            window.MouseMove(selected.TranslatePoint(new Point(20, 15), window)!.Value);
        }
    }

    private static ComboBox CreateChoice() => new()
    {
        Width = 280,
        Classes = { "setting-control" },
        Items =
        {
            new ComboBoxItem { Content = "Follow system" },
            new ComboBoxItem { Content = "Unavailable", IsEnabled = false },
            new ComboBoxItem { Content = "Dark" }
        },
        SelectedIndex = 0
    };

    private static Popup OpenChoice(ComboBox combo)
    {
        var popup = Part<Popup>(combo, "PART_Popup");
        popup.ShouldUseOverlayLayer = true;
        combo.Focus(NavigationMethod.Tab);
        combo.IsDropDownOpen = true;
        Dispatcher.UIThread.RunJobs();
        return popup;
    }

    private static void Press(Window window, Key key, PhysicalKey physical)
    {
        window.KeyPress(key, RawInputModifiers.None, physical, string.Empty);
        window.KeyRelease(key, RawInputModifiers.None, physical, string.Empty);
        Dispatcher.UIThread.RunJobs();
    }

    private static T Part<T>(Control control, string name) where T : Control =>
        Assert.Single(control.GetVisualDescendants().OfType<T>(), part => part.Name == name);

    private static void AssertPopupSurface(Window window, Border surface)
    {
        AssertPaint(window, "Launcher.Color.Dialog.Background", surface.Background);
        AssertPaint(window, "Launcher.Color.Card.Border", surface.BorderBrush);
        Assert.Equal(new CornerRadius(8), surface.CornerRadius);
        Assert.Equal(new Thickness(1), surface.BorderThickness);
        Assert.Equal(new Thickness(4), surface.Padding);
    }

    private static void AssertPaint(Window window, string role, IBrush? actual) =>
        Assert.Equal(window.FindResource(window.ActualThemeVariant, role), actual);

    private static Host CreateHost(Control control, bool dark, string seed = "#0078D4", string schemeVariant = ThemeColorVariants.TonalSpot)
    {
        var panel = new StackPanel
        {
            Spacing = 16,
            HorizontalAlignment = HorizontalAlignment.Left,
            Children = { new TextBlock { Text = "Appearance", FontSize = 20 }, control }
        };
        var window = new Window
        {
            Content = new Border { Padding = new Thickness(24), Child = panel },
            Width = 460,
            Height = 320,
            FontFamily = new FontFamily("Segoe UI"),
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
        };
        // 独立窗口也钉住生产调色盘，截图不依赖其他用例留在 Application 中的动态角色。
        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            var scheme = MaterialSchemeGenerator.CreateScheme(Color.Parse(seed), schemeVariant, variant == ThemeVariant.Dark);
            var palette = new ResourceDictionary();
            foreach (var role in MaterialSchemeGenerator.BuildRoleBrushes(scheme, seedFollowingNeutrals: false, isDark: variant == ThemeVariant.Dark))
            {
                palette[role.Key] = role.Value;
            }

            window.Resources.ThemeDictionaries[variant] = palette;
        }

        window.Bind(Window.BackgroundProperty, window.GetResourceObservable("Launcher.Color.Dialog.Background"));
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return new Host(window);
    }

    private sealed class Host(Window window) : IDisposable
    {
        public Window Window { get; } = window;
        public void Dispose() => Window.Close();
    }
}
