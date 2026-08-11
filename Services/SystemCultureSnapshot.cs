using System;
using System.Globalization;

namespace Cafe.Launcher.Avalonia.Services;

/// <summary>
/// Captures the process-wide <see cref="CultureInfo.CurrentCulture"/>
/// and <see cref="CultureInfo.CurrentUICulture"/> exactly once at startup
/// so &quot;auto&quot; can restore the genuine OS settings later.
/// </summary>
public sealed class SystemCultureSnapshot
{
    private bool captured;

    /// <summary>The culture used for formatting (numbers, dates, currency).</summary>
    public CultureInfo Culture { get; private set; } = CultureInfo.CurrentCulture;

    /// <summary>The culture used for resource lookups (UI strings).</summary>
    public CultureInfo UiCulture { get; private set; } = CultureInfo.CurrentUICulture;

    /// <summary>
    /// Takes the snapshot. Subsequent calls are no-ops so late
    /// initialization paths never overwrite the genuine OS snapshot.
    /// </summary>
    public void Capture()
    {
        if (captured) return;

        Culture = CultureInfo.CurrentCulture;
        UiCulture = CultureInfo.CurrentUICulture;
        captured = true;
    }

    /// <summary>
    /// Applies the stored cultures to the current thread.
    /// Callers that also need default-thread cultures should set those
    /// via the caller (<see cref="LocalizationService.SetLanguage"/> does this).
    /// </summary>
    public void Restore()
    {
        CultureInfo.CurrentCulture = Culture;
        CultureInfo.CurrentUICulture = UiCulture;
    }
}
