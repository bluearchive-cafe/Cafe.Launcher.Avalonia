using Cafe.Launcher.Avalonia.ViewModels;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Tests;

public partial class MainWindowViewModelTests
{
    [Fact]
    public async Task SetupWizardLanguage_WhenChanged_AppliesLanguageImmediately()
    {
        var coreService = new CountingCoreService(CreateSnapshot());
        using var viewModel = await CreateViewModelAsync(coreService);
        viewModel.Dialogs.ShowSetupWizard();

        viewModel.Dialogs.SetupWizard.Language = LauncherLanguages.Japanese;

        Assert.Equal("言語", viewModel.Shell.I18n["setupWizardLanguage"]);
    }

    [Fact]
    public async Task SetupWizardLanguage_WhenWizardIsHidden_DoesNotPreviewLanguage()
    {
        var coreService = new CountingCoreService(CreateSnapshot());
        using var viewModel = await CreateViewModelAsync(coreService);
        var originalTitle = viewModel.Shell.I18n["setupWizardLanguage"];

        viewModel.Dialogs.SetupWizard.Language = LauncherLanguages.Japanese;

        Assert.Equal(originalTitle, viewModel.Shell.I18n["setupWizardLanguage"]);
    }

    public static TheoryData<ModalKind, ModalKind?> EscapeExpectations()
    {
        var data = new TheoryData<ModalKind, ModalKind?>();
        foreach (var kind in Enum.GetValues<ModalKind>())
        {
            // The setup wizard confirms the exit instead of closing directly.
            data.Add(
                kind,
                kind == ModalKind.SetupWizard ? ModalKind.SetupWizardExitConfirmation : null);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EscapeExpectations))]
    public async Task TryHandleEscape_ForEveryModalKind_IsRoutedToThatModal(
        ModalKind kind,
        ModalKind? expectedTopAfterEscape)
    {
        using var viewModel = await CreateViewModelAsync(new CountingCoreService(CreateSnapshot()));

        await OpenModalAsync(viewModel, kind);

        Assert.Equal(kind, viewModel.ModalHost.Top?.Kind);
        Assert.True(viewModel.TryHandleEscape());
        Assert.Equal(expectedTopAfterEscape, viewModel.ModalHost.Top?.Kind);
    }

    [Fact]
    public async Task TryHandleEscape_WhenSettingsOpenUnderConfirmation_ClosesConfirmationFirst()
    {
        using var viewModel = await CreateViewModelAsync(new CountingCoreService(CreateSnapshot()));

        viewModel.WindowChrome.IsSettingsVisible = true;
        viewModel.Dialogs.ShowRepairConfirm("repair");
        Assert.Equal(ModalKind.RepairConfirmation, viewModel.ModalHost.Top?.Kind);

        Assert.True(viewModel.TryHandleEscape());
        Assert.False(viewModel.Dialogs.IsRepairConfirmVisible);
        Assert.True(viewModel.WindowChrome.IsSettingsVisible);

        Assert.True(viewModel.TryHandleEscape());
        Assert.False(viewModel.WindowChrome.IsSettingsVisible);
    }

    /// <summary>
    /// Opens the modal surface for <paramref name="kind"/>. A new <see cref="ModalKind"/>
    /// fails the default branch, forcing this switch to be extended together with the
    /// Escape routing it guards.
    /// </summary>
    private static async Task OpenModalAsync(MainWindowViewModel viewModel, ModalKind kind)
    {
        switch (kind)
        {
            case ModalKind.Settings:
                viewModel.WindowChrome.IsSettingsVisible = true;
                break;
            case ModalKind.ResourcePanel:
                viewModel.ResourcePanel.IsResourcePanelVisible = true;
                break;
            case ModalKind.LogViewer:
                viewModel.LogViewer.OpenCommand.Execute(null);
                break;
            case ModalKind.LogExport:
                viewModel.LogExport.OpenCommand.Execute(null);
                break;
            case ModalKind.Debug:
                await viewModel.Debug.OpenCommand.ExecuteAsync(null);
                break;
            case ModalKind.DesignGallery:
                viewModel.Dialogs.Gallery.OpenCommand.Execute(null);
                break;
            case ModalKind.DebugResetConfirmation:
                viewModel.Dialogs.ShowDebugResetConfirmation();
                break;
            case ModalKind.Notice:
                viewModel.Dialogs.IsNoticeDialogVisible = true;
                break;
            case ModalKind.Update:
                viewModel.Dialogs.ShowUpdateAvailable("1.0.0", []);
                break;
            case ModalKind.Error:
                // Set the flag directly: ShowCriticalError posts to the dispatcher when the
                // calling thread is not the UI thread, which makes the modal registration
                // order-dependent. The Escape routing under test is the same either way.
                viewModel.Dialogs.IsErrorDialogVisible = true;
                break;
            case ModalKind.SetupWizard:
                viewModel.Dialogs.ShowSetupWizard();
                break;
            case ModalKind.SetupWizardExitConfirmation:
                viewModel.Dialogs.RequestSetupWizardExitCommand.Execute(null);
                break;
            case ModalKind.UnsavedSettingsConfirmation:
                viewModel.Settings.IsUnsavedChangesVisible = true;
                break;
            case ModalKind.SettingsResetConfirmation:
                viewModel.Dialogs.ShowSettingsResetConfirmation();
                break;
            case ModalKind.RepairConfirmation:
                viewModel.Dialogs.ShowRepairConfirm("repair");
                break;
            case ModalKind.ResourcePanelSourceConfirmation:
                viewModel.Dialogs.ShowResourcePanelSourceConfirm("source");
                break;
            case ModalKind.UninstallConfirmation:
                viewModel.Dialogs.ShowUninstallConfirm("uninstall");
                break;
            case ModalKind.StopConfirmation:
                viewModel.Dialogs.ShowStopConfirm();
                break;
            case ModalKind.DownloadRunningCloseConfirmation:
                viewModel.Dialogs.ShowDownloadRunningCloseConfirm();
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(kind),
                    kind,
                    "Unhandled ModalKind: add its open call and Escape routing.");
        }
    }
}
