using System;
using System.Globalization;
using Cafe.Launcher.UI.Constants;
using Cafe.Launcher.UI.Resources;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.Core.Services.Diagnostics;
using Cafe.Launcher.UI.Services.Diagnostics;
using Cafe.Launcher.Core.Services;

namespace Cafe.Launcher.UI.ViewModels;

/// <summary>Presentation model for the independent, terminal crash-report window.</summary>
internal sealed class CrashReportWindowViewModel
{
    private readonly CrashReport report;
    private readonly LauncherDataRoot dataRoot;

    public CrashReportWindowViewModel(CrashReport report, LauncherDataRoot dataRoot)
    {
        ArgumentNullException.ThrowIfNull(dataRoot);
        this.report = report;
        this.dataRoot = dataRoot;
    }

    public string WindowTitle => T(LocalizationKeys.CrashWindowCaption);

    public string Status => T(LocalizationKeys.CrashWindowStatus);

    public string Title => T(LocalizationKeys.CrashWindowTitle);

    public string Description => T(LocalizationKeys.CrashWindowDescription);

    public string ReportIdLabel => T(LocalizationKeys.CrashWindowReportId);

    public string ReportId => report.Id;

    public string TimeLabel => T(LocalizationKeys.CrashWindowTime);

    public string Time => report.OccurredAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    public string VersionLabel => T(LocalizationKeys.CrashWindowVersion);

    public string Version => report.AppVersion;

    public string BuildLabel => T(LocalizationKeys.CrashWindowBuild);

    public string Build => report.BuildSha;

    public string TechnicalDetailsLabel => T(LocalizationKeys.CrashWindowTechnicalDetails);

    public string TechnicalDetails => report.TechnicalDetails;

    public string OpenLogsText => T(LocalizationKeys.CrashWindowOpenLogs);

    public string CopyDetailsText => T(LocalizationKeys.CrashWindowCopyDetails);

    public string CopiedText => T(LocalizationKeys.CrashWindowCopied);

    public string ExitText => T(LocalizationKeys.CrashWindowExit);

    /// <summary>Directory the "open log folder" action reveals: the launcher log lives there.</summary>
    public string LogDirectory => dataRoot.Root;

    private static string T(string key) =>
        LauncherStrings.ResourceManager.GetString(key, CultureInfo.CurrentUICulture)
        ?? LauncherStrings.ResourceManager.GetString(key, CultureInfo.GetCultureInfo("en"))
        ?? key;
}
