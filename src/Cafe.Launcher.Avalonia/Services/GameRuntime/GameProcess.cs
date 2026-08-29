using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Cafe.Launcher.Avalonia.Services.GameRuntime;

/// <summary>
/// Trackable handle to a launched game host process. The host process is the
/// process the runner started (game executable natively, or the compatibility
/// layer entry such as umu-run), not necessarily the game PE itself.
/// </summary>
public sealed record GameProcess(Process HostProcess, string RunnerId)
{
    public int ProcessId => HostProcess.Id;

    public Task WaitForExitAsync(CancellationToken cancellationToken = default) =>
        HostProcess.WaitForExitAsync(cancellationToken);
}
