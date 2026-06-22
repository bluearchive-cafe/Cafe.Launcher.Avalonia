using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class WindowEscapeStrategyTests
{
    [Theory]
    [MemberData(nameof(SingleVisibleStates))]
    public void ResolveEscape_WhenOneLayerIsVisible_ReturnsMatchingAction(
        WindowInteractionState state,
        WindowEscapeAction expected)
    {
        Assert.Equal(expected, WindowEscapeStrategy.ResolveEscape(state));
    }

    [Fact]
    public void ResolveEscape_WhenMultipleLayersAreVisible_UsesDeclaredPriority()
    {
        var state = new WindowInteractionState
        {
            IsMigrationVisible = true,
            IsDownloadRunningCloseConfirmVisible = true,
            IsStopConfirmVisible = true,
            IsUnsavedChangesVisible = true,
            IsRepairConfirmVisible = true,
            IsResourcePanelSourceConfirmVisible = true,
            IsUninstallConfirmVisible = true,
            IsNoticeDialogVisible = true,
            IsSettingsVisible = true,
            IsResourcePanelVisible = true
        };

        Assert.Equal(
            WindowEscapeAction.SkipMigration,
            WindowEscapeStrategy.ResolveEscape(state));
    }

    [Fact]
    public void ResolveEscape_WhenNoLayerIsVisible_ReturnsNull()
    {
        Assert.Null(WindowEscapeStrategy.ResolveEscape(new WindowInteractionState()));
    }

    public static TheoryData<WindowInteractionState, WindowEscapeAction> SingleVisibleStates =>
        new()
        {
            {
                new WindowInteractionState { IsMigrationVisible = true },
                WindowEscapeAction.SkipMigration
            },
            {
                new WindowInteractionState { IsDownloadRunningCloseConfirmVisible = true },
                WindowEscapeAction.CancelCloseWhileDownloading
            },
            {
                new WindowInteractionState { IsStopConfirmVisible = true },
                WindowEscapeAction.CancelStop
            },
            {
                new WindowInteractionState { IsUnsavedChangesVisible = true },
                WindowEscapeAction.KeepEditingSettings
            },
            {
                new WindowInteractionState { IsRepairConfirmVisible = true },
                WindowEscapeAction.CancelRepair
            },
            {
                new WindowInteractionState { IsResourcePanelSourceConfirmVisible = true },
                WindowEscapeAction.CancelResourcePanelSourceSwitch
            },
            {
                new WindowInteractionState { IsUninstallConfirmVisible = true },
                WindowEscapeAction.CancelUninstall
            },
            {
                new WindowInteractionState { IsNoticeDialogVisible = true },
                WindowEscapeAction.DismissNotice
            },
            {
                new WindowInteractionState { IsSettingsVisible = true },
                WindowEscapeAction.ToggleSettings
            },
            {
                new WindowInteractionState { IsResourcePanelVisible = true },
                WindowEscapeAction.CloseResourcePanel
            }
        };
}
