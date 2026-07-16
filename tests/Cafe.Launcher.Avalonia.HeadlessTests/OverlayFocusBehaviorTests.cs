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

    [AvaloniaFact]
    public void Overlay_WhenDisabledWhileVisible_RestoresPreviousFocus()
    {
        var previous = new Button { Content = "Previous" };
        var first = new Button { Content = "First" };
        var overlay = new Grid
        {
            IsVisible = false,
            Children = { first }
        };
        OverlayFocusBehavior.SetIsEnabled(overlay, true);
        var window = new Window
        {
            Content = new Grid
            {
                Children = { previous, overlay }
            }
        };

        try
        {
            window.Show();
            previous.Focus(NavigationMethod.Tab);
            overlay.IsVisible = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Same(first, window.FocusManager?.GetFocusedElement());

            OverlayFocusBehavior.SetIsEnabled(overlay, false);
            Dispatcher.UIThread.RunJobs();

            Assert.Same(previous, window.FocusManager?.GetFocusedElement());
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Overlay_WhenEnabledAfterAttachAndVisible_FocusesFirstControl()
    {
        var previous = new Button { Content = "Previous" };
        var first = new Button { Content = "First" };
        var overlay = new Grid
        {
            IsVisible = true,
            Children = { first }
        };
        var window = new Window
        {
            Content = new Grid
            {
                Children = { previous, overlay }
            }
        };

        try
        {
            window.Show();
            previous.Focus(NavigationMethod.Tab);

            OverlayFocusBehavior.SetIsEnabled(overlay, true);
            Dispatcher.UIThread.RunJobs();

            Assert.Same(first, window.FocusManager?.GetFocusedElement());
        }
        finally
        {
            window.Close();
        }
    }
}
