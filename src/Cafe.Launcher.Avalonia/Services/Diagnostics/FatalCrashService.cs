using System;
using System.Threading;
using Cafe.Launcher.Avalonia.Constants;

namespace Cafe.Launcher.Avalonia.Services.Diagnostics;

/// <summary>Terminal error boundary kept separate from recoverable and critical shell errors.</summary>
public interface IFatalCrashService
{
    /// <summary>Raised once when a healthy UI process can show the independent crash window itself.</summary>
    event Action<CrashReport>? FatalCrashRequested;

    /// <summary>
    /// Captures an unrecoverable failure and requests the in-process crash window. This is
    /// the only tier-1 entry: it requires a live UI, so process-boundary faults (AppDomain,
    /// dispatcher, entry escape) go through the isolated reporter instead.
    /// </summary>
    void HandleFatalCrash(CrashOrigin origin, Exception exception);
}

/// <summary>Coordinates first-failure capture, deduplication, logging, and reporter presentation.</summary>
internal sealed class FatalCrashService : IFatalCrashService
{
    private readonly object gate = new();
    private readonly UnifiedLogger? logger;
    private readonly CrashReportStore reportStore;
    private readonly ICrashReporterLauncher reporterLauncher;
    private CrashReport? primaryReport;

    public FatalCrashService(
        UnifiedLogger? logger,
        CrashReportStore reportStore,
        ICrashReporterLauncher reporterLauncher)
    {
        this.logger = logger;
        this.reportStore = reportStore;
        this.reporterLauncher = reporterLauncher;
    }

    public event Action<CrashReport>? FatalCrashRequested;

    /// <inheritdoc />
    public void HandleFatalCrash(CrashOrigin origin, Exception exception)
    {
        var (report, isPrimary) = Capture(origin, exception);
        if (!isPrimary)
        {
            return;
        }

        var handler = FatalCrashRequested;
        if (handler is null)
        {
            _ = reporterLauncher.TryLaunch(report.SnapshotPath);
            return;
        }

        try
        {
            handler(report);
        }
        catch
        {
            _ = reporterLauncher.TryLaunch(report.SnapshotPath);
        }
    }

    /// <summary>Captures a process-boundary failure and starts the isolated reporter once.</summary>
    internal void HandleUnhandledCrash(CrashOrigin origin, Exception exception)
    {
        var (report, isPrimary) = Capture(origin, exception);
        if (isPrimary)
        {
            // An empty path means no snapshot could be written anywhere; the reporter then
            // opens in its "snapshot unavailable" mode instead of leaving no surface at all.
            _ = reporterLauncher.TryLaunch(report.SnapshotPath);
        }
    }

    private (CrashReport Report, bool IsPrimary) Capture(CrashOrigin origin, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        CrashReport report;
        lock (gate)
        {
            if (primaryReport is not null)
            {
                reportStore.AppendAdditionalFailure(primaryReport, origin, exception);
                LogFatal(origin, exception);
                return (primaryReport, false);
            }

            try
            {
                primaryReport = reportStore.Create(origin, exception);
            }
            catch
            {
                primaryReport = reportStore.TryPersistTransient(CreateTransientReport(origin, exception));
            }

            report = primaryReport;
        }

        LogFatal(origin, exception);
        return (report, true);
    }

    private void LogFatal(CrashOrigin origin, Exception exception)
    {
        try
        {
            logger?.LogAsync(
                    LogEntrySeverity.Fatal,
                    origin.ToSourceLabel(),
                    exception: exception,
                    cancellationToken: CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }
        catch
        {
            // The synchronous snapshot is the authoritative fallback when logging is unavailable.
        }
    }

    private static CrashReport CreateTransientReport(CrashOrigin origin, Exception exception)
    {
        var now = DateTimeOffset.Now;
        return new CrashReport
        {
            Id = $"CR-{now:yyyyMMdd-HHmmss}-LOCAL",
            OccurredAt = now,
            Source = origin.ToSourceLabel(),
            AppVersion = BuildInfo.LauncherVersion,
            OperatingSystem = Environment.OSVersion.ToString(),
            UiCulture = System.Globalization.CultureInfo.CurrentUICulture.Name,
            ExceptionType = exception.GetType().FullName ?? exception.GetType().Name,
            TechnicalDetails = CrashReportStore.Sanitize(exception.ToString())
        };
    }
}
