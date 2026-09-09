using System;
using System.Globalization;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Resources;
using Cafe.Launcher.Avalonia.Services.Diagnostics;

namespace Cafe.Launcher.Avalonia.ViewModels;

/// <summary>Presentation model for the independent, terminal crash-report window.</summary>
public sealed class CrashReportWindowViewModel
{
    private readonly CrashReport report;

    public CrashReportWindowViewModel(CrashReport report)
    {
        this.report = report;
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
    public string LogDirectory => Services.LauncherUserDataDirectory.Root;

    private static string T(string key) =>
        LauncherStrings.ResourceManager.GetString(key, CultureInfo.CurrentUICulture)
        ?? LauncherStrings.ResourceManager.GetString(key, CultureInfo.GetCultureInfo("en"))
        ?? key;
}
