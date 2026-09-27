using System;
using System.Diagnostics;
using Cafe.Launcher.UI.Services.Diagnostics;

namespace Cafe.Launcher.Services.Diagnostics;

/// <summary>Starts the current executable in its isolated crash-report mode.</summary>
public sealed class CrashReporterLauncher : ICrashReporterLauncher
{
    public bool TryLaunch(string snapshotPath)
    {
        if (string.IsNullOrWhiteSpace(Environment.ProcessPath))
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
            if (!string.IsNullOrWhiteSpace(snapshotPath))
            {
                startInfo.ArgumentList.Add(snapshotPath);
            }

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
