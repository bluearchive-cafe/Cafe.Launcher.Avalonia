using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.ViewModels;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class SetupWizardViewModelTests
{
    static SetupWizardViewModelTests()
    {
        TestLocalizationHelper.Initialize();
    }

    private static SetupWizardViewModel CreateViewModel() => new(
        new LocalizationService(),
        new GameInstallationPath());

    [Fact]
    public void InitialState_IsStep0_CanGoNext_CannotGoPrevious()
    {
        var vm = CreateViewModel();
        Assert.Equal(0, vm.Step);
        Assert.True(vm.IsFirstStep);
        Assert.False(vm.IsLastStep);
        Assert.False(vm.CanGoPrevious);
        Assert.True(vm.CanGoNext);
    }

    [Fact]
    public void NextCommand_MovesToStep1()
    {
        var vm = CreateViewModel();
        vm.NextCommand.Execute(null);
        Assert.Equal(1, vm.Step);
        Assert.True(vm.IsStep1);
        Assert.True(vm.CanGoPrevious);
        Assert.False(vm.IsFirstStep);
    }

    [Fact]
    public void PreviousCommand_MovesBack()
    {
        var vm = CreateViewModel();
        vm.NextCommand.Execute(null);
        vm.PreviousCommand.Execute(null);
        Assert.Equal(0, vm.Step);
        Assert.True(vm.IsFirstStep);
        Assert.False(vm.CanGoPrevious);
    }

    [Fact]
    public void PreviousCommand_AtStep0_DoesNothing()
    {
        var vm = CreateViewModel();
        vm.PreviousCommand.Execute(null);
        Assert.Equal(0, vm.Step);
    }

    [Fact]
    public void NextCommand_AtStep4_DoesNotMove()
    {
        var vm = CreateViewModel();
        vm.GamePath = @"D:\YostarGames\BlueArchive_JP";
        for (var i = 0; i < 4; i++)
            vm.NextCommand.Execute(null);
        Assert.Equal(4, vm.Step);
        Assert.True(vm.IsLastStep);
        Assert.False(vm.IsStep1);
        Assert.False(vm.IsStep2);
        Assert.False(vm.IsStep3);
        vm.NextCommand.Execute(null);
        Assert.Equal(4, vm.Step);
    }

    [Fact]
    public void Step_IsStepProperties_AreConsistent()
    {
        var vm = CreateViewModel();
        Assert.True(vm.IsFirstStep);
        Assert.False(vm.IsStep1);
        Assert.False(vm.IsStep2);
        Assert.False(vm.IsStep3);
        Assert.False(vm.IsLastStep);

        vm.NextCommand.Execute(null);
        Assert.True(vm.IsStep1);
        Assert.False(vm.IsStep2);

        vm.NextCommand.Execute(null);
        Assert.True(vm.IsStep2);

        vm.GamePath = @"D:\YostarGames\BlueArchive_JP";
        vm.NextCommand.Execute(null);
        Assert.True(vm.IsStep3);

        vm.NextCommand.Execute(null);
        Assert.True(vm.IsLastStep);
    }

    [Fact]
    public void StepTitle_AtEachStep_ReturnsNonEmpty()
    {
        var vm = CreateViewModel();
        Assert.NotEmpty(vm.StepTitle);
        for (var i = 0; i < 4; i++)
        {
            vm.NextCommand.Execute(null);
            Assert.NotEmpty(vm.StepTitle);
        }
    }

    [Fact]
    public void CanGoNext_Step2WithEmptyPath_ReturnsFalse()
    {
        var vm = CreateViewModel();
        vm.GamePath = "";
        vm.NextCommand.Execute(null);
        vm.NextCommand.Execute(null);
        Assert.False(vm.CanGoNext);
    }

    [Fact]
    public void NextCommand_Step2WithEmptyPath_DoesNotAdvance()
    {
        var vm = CreateViewModel();
        vm.GamePath = "";
        vm.NextCommand.Execute(null);
        vm.NextCommand.Execute(null);

        vm.NextCommand.Execute(null);

        Assert.Equal(2, vm.Step);
        Assert.False(vm.CanGoNext);
    }

    [Fact]
    public void CanGoNext_Step2WithValidPath_ReturnsTrue()
    {
        var vm = CreateViewModel();
        vm.GamePath = @"D:\YostarGames\BlueArchive_JP";
        vm.NextCommand.Execute(null);
        vm.NextCommand.Execute(null);
        Assert.True(vm.CanGoNext);
    }

    [Fact]
    public void LastStep_ShowsSummaryDisplayNames()
    {
        var vm = CreateViewModel();
        vm.Language = LauncherLanguages.Japanese;
        vm.PatchUrlGroup = PatchUrlGroups.Cafe;
        vm.ProxyMode = ProxyModes.System;
        vm.GamePath = @"D:\Test\Path";
        while (!vm.IsLastStep)
            vm.NextCommand.Execute(null);
        Assert.NotNull(vm.LanguageDisplayName);
        Assert.NotNull(vm.DownloadSourceDisplayName);
        Assert.NotNull(vm.ProxyDisplayName);
    }

    [Fact]
    public async Task CompleteCommand_RaisesSettingsApplied()
    {
        var vm = CreateViewModel();
        LauncherSettings? captured = null;
        vm.SettingsApplied += s => { captured = s; return Task.CompletedTask; };
        var defaults = LauncherSettings.CreateDefaults();
        vm.Language = LauncherLanguages.Japanese;
        vm.PatchUrlGroup = PatchUrlGroups.Cafe;
        vm.ProxyMode = ProxyModes.System;
        vm.GamePath = @"D:\Games\Path";
        await vm.CompleteCommand.ExecuteAsync(null);
        Assert.NotNull(captured);
        Assert.Equal(LauncherLanguages.Japanese, captured!.Language);
        Assert.Equal(PatchUrlGroups.Cafe, captured.PatchUrlGroup);
        Assert.Equal(ProxyModes.System, captured.ProxyMode);
        Assert.Contains("BlueArchive_JP", captured.GamePath, StringComparison.Ordinal);
        Assert.Equal(defaults.UpdateChannel, captured.UpdateChannel);
        Assert.Equal(defaults.LogLevel, captured.LogLevel);
        Assert.Equal(defaults.ToastNotificationsEnabled, captured.ToastNotificationsEnabled);
        Assert.Equal(defaults.ShowRemoteContentCard, captured.ShowRemoteContentCard);
    }

    [Fact]
    public async Task SkipCommand_RaisesSettingsAppliedWithDefaults()
    {
        var vm = CreateViewModel();
        LauncherSettings? captured = null;
        vm.SettingsApplied += s => { captured = s; return Task.CompletedTask; };
        vm.Language = LauncherLanguages.Japanese;
        await vm.SkipCommand.ExecuteAsync(null);
        Assert.NotNull(captured);
        Assert.Equal(LauncherLanguages.Auto, captured!.Language);
    }

    [Fact]
    public void BrowseGamePathCommand_WhenPickerNull_DoesNothing()
    {
        var vm = CreateViewModel();
        var original = vm.GamePath;
        vm.BrowseGamePathCommand.Execute(null);
        Assert.Equal(original, vm.GamePath);
    }

    [Fact]
    public void LanguageDefaults_MatchLauncherSettingsCreateDefaults()
    {
        var vm = CreateViewModel();
        var defaults = LauncherSettings.CreateDefaults();
        Assert.Equal(defaults.Language, vm.Language);
        Assert.Equal(defaults.PatchUrlGroup, vm.PatchUrlGroup);
        Assert.Equal(defaults.ProxyMode, vm.ProxyMode);
    }
}
