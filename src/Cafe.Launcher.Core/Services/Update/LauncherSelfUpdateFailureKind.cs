namespace Cafe.Launcher.Core.Services.Update;

/// <summary>
/// Why an in-app self-update preparation failed. Presentation only needs to act on
/// <see cref="UrlRejected"/>: that failure names a cause the user can fix (hosts entry or
/// DNS hijack), while the rest share the generic download-failure attribution.
/// </summary>
public enum LauncherSelfUpdateFailureKind
{
    /// <summary>No failure: the preparation is not in the failed state.</summary>
    None = 0,

    /// <summary>No usable answer was produced (connection failure, timeout).</summary>
    Network,

    /// <summary>The staged file could not be written.</summary>
    Io,

    /// <summary>The downloaded package or its checksum manifest did not verify.</summary>
    Verification,

    /// <summary>
    /// The target was refused because the OS resolver answered with a local or private
    /// address (a hosts entry or DNS hijack pointing at 127.0.0.1).
    /// </summary>
    UrlRejected
}
