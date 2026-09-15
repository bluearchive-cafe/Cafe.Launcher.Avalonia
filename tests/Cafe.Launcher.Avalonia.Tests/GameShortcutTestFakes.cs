using Cafe.Launcher.Avalonia.Features.GameOperations;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Tests;

/// <summary>Test double for <see cref="IGameShortcutService"/> that records invocations.</summary>
internal sealed class TestGameShortcutService : IGameShortcutService
{
    public GameShortcutResult CreationResult { get; set; } = new(GameShortcutStatus.Created);

    /// <summary>删除路径的返回结果；默认「桌面本来就没有这个快捷方式」。</summary>
    public GameShortcutResult DeletionResult { get; set; } = new(GameShortcutStatus.NotFound);

    /// <summary>删除被调用的次数（卸载必须调用一次，ADR-030）。</summary>
    public int DeleteCallCount { get; private set; }

    public bool FolderOpened { get; private set; }

    public LauncherStatusSnapshot? LastSnapshot { get; private set; }

    public Func<string, bool>? OpenDirectory { get; set; }

    public Task<GameShortcutResult> CreateDesktopShortcutAsync(LauncherStatusSnapshot snapshot)
    {
        LastSnapshot = snapshot;
        return Task.FromResult(CreationResult);
    }

    public Task<GameShortcutResult> DeleteDesktopShortcutAsync(LauncherStatusSnapshot snapshot)
    {
        LastSnapshot = snapshot;
        DeleteCallCount++;
        return Task.FromResult(DeletionResult);
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
