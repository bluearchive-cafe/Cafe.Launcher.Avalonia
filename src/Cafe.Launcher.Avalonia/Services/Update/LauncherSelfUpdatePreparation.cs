namespace Cafe.Launcher.Avalonia.Services.Update;

/// <summary>Outcome of preparing a launcher self-update.</summary>
public enum LauncherSelfUpdatePreparationStatus
{
    /// <summary>A verified package is staged and ready to apply.</summary>
    Ready,

    /// <summary>No in-app apply path: the caller should open the download page.</summary>
    ExternalDownload,

    /// <summary>An in-app path existed but the package could not be fetched or verified.</summary>
    Failed
}

/// <summary>
/// The result of preparing a self-update: either a verified staged package for the
/// helper to apply, a browser hand-off, or a failure with diagnostic detail.
/// </summary>
public sealed record LauncherSelfUpdatePreparation(
    LauncherSelfUpdatePreparationStatus Status,
    LauncherUpdateTarget Target,
    string? PackagePath,
    string ExpectedSha256,
    string FailureMessage)
{
    public static LauncherSelfUpdatePreparation External() =>
        new(LauncherSelfUpdatePreparationStatus.ExternalDownload, LauncherUpdateTarget.ExternalDownload, null, "", "");

    public static LauncherSelfUpdatePreparation Ready(
        LauncherUpdateTarget target,
        string packagePath,
        string expectedSha256) =>
        new(LauncherSelfUpdatePreparationStatus.Ready, target, packagePath, expectedSha256, "");

    public static LauncherSelfUpdatePreparation Failed(string failureMessage) =>
        new(LauncherSelfUpdatePreparationStatus.Failed, LauncherUpdateTarget.ExternalDownload, null, "", failureMessage);
}
