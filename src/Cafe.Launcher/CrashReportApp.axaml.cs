using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Cafe.Launcher.Constants;
using Cafe.Launcher.Services;
using Cafe.Launcher.Core.Constants;
using Cafe.Launcher.Core.Services.Diagnostics;
using Cafe.Launcher.Services.Diagnostics;
using Cafe.Launcher.UI.Services.Diagnostics;
using Cafe.Launcher.UI.Views;
using Cafe.Launcher.Core.Services;

namespace Cafe.Launcher;

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
            // 崩溃报告窗口展示的版本与提交来自宿主注入的构建标识：不传的话窗口上的
            // Version/Commit 两行是空的，而独立报告进程拿不到别的来源。
            var report = CrashReportBootstrap.Resolve(Program.CrashReportPath, BuildInfo.Identity);
            CrashReportBootstrap.ApplyCulture(report.UiCulture);

            var crashWindow = new CrashReportWindow(
                report,
                LauncherDataRoot.ForCurrentProcess(LauncherProfiles.Cafe.ProductName));
            desktop.MainWindow = crashWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
