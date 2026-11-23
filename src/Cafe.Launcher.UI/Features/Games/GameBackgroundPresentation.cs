using Cafe.Launcher.Core.Models;

namespace Cafe.Launcher.UI.Features.Games;

/// <summary>Resolves background inheritance into an isolated render snapshot, never into the settings draft.</summary>
internal static class GameBackgroundPresentation
{
    public static LauncherSettings Resolve(LauncherSettings settings, string gameId, bool previewGlobal = false)
    {
        var resolved = settings.DeepClone();
        resolved.LastViewedGameId = gameId;
        var game = resolved.GetGameSettings(gameId);
        if (!previewGlobal && game.UseBackgroundOverride)
        {
            resolved.BackgroundSource = game.BackgroundSource;
            resolved.CustomBackgroundPath = game.CustomBackgroundPath;
            resolved.BackgroundFit = game.BackgroundFit;
            resolved.BackgroundFillColor = game.BackgroundFillColor;
            resolved.BackgroundVideoVolume = game.BackgroundVideoVolume;
            resolved.BackgroundVideoMuted = game.BackgroundVideoMuted;
        }

        return resolved;
    }
}
