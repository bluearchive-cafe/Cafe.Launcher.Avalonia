namespace Cafe.Launcher.Avalonia.Services.Update;

/// <summary>How a launcher update package download terminated.</summary>
public enum LauncherUpdateDownloadStatus
{
    /// <summary>The package was transferred and its SHA-256 matched.</summary>
    Succeeded,

    /// <summary>The package was transferred but its SHA-256 did not match the manifest.</summary>
    ChecksumMismatch,

    /// <summary>No usable answer was produced (connection failure, timeout, rejected URL).</summary>
    NetworkFailure,

    /// <summary>The destination could not be written.</summary>
    IoFailure,

    /// <summary>The caller cancelled.</summary>
    Cancelled
}

/// <summary>
/// Explicit result of a launcher update download. There is no null contract: every
/// termination maps to one status, and <see cref="FailureMessage"/> is diagnostic
/// detail for the log, never user-facing text.
/// </summary>
public sealed record LauncherUpdateDownloadResult(
    LauncherUpdateDownloadStatus Status,
    string? FilePath,
    string FailureMessage)
{
    public bool IsSuccess => Status == LauncherUpdateDownloadStatus.Succeeded;

    public static LauncherUpdateDownloadResult Succeeded(string filePath) =>
        new(LauncherUpdateDownloadStatus.Succeeded, filePath, "");

    public static LauncherUpdateDownloadResult Failed(
        LauncherUpdateDownloadStatus status,
        string failureMessage) =>
        new(status, FilePath: null, failureMessage);
}
