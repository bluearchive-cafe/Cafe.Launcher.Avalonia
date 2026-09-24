using System;

namespace Cafe.Launcher.Avalonia.Services;

/// <summary>Shell-owned actions and availability for the native tray. Call actions on the UI thread.</summary>
public interface ISystemTrayActions
{
    /// <summary>Raised when menu availability or game session presentation changes; may run off the UI thread.</summary>
    event Action? Changed;

    /// <summary>Whether the existing launch flow can be entered from the tray.</summary>
    bool CanStartGame { get; }

    /// <summary>Whether the launcher is waiting for the game's process family.</summary>
    bool IsGameStarting { get; }

    /// <summary>Whether the monitored game session is running.</summary>
    bool IsGameRunning { get; }

    /// <summary>Whether settings can be opened without bypassing a modal surface.</summary>
    bool CanOpenSettings { get; }

    /// <summary>Rechecks availability and invokes the existing game launch command.</summary>
    void StartGame();

    /// <summary>Opens settings without toggling an already visible editor or discarding its draft.</summary>
    void OpenSettings();
}
