namespace Cafe.Launcher.Avalonia.Services.GameRuntime;

/// <summary>
/// Distinguishes "this runner supports the current platform" from
/// "the runner's runtime environment is actually installed" — a runner can
/// be platform-supported yet unavailable because umu-run, Wine, etc. are missing.
/// </summary>
public sealed record GameRunnerAvailability(
    bool Available,
    string? Version = null,
    string? ExecutablePath = null,
    string? Message = null);
