using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace Cafe.Launcher.Avalonia.HeadlessTests;

public sealed class ProgressBarThemeHeadlessTests
{
    [AvaloniaFact]
    public void ProgressBar_ApplicationThemeColorChanges_UsesLauncherPrimary()
    {
        var application = Application.Current
            ?? throw new InvalidOperationException("Headless application is not initialised.");
        Assert.True(
            application.Resources.TryGetResource(
                "Launcher.Color.Primary",
                application.ActualThemeVariant,
                out var resource));
        var primaryBrush = Assert.IsType<SolidColorBrush>(resource);
        var originalColor = primaryBrush.Color;
        var progressBar = new ProgressBar { Value = 50 };
        var window = new Window { Content = progressBar };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            primaryBrush.Color = Color.Parse("#FF2E9E46");
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(primaryBrush.Color, Assert.IsType<SolidColorBrush>(progressBar.Foreground).Color);
            var indicator = progressBar.GetVisualDescendants()
                .OfType<Border>()
                .Single(control => control.Name == "PART_Indicator");
            Assert.Equal(primaryBrush.Color, Assert.IsType<SolidColorBrush>(indicator.Background).Color);
        }
        finally
        {
            primaryBrush.Color = originalColor;
            window.Close();
        }
    }
}
