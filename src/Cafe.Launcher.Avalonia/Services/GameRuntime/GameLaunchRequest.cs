using System.Collections.Generic;

namespace Cafe.Launcher.Avalonia.Services.GameRuntime;

/// <summary>
/// Platform-agnostic description of a single game launch: what to start,
/// where, and with which arguments. Carries no compatibility-layer details
/// (Proton, Wine, prefix, bottle) — those belong to <see cref="IGameRunner"/> implementations.
/// </summary>
public sealed record GameLaunchRequest(
    string GameId,
    string ExecutablePath,
    string WorkingDirectory,
    IReadOnlyList<string> Arguments);
