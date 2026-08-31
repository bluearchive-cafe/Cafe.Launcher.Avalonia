namespace Cafe.Launcher.Avalonia.Services.GameRuntime;

/// <summary>
/// Per-launch runtime options handed to an <see cref="IGameRunner"/>.
/// All paths are optional: runners fall back to their platform defaults
/// (umu-run on PATH, a launcher-managed prefix under the XDG data home).
/// A custom runner path applies only to a manually selected runner; auto mode
/// discovers each candidate independently.
/// </summary>
public sealed record GameRuntimeOptions(
    string? RunnerPath = null,
    string? PrefixPath = null,
    string? ProtonPath = null)
{
    /// <summary>
    /// A single custom runner path is meaningful only when the user explicitly
    /// selected that runner. Auto mode must discover each candidate independently;
    /// otherwise a Wine executable can satisfy UMU's generic version probe (or vice
    /// versa) and cause the resolver to report the wrong runtime.
    /// </summary>
    public GameRuntimeOptions ForRunnerSelection(string? preferredRunnerId) =>
        string.IsNullOrWhiteSpace(preferredRunnerId)
            ? this with { RunnerPath = null }
            : this;

    /// <summary>Applies the custom path only to the manually selected runner.</summary>
    public GameRuntimeOptions ForStatusCheck(string? preferredRunnerId, string runnerId) =>
        !string.IsNullOrWhiteSpace(preferredRunnerId)
        && string.Equals(preferredRunnerId, runnerId, System.StringComparison.OrdinalIgnoreCase)
            ? this
            : this with { RunnerPath = null };
}
