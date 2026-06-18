using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Cafe.Launcher.Avalonia.Services;

public static class ProcessService
{
    public static Task<bool> IsExeRunningAsync(string exeName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(exeName))
        {
            return Task.FromResult(false);
        }

        var nameWithoutExtension = Path.GetFileNameWithoutExtension(exeName);
        var isRunning = Process.GetProcessesByName(nameWithoutExtension).Length > 0;
        return Task.FromResult(isRunning);
    }
}
