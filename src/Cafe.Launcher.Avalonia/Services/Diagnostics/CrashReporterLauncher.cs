using System;
using System.Diagnostics;

namespace Cafe.Launcher.Avalonia.Services.Diagnostics;

internal interface ICrashReporterLauncher
{
    bool TryLaunch(string snapshotPath);
}

/// <summary>Starts the current executable in its isolated crash-report mode.</summary>
internal sealed class CrashReporterLauncher : ICrashReporterLauncher
{
    public bool TryLaunch(string snapshotPath)
    {
        if (string.IsNullOrWhiteSpace(snapshotPath) || string.IsNullOrWhiteSpace(Environment.ProcessPath))
        {
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = Environment.ProcessPath,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add(Program.CrashReportArgument);
            startInfo.ArgumentList.Add(snapshotPath);
            return Process.Start(startInfo) is not null;
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                           or System.ComponentModel.Win32Exception
                                           or NotSupportedException)
        {
            return false;
        }
    }
}
