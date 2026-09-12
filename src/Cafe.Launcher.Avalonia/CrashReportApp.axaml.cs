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
            var report = CrashReportBootstrap.Resolve(Program.CrashReportPath);
            CrashReportBootstrap.ApplyCulture(report.UiCulture);

            var crashWindow = new CrashReportWindow(report);
            desktop.MainWindow = crashWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
