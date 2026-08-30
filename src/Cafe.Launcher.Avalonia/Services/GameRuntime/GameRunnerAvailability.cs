namespace Cafe.Launcher.Avalonia.Services.GameRuntime;

/// <summary>
/// Distinguishes why a runner's runtime environment is or is not usable:
/// the platform may be unsupported outright, the executable may be missing,
/// or the executable may exist but fail its health probe (broken install).
/// </summary>
public enum GameRunnerAvailabilityStatus
{
    /// <summary>The runner cannot operate on the current operating system at all.</summary>
    Unsupported,

    /// <summary>The runner's runtime executable was not found.</summary>
    NotFound,

    /// <summary>The runner's runtime executable exists but failed its version probe.</summary>
    Broken,

    /// <summary>The runtime environment is installed and responds to its version probe.</summary>
    Available
}

/// <summary>
/// Evidence collected while checking a runner's runtime environment. A runner can
/// be platform-supported yet unavailable because umu-run, Wine, etc. are missing
/// or broken.
/// </summary>
public sealed record GameRunnerAvailability(
    GameRunnerAvailabilityStatus Status,
    string? Version = null,
    string? ExecutablePath = null,
    string? Message = null,
    string? TechnicalDetail = null)
{
    public bool Available => Status == GameRunnerAvailabilityStatus.Available;
}
