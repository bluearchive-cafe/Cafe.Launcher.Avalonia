namespace Cafe.Launcher.Avalonia.Services.GameRuntime;

/// <summary>
/// Stable per-game runtime identities used for UMU GAMEID values and
/// launcher-managed compatibility state (prefix layout). Deliberately decoupled
/// from the game executable name: if an EXE is renamed, existing compatibility
/// state must not be orphaned under a new id.
/// </summary>
public static class GameRuntimeIds
{
    public const string BlueArchiveJapan = "blue-archive-jp";
}
