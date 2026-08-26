using Cafe.Launcher.Avalonia.Features.GameOperations;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Tests;

/// <summary>Test double for <see cref="IGameShortcutService"/> that records invocations.</summary>
internal sealed class TestGameShortcutService : IGameShortcutService
{
    public GameShortcutResult CreationResult { get; set; } = new(GameShortcutStatus.Created);

    public bool FolderOpened { get; private set; }

    public LauncherStatusSnapshot? LastSnapshot { get; private set; }

    public Func<string, bool>? OpenDirectory { get; set; }

    public Task<GameShortcutResult> CreateDesktopShortcutAsync(LauncherStatusSnapshot snapshot)
    {
        LastSnapshot = snapshot;
        return Task.FromResult(CreationResult);
    }

    public bool TryOpenGameFolder(LauncherStatusSnapshot snapshot)
    {
        LastSnapshot = snapshot;
        if (OpenDirectory is not null)
        {
            FolderOpened = OpenDirectory(snapshot.LocalGame.GamePath);
            return FolderOpened;
        }

        FolderOpened = true;
        return true;
    }
}
