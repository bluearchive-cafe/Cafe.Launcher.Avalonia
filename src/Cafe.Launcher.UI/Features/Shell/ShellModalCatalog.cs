using System.Collections.Generic;
using Cafe.Launcher.UI.Features.Diagnostics;
using Cafe.Launcher.UI.Features.GameOperations;
using Cafe.Launcher.UI.Features.ResourcePanel;
using Cafe.Launcher.UI.Features.Settings;
using Cafe.Launcher.UI.ViewModels;

namespace Cafe.Launcher.UI.Features.Shell;

/// <summary>
/// 主窗口全部模态的注册记录，按栈序排列（ADR-023）。顺序即注册序：多处旗标
/// 同时翻转时，栈序依赖注册序；调整列表前先读 ADR-023 的显示语义条款。
/// 每条记录只描述「种类、旗标来源、栈内容、ESC 命令」，不持有生命周期行为。
/// </summary>
internal static class ShellModalCatalog
{
    public static IReadOnlyList<ModalRegistration> Create(ShellPresentationFamily family)
    {
        var dialogs = family.Dialogs;
        var operations = family.Operations;

        return
        [
            new ModalRegistration(
                ModalKind.Settings,
                family.WindowChrome,
                nameof(WindowChromeViewModel.IsSettingsVisible),
                () => family.WindowChrome.IsSettingsVisible,
                family.Settings,
                family.WindowChrome.ShowSettingsCommand),
            new ModalRegistration(
                ModalKind.UnsavedSettingsConfirmation,
                family.Settings,
                nameof(SettingsViewModel.IsUnsavedChangesVisible),
                () => family.Settings.IsUnsavedChangesVisible,
                family.Settings,
                family.WindowChrome.KeepEditingSettingsCommand),
            new ModalRegistration(
                ModalKind.ResourcePanel,
                family.ResourcePanel,
                nameof(ResourcePanelViewModel.IsResourcePanelVisible),
                () => family.ResourcePanel.IsResourcePanelVisible,
                family.ResourcePanel,
                family.ResourcePanel.CloseResourcePanelCommand),
            new ModalRegistration(
                ModalKind.LogViewer,
                family.LogViewer,
                nameof(LogViewerDialogViewModel.IsVisible),
                () => family.LogViewer.IsVisible,
                family.LogViewer,
                family.LogViewer.CloseCommand),
            new ModalRegistration(
                ModalKind.LogExport,
                family.LogExport,
                nameof(LogExportDialogViewModel.IsVisible),
                () => family.LogExport.IsVisible,
                family.LogExport,
                family.LogExport.CloseCommand),
            new ModalRegistration(
                ModalKind.Debug,
                family.Debug,
                nameof(DebugViewModel.IsVisible),
                () => family.Debug.IsVisible,
                family.Debug,
                family.Debug.CloseCommand),
            new ModalRegistration(
                ModalKind.DesignGallery,
                dialogs.Gallery,
                nameof(DesignGalleryViewModel.IsVisible),
                () => dialogs.Gallery.IsVisible,
                dialogs.Gallery,
                dialogs.Gallery.CloseCommand),
            new ModalRegistration(
                ModalKind.Notice,
                dialogs,
                nameof(DialogsViewModel.IsNoticeDialogVisible),
                () => dialogs.IsNoticeDialogVisible,
                dialogs,
                dialogs.DismissNoticeCommand),
            new ModalRegistration(
                ModalKind.Update,
                dialogs,
                nameof(DialogsViewModel.IsUpdateAvailableVisible),
                () => dialogs.IsUpdateAvailableVisible,
                dialogs,
                dialogs.CancelUpdateAvailableCommand),
            new ModalRegistration(
                ModalKind.Error,
                dialogs,
                nameof(DialogsViewModel.IsErrorDialogVisible),
                () => dialogs.IsErrorDialogVisible,
                dialogs,
                dialogs.ContinueAfterErrorCommand),
            new ModalRegistration(
                ModalKind.SetupWizard,
                dialogs,
                nameof(DialogsViewModel.IsSetupWizardVisible),
                () => dialogs.IsSetupWizardVisible,
                dialogs.SetupWizard,
                dialogs.ShowSetupWizardExitConfirmCommand),
            new ModalRegistration(
                ModalKind.ResourcePanelDiscardConfirmation,
                family.ResourcePanel.DiscardChangesConfirm,
                nameof(ConfirmationDialogViewModel.IsVisible),
                () => family.ResourcePanel.DiscardChangesConfirm.IsVisible,
                family.ResourcePanel,
                family.ResourcePanel.CancelDiscardChangesCommand),
            new ModalRegistration(
                ModalKind.GameTask,
                operations,
                nameof(GameOperationsViewModel.IsTaskDialogVisible),
                () => operations.IsTaskDialogVisible,
                operations,
                operations.CloseTaskCommand),
            Confirmation(ModalKind.StopConfirmation, dialogs.StopConfirm, dialogs),
            Confirmation(ModalKind.DownloadRunningCloseConfirmation, dialogs.DownloadRunningCloseConfirm, dialogs),
            new ModalRegistration(
                ModalKind.UninstallConfirmation,
                operations.Uninstall,
                nameof(UninstallDialogViewModel.IsVisible),
                () => operations.Uninstall.IsVisible,
                operations.Uninstall,
                operations.Uninstall.CloseCommand),
            Confirmation(ModalKind.RepairConfirmation, dialogs.RepairConfirm, dialogs),
            Confirmation(ModalKind.ResourcePanelSourceConfirmation, dialogs.ResourcePanelSourceConfirm, dialogs),
            Confirmation(ModalKind.DebugResetConfirmation, dialogs.DebugResetConfirm, dialogs),
            Confirmation(ModalKind.SettingsResetConfirmation, dialogs.SettingsResetConfirm, dialogs),
            Confirmation(ModalKind.SetupWizardExitConfirmation, dialogs.SetupWizardExitConfirm, dialogs)
        ];
    }

    private static ModalRegistration Confirmation(
        ModalKind kind,
        ConfirmationDialogViewModel confirmation,
        DialogsViewModel owner) =>
        new(
            kind,
            confirmation,
            nameof(ConfirmationDialogViewModel.IsVisible),
            () => confirmation.IsVisible,
            owner,
            confirmation.CancelCommand);
}
