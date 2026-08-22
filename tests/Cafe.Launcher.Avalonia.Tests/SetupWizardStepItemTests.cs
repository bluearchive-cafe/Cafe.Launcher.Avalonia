using Cafe.Launcher.Avalonia.Features.SetupWizard;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.ViewModels;
using Xunit;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class SetupWizardStepItemTests : LocalizationTestBase
{
    static SetupWizardStepItemTests() => TestLocalizationHelper.Initialize();

    [Fact]
    public void Constructor_SetsSummaryToEmpty()
    {
        var item = new SetupWizardStepItem { Index = 0 };

        Assert.Equal(string.Empty, item.Summary);
    }

    [Fact]
    public void InitialState_ExposesFiveOrderedStepsWithOnlyFirstCurrent()
    {
        var viewModel = new SetupWizardViewModel(
            new LocalizationService(),
            new GameInstallationPath(), new LocalInstallationStateStore(),
            new LocalDiagnostics());

        Assert.Equal(5, viewModel.Steps.Count);
        Assert.Equal([0, 1, 2, 3, 4], viewModel.Steps.Select(item => item.Index));
        Assert.Equal(SetupWizardStepState.Current, viewModel.Steps[0].State);
        Assert.All(
            viewModel.Steps.Skip(1),
            item => Assert.Equal(SetupWizardStepState.Locked, item.State));
    }

    [Fact]
    public void LanguageChanged_RefreshesStepTitlesFromCurrentLocale()
    {
        var localizer = new LocalizationService();
        var viewModel = new SetupWizardViewModel(localizer, new GameInstallationPath(), new LocalInstallationStateStore(), new LocalDiagnostics());

        localizer.SetLanguage(LauncherLanguages.SimplifiedChinese);

        Assert.Equal(
            [
                localizer.T("setupWizardLanguage"),
                localizer.T("setupWizardGamePath"),
                localizer.T("setupWizardDownloadSource"),
                localizer.T("setupWizardProxy"),
                localizer.T("setupWizardReview")
            ],
            viewModel.Steps.Select(item => item.Title));
    }

    [Fact]
    public void NextCommand_RefreshesCompletedCurrentAndLockedStates()
    {
        var viewModel = new SetupWizardViewModel(
            new LocalizationService(),
            new GameInstallationPath(), new LocalInstallationStateStore(),
            new LocalDiagnostics());

        viewModel.NextCommand.Execute(null);

        Assert.Equal(SetupWizardStepState.Completed, viewModel.Steps[0].State);
        Assert.Equal(SetupWizardStepState.Current, viewModel.Steps[1].State);
        Assert.Equal(SetupWizardStepState.Locked, viewModel.Steps[2].State);
    }

    [Fact]
    public void GoToStepCommand_LockedStepDoesNotNavigate()
    {
        var viewModel = new SetupWizardViewModel(
            new LocalizationService(),
            new GameInstallationPath(), new LocalInstallationStateStore(),
            new LocalDiagnostics());

        viewModel.GoToStepCommand.Execute(3);

        Assert.Equal(0, viewModel.Step);
    }

    [Fact]
    public void GoToStepCommand_CompletedStepNavigatesBack()
    {
        var viewModel = new SetupWizardViewModel(
            new LocalizationService(),
            new GameInstallationPath(), new LocalInstallationStateStore(),
            new LocalDiagnostics());
        viewModel.NextCommand.Execute(null);
        viewModel.NextCommand.Execute(null);

        viewModel.GoToStepCommand.Execute(0);

        Assert.Equal(0, viewModel.Step);
        Assert.Equal(SetupWizardStepState.Current, viewModel.Steps[0].State);
    }
}
