using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.Views;

namespace Cafe.Launcher.Avalonia;

/// <summary>Minimal application lifetime used when the primary launcher UI cannot be trusted.</summary>
public partial class CrashReportApp : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var report = CrashReportStore.TryRead(Program.CrashReportPath ?? "")
                         ?? CreateUnreadableReport();
            ApplyReportCulture(report.UiCulture);

            var crashWindow = new CrashReportWindow(report);
            desktop.MainWindow = crashWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ApplyReportCulture(string cultureName)
    {
        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }
        catch (CultureNotFoundException)
        {
            // The neutral English resources remain available when the captured culture is invalid.
        }
    }

    private static CrashReport CreateUnreadableReport() => new()
    {
        Id = "CR-UNAVAILABLE",
        OccurredAt = DateTimeOffset.Now,
        Source = "CrashReportMode",
        AppVersion = Constants.BuildInfo.LauncherVersion,
        OperatingSystem = Environment.OSVersion.ToString(),
        UiCulture = CultureInfo.CurrentUICulture.Name,
        ExceptionType = nameof(InvalidOperationException),
        TechnicalDetails = "The crash snapshot could not be read. Open the log directory for any diagnostics that were saved."
    };
}
