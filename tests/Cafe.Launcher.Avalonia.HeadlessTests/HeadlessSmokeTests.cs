using Avalonia.Controls;
using Avalonia.Headless.XUnit;

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
}
