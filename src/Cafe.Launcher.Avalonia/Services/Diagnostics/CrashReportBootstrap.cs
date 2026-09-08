using System;
using System.Globalization;
using Cafe.Launcher.Avalonia.Constants;

namespace Cafe.Launcher.Avalonia.Services.Diagnostics;

/// <summary>
/// Snapshot resolution and culture application for the isolated crash reporter.
/// Deliberately free of Avalonia and DI: the reporter's last-resort surface must
/// stay reachable when the application lifetime cannot be trusted, and this is
/// the part of that path worth testing directly.
/// </summary>
internal static class CrashReportBootstrap
{
    /// <summary>
    /// Resolves the snapshot at <paramref name="snapshotPath"/>. A missing,
    /// unreadable, or malformed snapshot yields the "snapshot unavailable"
    /// report, so the reporter always has a surface to show.
    /// </summary>
    public static CrashReport Resolve(string? snapshotPath) =>
        CrashReportStore.TryRead(snapshotPath ?? "") ?? CreateUnreadableReport();

    /// <summary>
    /// Applies the UI culture captured with the snapshot. An absent or invalid
    /// name keeps the neutral resources: the reporter must never fail to appear
    /// because of a field it cannot resolve.
    /// </summary>
    public static void ApplyCulture(string? cultureName)
    {
        if (string.IsNullOrWhiteSpace(cultureName))
        {
            return;
        }

        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }
        catch (CultureNotFoundException)
        {
            // The neutral English resources remain available when the captured culture is invalid.
        }
    }

    /// <summary>Report shown when no snapshot could be read from disk.</summary>
    public static CrashReport CreateUnreadableReport() => new()
    {
        Id = "CR-UNAVAILABLE",
        OccurredAt = DateTimeOffset.Now,
        Source = "CrashReportMode",
        AppVersion = BuildInfo.LauncherVersion,
        BuildSha = BuildInfo.CommitSha,
        OperatingSystem = Environment.OSVersion.ToString(),
        UiCulture = CultureInfo.CurrentUICulture.Name,
        ExceptionType = nameof(InvalidOperationException),
        TechnicalDetails = "The crash snapshot could not be read. Open the log directory for any diagnostics that were saved."
    };
}
