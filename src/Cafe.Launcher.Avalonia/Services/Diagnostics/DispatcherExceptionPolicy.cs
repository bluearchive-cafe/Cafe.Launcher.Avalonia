using System;

namespace Cafe.Launcher.Avalonia.Services.Diagnostics;

/// <summary>
/// Classifies exceptions that reach the UI-dispatcher boundary.
/// </summary>
/// <remarks>
/// Cancellation is control flow rather than a fault. Disposing the FreeDesktop tray icon
/// cancels its DBus watcher, and Avalonia's <c>async void</c> watcher rethrows the resulting
/// <see cref="OperationCanceledException"/> onto the dispatcher during an otherwise clean
/// exit. Treating that as a crash would abort shutdown, so it is logged instead of captured.
/// </remarks>
internal static class DispatcherExceptionPolicy
{
    /// <summary>
    /// Whether a dispatcher exception must be captured as a fatal crash. Cancellation is not
    /// fatal; every other exception is.
    /// </summary>
    public static bool IsFatal(Exception exception) => exception is not OperationCanceledException;
}
