using System;
using System.Threading;
using System.Threading.Tasks;

namespace Cafe.Launcher.Core.Services.Diagnostics;

/// <summary>
/// Core-side diagnostic seam. Presentation and process hosts adapt their logging implementation
/// to this narrow contract; Core never needs to know about a UI or logger implementation.
/// </summary>
public interface ILauncherDiagnostics
{
    Task DebugAsync(
        string title,
        string? message = null,
        CancellationToken cancellationToken = default);

    Task ErrorAsync(
        string title,
        string? message,
        Exception exception,
        CancellationToken cancellationToken = default);
}
