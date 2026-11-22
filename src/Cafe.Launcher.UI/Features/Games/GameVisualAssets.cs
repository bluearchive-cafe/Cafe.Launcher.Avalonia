using Cafe.Launcher.Core.Constants;

namespace Cafe.Launcher.UI.Features.Games;

/// <summary>UI-owned resources keyed by stable game identity; Core never resolves Avalonia assets.</summary>
internal static class GameVisualAssets
{
    public const string FallbackBackground = "avares://Cafe.Launcher.UI/Assets/launcher-background-fallback.png";

    public static string Background(string gameId) => gameId == LauncherProfiles.StellaSoraChina.RuntimeId
        ? "avares://Cafe.Launcher.UI/Assets/launcher-background-stella-sora.png"
        : gameId == LauncherProfiles.BlueArchiveJapan.RuntimeId
            ? "avares://Cafe.Launcher.UI/Assets/launcher-background.png" : FallbackBackground;

    public static string Icon(string gameId) => gameId == LauncherProfiles.StellaSoraChina.RuntimeId
        ? "avares://Cafe.Launcher.UI/Assets/game-stella-sora.png"
        : "avares://Cafe.Launcher.UI/Assets/game-blue-archive.png";
}
