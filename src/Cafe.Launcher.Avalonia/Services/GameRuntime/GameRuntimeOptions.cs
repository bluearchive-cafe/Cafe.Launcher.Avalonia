namespace Cafe.Launcher.Avalonia.Services.GameRuntime;

/// <summary>
/// Per-launch runtime options handed to an <see cref="IGameRunner"/>.
/// All paths are optional: runners fall back to their platform defaults
/// (umu-run on PATH, a launcher-managed prefix under the XDG data home).
/// Settings UI wiring for these values lands in a later phase.
/// </summary>
public sealed record GameRuntimeOptions(
    string? RunnerPath = null,
    string? PrefixPath = null,
    string? ProtonPath = null);
