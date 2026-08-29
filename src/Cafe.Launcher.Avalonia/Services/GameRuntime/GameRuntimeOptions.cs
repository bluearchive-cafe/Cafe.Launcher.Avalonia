namespace Cafe.Launcher.Avalonia.Services.GameRuntime;

/// <summary>
/// Per-launch runtime options handed to an <see cref="IGameRunner"/>.
/// Intentionally empty in Phase 1; extended in later phases with prefix,
/// Proton path, and custom environment variables.
/// </summary>
public sealed record GameRuntimeOptions();
