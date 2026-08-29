using System;

namespace Cafe.Launcher.Avalonia.Services.GameRuntime;

/// <summary>Outcome of a tracked game process that has exited.</summary>
public sealed record GameLaunchExitInfo(
    int ExitCode,
    TimeSpan Duration,
    DateTimeOffset ExitedAt,
    string RunnerId);
