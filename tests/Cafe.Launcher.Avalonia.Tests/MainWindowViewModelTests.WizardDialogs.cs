using Cafe.Launcher.Avalonia.Features.Shell;
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

    [Fact]
    public async Task TryHandleEscape_ForEveryModalKind_ClosesOnlyTopModal()
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

        viewModel.Settings.IsUnsavedChangesVisible = true;
        Assert.True(viewModel.TryHandleEscape());
        Assert.False(viewModel.Settings.IsUnsavedChangesVisible);

        viewModel.Dialogs.ShowResourcePanelSourceConfirm("source");
        Assert.True(viewModel.TryHandleEscape());
        Assert.False(viewModel.Dialogs.IsResourcePanelSourceConfirmVisible);

        viewModel.Dialogs.ShowUninstallConfirm("uninstall");
        Assert.True(viewModel.TryHandleEscape());
        Assert.False(viewModel.Dialogs.IsUninstallConfirmVisible);

        viewModel.Dialogs.ShowStopConfirm();
        Assert.True(viewModel.TryHandleEscape());
        Assert.False(viewModel.Dialogs.IsStopConfirmVisible);

        viewModel.Dialogs.ShowDownloadRunningCloseConfirm();
        Assert.True(viewModel.TryHandleEscape());
        Assert.False(viewModel.Dialogs.IsDownloadRunningCloseConfirmVisible);

        viewModel.Dialogs.IsNoticeDialogVisible = true;
        Assert.True(viewModel.TryHandleEscape());
        Assert.False(viewModel.Dialogs.IsNoticeDialogVisible);

        viewModel.Dialogs.ShowUpdateAvailable("1.0.0", []);
        Assert.True(viewModel.TryHandleEscape());
        Assert.False(viewModel.Dialogs.IsUpdateAvailableVisible);

        viewModel.LogViewer.OpenCommand.Execute(null);
        Assert.True(viewModel.TryHandleEscape());
        Assert.False(viewModel.LogViewer.IsVisible);

        viewModel.ResourcePanel.IsResourcePanelVisible = true;
        Assert.True(viewModel.TryHandleEscape());
        Assert.False(viewModel.ResourcePanel.IsResourcePanelVisible);

        Assert.False(viewModel.TryHandleEscape());
    }
}
