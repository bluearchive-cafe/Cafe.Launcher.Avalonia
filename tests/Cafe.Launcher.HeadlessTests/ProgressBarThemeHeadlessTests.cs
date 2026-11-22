using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.Styling;
using Xunit;

namespace Cafe.Launcher.HeadlessTests;

public sealed class ProgressBarThemeHeadlessTests
{
    [AvaloniaFact]
    public void ProgressBar_UsesTheFluentThemeAccent()
    {
        var application = Application.Current
            ?? throw new InvalidOperationException("Headless application is not initialised.");
        var theme = Assert.Single(application.Styles.OfType<FluentAvaloniaTheme>());
        var originalColor = theme.CustomAccentColor;
        var progressBar = new ProgressBar { Value = 50 };
        var window = new Window { Content = progressBar };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            theme.CustomAccentColor = Color.Parse("#FF2E9E46");
            Dispatcher.UIThread.RunJobs();


            var indicator = progressBar.GetVisualDescendants()
                .OfType<Border>()
                .Single(control => control.Name == "PART_Indicator");
            Assert.Equal(progressBar.FindResource("ProgressBarForeground"), indicator.Background);
        }
        finally
        {
            theme.CustomAccentColor = originalColor;
            window.Close();
        }
    }
}
