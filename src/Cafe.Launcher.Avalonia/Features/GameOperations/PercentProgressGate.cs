using System.Threading;

namespace Cafe.Launcher.Avalonia.Features.GameOperations;

/// <summary>
/// Gates per-file percent callbacks so the consumer receives them only when
/// the integer percent actually changes (AUD-PERF-007). The verify/diff/
/// uninstall stages used to dispatch one callback per manifest file — for a
/// full installation that is thousands of UI-thread posts, each doing about a
/// dozen observable-property writes plus localized string formatting, the same
/// flooding <see cref="DownloadProgressAccumulator"/> solves with a time gate
/// on the byte-streaming download stage. A percent gate fits these stages:
/// they report a 0-100 percent derived from a per-stage counter, a fresh gate
/// always delivers 0, repeats are suppressed, and a stage restart (percent
/// rolling back) is a value change and is delivered too. Explicit stage
/// emissions (stage switches, repair-confirm) bypass the gate on purpose.
/// Thread-safe: the install-verification stage reports from parallel workers.
/// </summary>
internal sealed class PercentProgressGate
{
    private int lastReported = -1;

    /// <summary>Reports whether <paramref name="percent"/> differs from the last delivered value.</summary>
    internal bool ShouldDeliver(int percent) => Interlocked.Exchange(ref lastReported, percent) != percent;
}
