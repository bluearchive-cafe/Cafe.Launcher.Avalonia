using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Cafe.Launcher.Avalonia.ViewModels;
using Cafe.Launcher.Avalonia.Views;

namespace Cafe.Launcher.Avalonia.HeadlessTests;

public sealed class HeadlessSmokeTests
{
    [AvaloniaFact]
    public void Window_WhenShown_AttachesContentToVisualTree()
    {
        var text = new TextBlock { Text = "headless" };
        var window = new Window { Content = text };

        window.Show();

        Assert.Same(window, TopLevel.GetTopLevel(text));
        window.Close();
    }

    [AvaloniaFact]
    public void MainWindow_WhenCreated_CanInstantiate()
    {
        // In headless tests without DI, DataContext is null by design.
        // Verify the window shell can be instantiated without exceptions.
        var window = new MainWindow();
        window.Show();

        Assert.NotNull(window);
        Assert.NotNull(window.Content);

        window.Close();
    }

    [AvaloniaFact]
    public void MainWindow_WhenShown_IsVisible()
    {
        var window = new MainWindow();
        window.Show();

        Assert.True(window.IsVisible);

        window.Close();
    }
}
