namespace Cafe.Launcher.Avalonia.Services.Update;

/// <summary>
/// Why this host can — or cannot — apply an offered launcher release itself. The judgement
/// reports the reason instead of a boolean so every call site has to state what a negative
/// answer means: the dialog names the cause (the platform, this installation, or the release),
/// and the download gate keeps its own precondition. Same rule the game-operation policy
/// follows (ADR-027).
/// </summary>
public enum LauncherUpdateInAppAvailability
{
    /// <summary>This host has an in-app path: a verifiable package for its install kind, plus the helper.</summary>
    Available,

    /// <summary>
    /// Not Windows x64, so the release assets do not target this host at all
    /// (macOS, Linux, and Windows on other architectures).
    /// </summary>
    PlatformUnsupported,

    /// <summary>
    /// The release offers nothing this host could download and verify: no package for this
    /// install kind, or no <c>SHA256SUMS</c> to check it against. The launcher never applies
    /// unverified bits, so this is a refusal rather than a limitation.
    /// </summary>
    PackageUnverifiable,

    /// <summary>This installation does not carry the Windows update helper that applies the package.</summary>
    HelperMissing
}
