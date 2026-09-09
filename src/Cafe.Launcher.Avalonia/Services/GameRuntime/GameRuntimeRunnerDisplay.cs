using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Services.GameRuntime;

/// <summary>
/// Localized presentation of runner identity and availability status. Shared by the
/// settings runtime-status list and launch-failure reporting so both surfaces name
/// the same runner the same way, and so a runner rename only has to be localized once.
/// </summary>
internal static class GameRuntimeRunnerDisplay
{
    /// <summary>
    /// Localized runner name for <paramref name="runnerId"/>. Unknown ids are shown
    /// verbatim (a hand-edited settings file must not render as an empty name), and an
    /// absent id falls back to the auto-selection label.
    /// </summary>
    public static string RunnerName(LocalizationService localizer, string? runnerId) =>
        runnerId switch
        {
            GameRuntimeRunners.Umu => localizer.T(LocalizationKeys.GameRuntimeRunnerUmu),
            GameRuntimeRunners.Wine => localizer.T(LocalizationKeys.GameRuntimeRunnerWine),
            GameRuntimeRunners.Native => localizer.T(LocalizationKeys.GameRuntimeRunnerNative),
            GameRuntimeRunners.Auto => localizer.T(LocalizationKeys.GameRuntimeRunnerAuto),
            _ => string.IsNullOrWhiteSpace(runnerId)
                ? localizer.T(LocalizationKeys.GameRuntimeRunnerAuto)
                : runnerId
        };

    /// <summary>
    /// Localized availability status. Unknown statuses report as unsupported, matching
    /// the settings status list (the only way to reach a runner the platform cannot host).
    /// </summary>
    public static string Status(LocalizationService localizer, GameRunnerAvailabilityStatus status) =>
        status switch
        {
            GameRunnerAvailabilityStatus.Available => localizer.T(LocalizationKeys.GameRuntimeStatusAvailable),
            GameRunnerAvailabilityStatus.NotFound => localizer.T(LocalizationKeys.GameRuntimeStatusNotFound),
            GameRunnerAvailabilityStatus.Broken => localizer.T(LocalizationKeys.GameRuntimeStatusBroken),
            _ => localizer.T(LocalizationKeys.GameRuntimeStatusUnsupported)
        };
}
