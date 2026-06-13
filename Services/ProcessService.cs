using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Cafe.Launcher.Avalonia.Services;

public static class ProcessService
{
    public static async Task<bool> IsExeRunningAsync(string exeName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(exeName))
        {
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "tasklist",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("/FO");
            startInfo.ArgumentList.Add("CSV");
            startInfo.ArgumentList.Add("/NH");
            startInfo.ArgumentList.Add("/FI");
            startInfo.ArgumentList.Add($"IMAGENAME eq {exeName}");

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return output.Contains(exeName, StringComparison.OrdinalIgnoreCase);
        }

        return Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exeName)).Length > 0;
    }
}
