namespace Cafe.Launcher.Avalonia.Services.GameRuntime;

/// <summary>
/// Per-launch runtime options handed to <see cref="GameRuntime"/>.
/// All paths are optional: runners fall back to their platform defaults
/// (umu-run on PATH, a launcher-managed prefix under the XDG data home).
/// A custom runner path applies only to a manually selected runner; the
/// <see cref="GameRuntime"/> module enforces that rule for both launch and
/// status paths.
/// </summary>
public sealed record GameRuntimeOptions(
    string? RunnerPath = null,
    string? PrefixPath = null,
    string? ProtonPath = null);
