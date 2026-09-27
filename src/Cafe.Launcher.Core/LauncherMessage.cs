using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Cafe.Launcher.Core;

/// <summary>
/// A presentation-neutral outcome that can be localized by a consuming surface.
/// The code is stable application vocabulary; the arguments are format values, and
/// diagnostic detail is intentionally kept separate from user-visible text.
/// </summary>
public sealed record LauncherMessage(
    LauncherMessageCode Code,
    IReadOnlyList<string> Arguments,
    string? DiagnosticDetail = null)
{
    public static LauncherMessage Create(
        LauncherMessageCode code,
        string? diagnosticDetail = null,
        params string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return new LauncherMessage(
            code,
            new ReadOnlyCollection<string>(arguments),
            diagnosticDetail);
    }
}

/// <summary>
/// Stable message vocabulary emitted by Core. New codes require a corresponding
/// mapping in each presentation layer before a user-visible path may return it.
/// </summary>
public enum LauncherMessageCode
{
    None = 0,
    OperationNotAllowed,
    GameIsRunning,
    GameNotInstalled,
    NetworkRequestFailed,
    ManifestInvalid,
    DownloadFailed,
    VerificationFailed,
    UninstallCompleted,
    UpdateUnavailable,
    UnexpectedFailure
}
