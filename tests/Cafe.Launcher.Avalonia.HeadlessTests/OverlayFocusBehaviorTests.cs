using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Cafe.Launcher.Avalonia.Views;

namespace Cafe.Launcher.Avalonia.HeadlessTests;

public sealed class OverlayFocusBehaviorTests
{
    [AvaloniaFact]
    public void Overlay_WhenShown_FocusesFirstControlAndRestoresPreviousFocusWhenHidden()
    {
        var previous = new Button { Content = "Previous" };
        var first = new Button { Content = "First" };
        var second = new Button { Content = "Second" };
        var overlay = new Grid
        {
            IsVisible = false,
            Children = { first, second }
        };
        OverlayFocusBehavior.SetIsEnabled(overlay, true);
        var window = new Window
        {
            Content = new Grid
            {
                Children = { previous, overlay }
            }
        };
        window.Show();
        previous.Focus(NavigationMethod.Tab);
        Assert.Same(previous, window.FocusManager?.GetFocusedElement());

        overlay.IsVisible = true;
        Dispatcher.UIThread.RunJobs();

        Assert.Same(first, window.FocusManager?.GetFocusedElement());

        overlay.IsVisible = false;
        Dispatcher.UIThread.RunJobs();

        Assert.Same(previous, window.FocusManager?.GetFocusedElement());
        window.Close();
    }
}
