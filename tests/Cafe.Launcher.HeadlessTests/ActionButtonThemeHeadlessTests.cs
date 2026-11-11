using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Cafe.Launcher.HeadlessTests;

/// <summary>跨功能验证实际主题解析、焦点几何和输入控件，避免局部覆盖重新制造视觉分支。</summary>
public sealed class ActionButtonThemeHeadlessTests
{
    private static readonly string[][] StandardActionClasses =
    [
        ["primary-action"],
        ["flat-action"],
        ["danger-action"],
        ["primary-action", "dialog-action", "confirm-dialog-action"],
        ["flat-action", "dialog-action", "confirm-dialog-action"],
        ["danger-action", "dialog-action", "confirm-dialog-action"],
        ["primary-action", "dialog-action", "wizard-action"],
        ["dialog-action", "wizard-action"]
    ];

    [AvaloniaFact]
    public void StandardActions_AcrossFeatureContexts_ResolveTheSameGeometry()
    {
        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            var buttons = CreateActions();
            using var host = CreateHost(buttons, variant);

            foreach (var button in buttons)
            {
                Assert.Equal(40, button.Bounds.Height);
                // 向导动作带只有两枚动作，按 ADR-059 取 100 DIP 最小宽度使二者同宽
                // （贴近中文自然宽度，短文案不被拉长）；角色外观（圆角、字号、
                // 描边、语义色）仍与其余标准动作一致。
                Assert.Equal(
                    button.Classes.Contains("wizard-action") ? 100 : 80,
                    button.Bounds.Width);
                Assert.Equal(new CornerRadius(8), button.CornerRadius);
                Assert.Equal(14, button.FontSize);
                Assert.Equal(new Thickness(2), button.BorderThickness);
                Assert.Equal(0, BrushColor(button.BorderBrush).A);
            }
        }
    }

    [AvaloniaFact]
    public void StandardActions_WhenKeyboardFocusMoves_KeepBoundsAndShowSemanticFocus()
    {
        var buttons = CreateActions();
        using var host = CreateHost(buttons, ThemeVariant.Light);
        var bounds = buttons.Select(button => button.Bounds).ToArray();
        buttons[0].Focus(NavigationMethod.Unspecified);

        for (var index = 1; index < buttons.Length; index++)
        {
            host.Window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, string.Empty);
            host.Window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, string.Empty);
            Dispatcher.UIThread.RunJobs();

            Assert.True(buttons[index].IsFocused);
            Assert.Contains(":focus-visible", buttons[index].Classes);
            Assert.True(BrushColor(buttons[index].BorderBrush).A > 0);
            Assert.Equal(bounds, buttons.Select(button => button.Bounds).ToArray());
        }
    }

    [AvaloniaFact]
    public void StandardActions_WhenDisabled_KeepGeometryAndUseOneFeedbackPalette()
    {
        var buttons = CreateActions();
        using var host = CreateHost(buttons, ThemeVariant.Dark);
        var bounds = buttons.Select(button => button.Bounds).ToArray();
        foreach (var button in buttons)
        {
            button.IsEnabled = false;
        }

        Dispatcher.UIThread.RunJobs();

        foreach (var button in buttons)
        {
            Assert.Equal(BrushColor(buttons[0].Background), BrushColor(button.Background));
            Assert.Equal(BrushColor(buttons[0].Foreground), BrushColor(button.Foreground));
            Assert.Equal(new Thickness(2), button.BorderThickness);
            Assert.Equal(0, BrushColor(button.BorderBrush).A);
        }

        Assert.Equal(bounds, buttons.Select(button => button.Bounds).ToArray());
    }

    [AvaloniaFact]
    public void StandardActions_InTallFormRows_StayCenteredAtStandardHeight()
    {
        var button = new Button { Content = "Reset", Classes = { "flat-action", "danger-action" } };
        var row = new Border { Width = 280, Height = 80, Child = button };
        using var host = CreateHost([row], ThemeVariant.Light);

        Assert.Equal(40, button.Bounds.Height);
        Assert.Equal(20, button.Bounds.Y);
        Assert.True(button.Bounds.Width < row.Bounds.Width);
    }

    [AvaloniaFact]
    public void SharedFields_UseStandardHeightAndCornerRadiusIncludingColorPickerTrigger()
    {
        var text = new TextBox { Text = "12345678", Classes = { "field-input" } };
        var select = new ComboBox { ItemsSource = new[] { "Cafe", "Official" }, SelectedIndex = 0, Classes = { "setting-control" } };
        var picker = new ColorPicker { Color = Colors.CornflowerBlue, Classes = { "setting-control" } };
        using var host = CreateHost([text, select, picker], ThemeVariant.Light);

        foreach (var control in new Control[] { text, select, picker })
        {
            Assert.Equal(40, control.Bounds.Height);
        }

        Assert.Equal(new CornerRadius(8), text.CornerRadius);
        Assert.Equal(new CornerRadius(8), select.CornerRadius);
        Assert.Equal(new CornerRadius(8), picker.CornerRadius);
        var trigger = Assert.Single(picker.GetVisualDescendants().OfType<DropDownButton>());
        Assert.Equal(new CornerRadius(8), trigger.CornerRadius);
        Assert.Equal(14, trigger.FontSize);
        Assert.Equal(40, trigger.Bounds.Height);
    }

    [AvaloniaFact]
    public void StandardActions_WithLargeWrappedLabels_GrowWithoutClipping()
    {
        var text = new TextBlock { Text = "保存所有尚未提交的外观设置改动", FontSize = 24, TextWrapping = TextWrapping.Wrap, MaxWidth = 120 };
        var button = new Button { Content = text, Classes = { "primary-action", "dialog-action" } };
        using var host = CreateHost([button], ThemeVariant.Light);

        Assert.True(button.Bounds.Height > 40);
        Assert.True(button.Bounds.Height >= text.DesiredSize.Height + button.Padding.Top + button.Padding.Bottom + button.BorderThickness.Top + button.BorderThickness.Bottom);
    }

    private static Button[] CreateActions() => StandardActionClasses.Select(classes =>
    {
        var button = new Button { Content = "Save", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
        foreach (var name in classes)
        {
            button.Classes.Add(name);
        }

        return button;
    }).ToArray();

    private static Color BrushColor(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    private static Host CreateHost(IEnumerable<Control> controls, ThemeVariant variant)
    {
        var panel = new StackPanel { Spacing = 8, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
        foreach (var control in controls)
        {
            panel.Children.Add(control);
        }

        var window = new Window { Content = panel, Width = 800, Height = 600, RequestedThemeVariant = variant };
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
