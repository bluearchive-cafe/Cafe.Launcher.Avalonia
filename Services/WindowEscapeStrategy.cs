namespace Cafe.Launcher.Avalonia.Services;

/// <summary>
/// Actions that can be triggered by pressing Escape in the main window.
/// Priority order is defined by <see cref="WindowEscapeStrategy.ResolveEscape"/>.
/// </summary>
public enum WindowEscapeAction
{
    SkipMigration,
    CancelCloseWhileDownloading,
    CancelStop,
    KeepEditingSettings,
    CancelRepair,
    CancelResourcePanelSourceSwitch,
    CancelUninstall,
    DismissNotice,
    ToggleSettings,
    CloseResourcePanel,
}

/// <summary>
/// Snapshot of all modal/panel states relevant to Escape key resolution.
/// </summary>
public sealed class WindowInteractionState
{
    public bool IsMigrationVisible { get; init; }
    public bool IsDownloadRunningCloseConfirmVisible { get; init; }
    public bool IsStopConfirmVisible { get; init; }
    public bool IsUnsavedChangesVisible { get; init; }
    public bool IsRepairConfirmVisible { get; init; }
    public bool IsResourcePanelSourceConfirmVisible { get; init; }
    public bool IsUninstallConfirmVisible { get; init; }
    public bool IsNoticeDialogVisible { get; init; }
    public bool IsSettingsVisible { get; init; }
    public bool IsResourcePanelVisible { get; init; }
}

/// <summary>
/// Pure strategy for main window interaction decisions.
/// Encapsulates the Escape key priority resolution — most-nested UI first.
/// This is a deep module: removing it forces every overlay to re-implement
/// the same priority ordering independently.
/// </summary>
public static class WindowEscapeStrategy
{
    /// <summary>
    /// Resolve what action Escape should trigger based on the current UI state.
    /// Priority: migration wizard → confirmation dialogs → settings overlay → resource panel.
    /// Returns null when no modal or panel is visible (Escape has no effect).
    /// </summary>
    public static WindowEscapeAction? ResolveEscape(WindowInteractionState state)
    {
        // Priority order — most-nested (highest Z-index) first.
        if (state.IsMigrationVisible) return WindowEscapeAction.SkipMigration;
        if (state.IsDownloadRunningCloseConfirmVisible) return WindowEscapeAction.CancelCloseWhileDownloading;
        if (state.IsStopConfirmVisible) return WindowEscapeAction.CancelStop;
        if (state.IsUnsavedChangesVisible) return WindowEscapeAction.KeepEditingSettings;
        if (state.IsRepairConfirmVisible) return WindowEscapeAction.CancelRepair;
        if (state.IsResourcePanelSourceConfirmVisible) return WindowEscapeAction.CancelResourcePanelSourceSwitch;
        if (state.IsUninstallConfirmVisible) return WindowEscapeAction.CancelUninstall;
        if (state.IsNoticeDialogVisible) return WindowEscapeAction.DismissNotice;
        if (state.IsSettingsVisible) return WindowEscapeAction.ToggleSettings;
        if (state.IsResourcePanelVisible) return WindowEscapeAction.CloseResourcePanel;
        return null;
    }
}
