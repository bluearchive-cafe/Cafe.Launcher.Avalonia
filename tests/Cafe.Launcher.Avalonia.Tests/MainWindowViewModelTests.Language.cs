using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Tests;

/// <summary>
/// 展示刷新与设置草稿的边界：语言预览只改呈现，不写草稿。首次向导在用户翻到语言一步时
/// 会即时预览所选语言，若预览落在草稿上，用户什么都没保存就会看到保存按钮亮起、设置页
/// 显示一个他没选过的语言。
/// </summary>
public partial class MainWindowViewModelTests
{
    [Fact]
    public async Task ApplyLanguage_WhenPreviewingAnotherLanguage_LeavesTheSettingsDraftUntouched()
    {
        using var viewModel = await CreateViewModelAsync(new CountingCoreService(CreateSnapshot()));
        await viewModel.InitializeAsync();
        viewModel.Shell.ApplyLanguage(
            LauncherLanguages.English,
            viewModel.Settings,
            viewModel.ResourcePanel,
            hasSnapshot: false);
        var draftLanguage = viewModel.Settings.Editor.Current.Language;
        var englishLabel = viewModel.Shell.I18n[LocalizationKeys.Settings];

        viewModel.Shell.ApplyLanguage(
            LauncherLanguages.Japanese,
            viewModel.Settings,
            viewModel.ResourcePanel,
            hasSnapshot: false);

        Assert.NotEqual(englishLabel, viewModel.Shell.I18n[LocalizationKeys.Settings]);
        Assert.Equal(draftLanguage, viewModel.Settings.Editor.Current.Language);
        Assert.False(viewModel.Settings.Editor.IsDirty);
    }

    [Fact]
    public async Task SetupWizardLanguagePreview_DoesNotWriteTheSettingsDraft()
    {
        using var viewModel = await CreateViewModelAsync(new CountingCoreService(CreateSnapshot()));
        await viewModel.InitializeAsync();
        viewModel.Shell.ApplyLanguage(
            LauncherLanguages.English,
            viewModel.Settings,
            viewModel.ResourcePanel,
            hasSnapshot: false);
        viewModel.Dialogs.IsSetupWizardVisible = true;
        var draftLanguage = viewModel.Settings.Editor.Current.Language;
        var englishLabel = viewModel.Shell.I18n[LocalizationKeys.Settings];

        viewModel.Dialogs.SetupWizard.Language = LauncherLanguages.Japanese;

        Assert.NotEqual(englishLabel, viewModel.Shell.I18n[LocalizationKeys.Settings]);
        Assert.Equal(draftLanguage, viewModel.Settings.Editor.Current.Language);
        Assert.False(viewModel.Settings.Editor.IsDirty);
    }
}
