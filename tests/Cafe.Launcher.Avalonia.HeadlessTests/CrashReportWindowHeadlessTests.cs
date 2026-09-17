using System.Globalization;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.ViewModels;
using Cafe.Launcher.Avalonia.Views;

namespace Cafe.Launcher.Avalonia.HeadlessTests;

public sealed class CrashReportWindowHeadlessTests
{
    /// <summary>崩溃窗口只需要一个数据根；日志目录本身在本组测试里不被读取。</summary>
    private static LauncherDataRoot TestDataRoot() =>
        new(Path.Combine(Path.GetTempPath(), "Cafe.Launcher.Avalonia.HeadlessTests", Guid.NewGuid().ToString("N")));

    [AvaloniaFact]
    public void CrashReportWindow_WhenTechnicalDetailsExpand_GrowsWithContent()
    {
        using var themeVariant = ThemeVariantSnapshot.Capture(ThemeVariant.Light);
        var window = new CrashReportWindow(CreateReport(), TestDataRoot())
        {
            FontFamily = new FontFamily("Segoe UI")
        };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var collapsedHeight = window.Bounds.Height;
            var collapsedWidth = window.Bounds.Width;
            var expander = window.FindControl<Expander>("TechnicalDetailsExpander");
            Assert.NotNull(expander);
            Assert.True(collapsedHeight > 0, "The content-driven window must size itself before the assertion.");

            expander!.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();

            Assert.True(
                window.Bounds.Height > collapsedHeight,
                $"Expanding the details must grow the window (collapsed {collapsedHeight}, expanded {window.Bounds.Height}).");
            Assert.Equal(collapsedWidth, window.Bounds.Width);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void CrashReportWindow_WhenShown_DefaultsToFriendlySummaryWithCollapsedDetails()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        using var themeVariant = ThemeVariantSnapshot.Capture(ThemeVariant.Light);
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");

        var report = CreateReport();
        var window = new CrashReportWindow(report, TestDataRoot())
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
            Assert.Equal(HorizontalAlignment.Center, exitButton!.HorizontalContentAlignment);
            Assert.Equal(VerticalAlignment.Center, exitButton.VerticalContentAlignment);
            Assert.Equal("CR-TEST", viewModel.ReportId);
            Assert.Equal("0123456789abcdef0123456789abcdef01234567", viewModel.Build);
            Assert.Equal("technical details", viewModel.TechnicalDetails);

            GoldenScreenshot.Compare(window, "crash-report-window");
        }
        finally
        {
            window.Close();
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    private static CrashReport CreateReport()
    {
        // The window renders the time in local time, so a fixed instant would render a
        // different string (and break the golden) on a runner in another time zone. Anchor
        // the report to a fixed wall-clock time in the runner's own offset instead.
        var wallClock = new DateTime(2026, 9, 8, 21, 47, 2, DateTimeKind.Unspecified);
        return new CrashReport
        {
            Id = "CR-TEST",
            OccurredAt = new DateTimeOffset(wallClock, TimeZoneInfo.Local.GetUtcOffset(wallClock)),
            Source = "Test",
            AppVersion = "1.2.3",
            // Fixed value: the window renders the snapshot's commit, so a build-derived SHA
            // would change the golden on every commit and on the CI merge commit.
            BuildSha = "0123456789abcdef0123456789abcdef01234567",
            OperatingSystem = "Test OS",
            UiCulture = "en",
            ExceptionType = nameof(InvalidOperationException),
            TechnicalDetails = "technical details"
        };
    }
}
