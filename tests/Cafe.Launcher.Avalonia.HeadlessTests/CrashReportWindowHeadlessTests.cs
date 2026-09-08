using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.ViewModels;
using Cafe.Launcher.Avalonia.Views;

namespace Cafe.Launcher.Avalonia.HeadlessTests;

public sealed class CrashReportWindowHeadlessTests
{
    [AvaloniaFact]
    public void CrashReportWindow_WhenShown_DefaultsToFriendlySummaryWithCollapsedDetails()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        var application = Application.Current
            ?? throw new InvalidOperationException("Headless application is not initialised.");
        var previousTheme = application.RequestedThemeVariant;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        application.RequestedThemeVariant = ThemeVariant.Light;

        var report = new CrashReport
        {
            Id = "CR-TEST",
            OccurredAt = new DateTimeOffset(2026, 9, 8, 21, 47, 2, TimeSpan.FromHours(8)),
            Source = "Test",
            AppVersion = "1.2.3",
            OperatingSystem = "Test OS",
            UiCulture = "en",
            ExceptionType = nameof(InvalidOperationException),
            TechnicalDetails = "technical details"
        };
        var window = new CrashReportWindow(report)
        {
            FontFamily = new FontFamily("Segoe UI")
        };

        try
        {
            window.Show();

            var expander = window.FindControl<Expander>("TechnicalDetailsExpander");
            var exitButton = window.FindControl<Button>("ExitButton");
            var viewModel = Assert.IsType<CrashReportWindowViewModel>(window.DataContext);
            Assert.NotNull(expander);
            Assert.False(expander!.IsExpanded);
            Assert.NotNull(exitButton);
            Assert.Equal("CR-TEST", viewModel.ReportId);
            Assert.Equal("technical details", viewModel.TechnicalDetails);

            GoldenScreenshot.Compare(window, "crash-report-window");
        }
        finally
        {
            window.Close();
            application.RequestedThemeVariant = previousTheme;
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }
}
