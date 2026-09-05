using System.ComponentModel;
using Cafe.Launcher.Avalonia.Features.SetupWizard;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.ViewModels;

namespace Cafe.Launcher.Avalonia.Tests;

[Collection(nameof(LocalizationServiceTestIsolation))]
public sealed class SetupWizardViewModelTests
{
    static SetupWizardViewModelTests()
    {
        TestLocalizationHelper.Initialize();
    }

    private static SetupWizardViewModel CreateViewModel() => new(
        new LocalizationService(),
        new GameInstallationPath(),
        new LocalInstallationStateStore(),
        new LocalDiagnostics(),
        new StubFilePickerService());

    [Fact]
    public void InitialState_IsStep0_CanGoNext_CannotGoPrevious()
    {
        var vm = CreateViewModel();
        Assert.Equal(0, vm.Step);
        Assert.Equal("1 / 5", vm.StepProgress);
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
    public void NextCommand_FirstEntryToStep1WithEmptyGamePath_FillsDefaultGamePath()
    {
        var installationPath = new GameInstallationPath();
        var vm = new SetupWizardViewModel(
            new LocalizationService(),
            installationPath,
            new LocalInstallationStateStore(),
            new LocalDiagnostics(), new StubFilePickerService());

        vm.NextCommand.Execute(null);

        Assert.Equal(installationPath.GetDefaultGamePath(), vm.GamePath);
    }

    [Fact]
    public void Step1_WhenGamePathWasAlreadySetOrCleared_DoesNotOverwriteItOnReentry()
    {
        var vm = CreateViewModel();
        var existingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        vm.GamePath = existingPath;

        vm.NextCommand.Execute(null);

        Assert.Equal(existingPath, vm.GamePath);
        vm.GamePath = "";
        vm.PreviousCommand.Execute(null);
        vm.NextCommand.Execute(null);
        Assert.Equal("", vm.GamePath);
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
    public async Task NextCommand_AtStep4_DoesNotMove()
    {
        var vm = CreateViewModel();
        vm.GamePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        vm.NextCommand.Execute(null);
        await WaitForGamePathStatusAsync(vm, SetupWizardGamePathStatus.AvailableForInstallation);
        for (var i = 0; i < 3; i++)
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
    public async Task Step_IsStepProperties_AreConsistent()
    {
        var vm = CreateViewModel();
        Assert.True(vm.IsFirstStep);
        Assert.False(vm.IsStep1);
        Assert.False(vm.IsStep2);
        Assert.False(vm.IsStep3);
        Assert.False(vm.IsLastStep);

        vm.NextCommand.Execute(null);
        Assert.True(vm.IsStep1);

        vm.GamePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        await WaitForGamePathStatusAsync(vm, SetupWizardGamePathStatus.AvailableForInstallation);
        vm.NextCommand.Execute(null);
        Assert.True(vm.IsStep2);

        vm.NextCommand.Execute(null);
        Assert.True(vm.IsStep3);

        vm.NextCommand.Execute(null);
        Assert.True(vm.IsLastStep);
    }

    [Fact]
    public async Task CanGoNext_Step1WithEmptyPath_ReturnsFalse()
    {
        var vm = CreateViewModel();
        vm.NextCommand.Execute(null);
        vm.GamePath = "";
        await WaitForGamePathStatusAsync(vm, SetupWizardGamePathStatus.NotSelected);
        Assert.False(vm.CanGoNext);
    }

    [Fact]
    public async Task NextCommand_Step1WithEmptyPath_DoesNotAdvance()
    {
        var vm = CreateViewModel();
        vm.NextCommand.Execute(null);
        vm.GamePath = "";
        await WaitForGamePathStatusAsync(vm, SetupWizardGamePathStatus.NotSelected);
        vm.NextCommand.Execute(null);

        Assert.Equal(1, vm.Step);
        Assert.Equal("2 / 5", vm.StepProgress);
        Assert.False(vm.CanGoNext);
    }

    [Fact]
    public async Task CanGoNext_Step1WithNotInstalledPath_ReturnsTrue()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var vm = new SetupWizardViewModel(
            new LocalizationService(),
            new GameInstallationPath(),
            new LocalInstallationStateStore(),
            new LocalDiagnostics(), new StubFilePickerService());
        vm.GamePath = path;
        vm.NextCommand.Execute(null);
        await WaitForGamePathStatusAsync(vm, SetupWizardGamePathStatus.AvailableForInstallation);
        Assert.True(vm.CanGoNext);
    }

    [Fact]
    public async Task LastStep_ShowsSummaryDisplayNames()
    {
        var vm = CreateViewModel();
        vm.Language = LauncherLanguages.Japanese;
        vm.PatchUrlGroup = PatchUrlGroups.Cafe;
        vm.ProxyMode = ProxyModes.System;
        vm.GamePath = @"D:\Test\Path";
        await WaitForGamePathStatusAsync(vm, SetupWizardGamePathStatus.AvailableForInstallation);
        AdvanceToLastStep(vm);
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

    [Fact]
    public void DownloadSources_WhenSimplifiedChineseSelected_RecommendCafeWithReason()
    {
        var vm = CreateViewModel();
        vm.Language = LauncherLanguages.SimplifiedChinese;

        var cafe = Assert.Single(vm.DownloadSources, item => item.Code == PatchUrlGroups.Cafe);

        Assert.True(cafe.IsRecommended);
        Assert.NotEmpty(cafe.RecommendationReason);
    }

    [Fact]
    public void GoToStepCommand_ToCompletedStep_NavigatesBack()
    {
        var vm = CreateViewModel();
        vm.GamePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        vm.NextCommand.Execute(null);
        vm.NextCommand.Execute(null);
        Assert.Equal(2, vm.Step);

        vm.GoToStepCommand.Execute(0);

        Assert.Equal(0, vm.Step);
        Assert.True(vm.IsFirstStep);
    }

    [Fact]
    public void GoToStepCommand_ToUnvisitedStep_DoesNotNavigate()
    {
        var vm = CreateViewModel();

        vm.GoToStepCommand.Execute(2);

        Assert.Equal(0, vm.Step);
    }

    [Fact]
    public async Task GamePathStatus_WhenStateFilesDoNotExist_IsAvailableForInstallationAndCanGoNext()
    {
        var vm = CreateViewModel();
        vm.GamePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        vm.NextCommand.Execute(null);

        await WaitForGamePathStatusAsync(vm, SetupWizardGamePathStatus.AvailableForInstallation);

        Assert.True(vm.IsGamePathReady);
        Assert.True(vm.CanGoNext);
    }

    [Fact]
    public async Task GamePathStatus_WhenStateFilesAreValid_IsValidInstallationAndCanGoNext()
    {
        var gamePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var normalizedGamePath = new GameInstallationPath().NormalizeGamePath(gamePath);
        var store = new LocalInstallationStateStore();
        Directory.CreateDirectory(normalizedGamePath);
        await store.CommitAsync(normalizedGamePath, CreateCommit());
        var vm = new SetupWizardViewModel(new LocalizationService(), new GameInstallationPath(), store, new LocalDiagnostics(), new StubFilePickerService())
        {
            GamePath = gamePath
        };

        vm.NextCommand.Execute(null);
        await WaitForGamePathStatusAsync(vm, SetupWizardGamePathStatus.ValidInstallation);

        Assert.True(vm.IsGamePathReady);
        Assert.True(vm.CanGoNext);
    }

    [Fact]
    public async Task GamePathStatus_WhenOnlyManifestExists_IsCorruptedInstallationAndCannotGoNext()
    {
        var gamePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var normalizedGamePath = new GameInstallationPath().NormalizeGamePath(gamePath);
        Directory.CreateDirectory(normalizedGamePath);
        await File.WriteAllTextAsync(Path.Combine(normalizedGamePath, "manifest.json"), "{}");
        var vm = CreateViewModel();
        vm.GamePath = gamePath;
        vm.NextCommand.Execute(null);

        await WaitForGamePathStatusAsync(vm, SetupWizardGamePathStatus.CorruptedInstallation);

        Assert.False(vm.IsGamePathReady);
        Assert.False(vm.CanGoNext);
    }

    [Fact]
    public async Task GamePathPresentation_WhenInstallationIsCorrupted_HasTitleAndDescription()
    {
        var gamePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var normalizedGamePath = new GameInstallationPath().NormalizeGamePath(gamePath);
        Directory.CreateDirectory(normalizedGamePath);
        await File.WriteAllTextAsync(Path.Combine(normalizedGamePath, "manifest.json"), "{}");
        var localizer = new LocalizationService();
        var vm = new SetupWizardViewModel(
            localizer,
            new GameInstallationPath(),
            new LocalInstallationStateStore(),
            new LocalDiagnostics(), new StubFilePickerService());
        vm.GamePath = gamePath;
        vm.NextCommand.Execute(null);

        await WaitForGamePathStatusAsync(vm, SetupWizardGamePathStatus.CorruptedInstallation);

        Assert.Equal(localizer.T("setupWizardGamePathStatusTitle"), vm.GamePathPresentation.Title);
        Assert.Equal(localizer.T("setupWizardGamePathCorrupted"), vm.GamePathPresentation.Description);
    }

    [Fact]
    public async Task GamePathStatus_WhenStateFileIsLocked_IsInaccessibleAndCannotGoNext()
    {
        var gamePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var normalizedGamePath = new GameInstallationPath().NormalizeGamePath(gamePath);
        var store = new LocalInstallationStateStore();
        Directory.CreateDirectory(normalizedGamePath);
        await store.CommitAsync(normalizedGamePath, CreateCommit());
        await using var locked = new FileStream(
            Path.Combine(normalizedGamePath, "manifest.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var vm = new SetupWizardViewModel(new LocalizationService(), new GameInstallationPath(), store, new LocalDiagnostics(), new StubFilePickerService())
        {
            GamePath = gamePath
        };
        vm.NextCommand.Execute(null);

        await WaitForGamePathStatusAsync(vm, SetupWizardGamePathStatus.Inaccessible);

        Assert.False(vm.IsGamePathReady);
        Assert.False(vm.CanGoNext);
    }

    [Fact]
    public async Task GamePathStatus_WhenPathCannotBeNormalized_IsInaccessibleAndCannotGoNext()
    {
        var vm = CreateViewModel();
        vm.GamePath = "\0";
        vm.NextCommand.Execute(null);

        await WaitForGamePathStatusAsync(vm, SetupWizardGamePathStatus.Inaccessible);

        Assert.False(vm.IsGamePathReady);
        Assert.False(vm.CanGoNext);
    }

    [Fact]
    public async Task GamePathStatus_WhenChecking_CannotGoNextAndUpdatesWhenReadCompletes()
    {
        var gamePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var normalizedGamePath = new GameInstallationPath().NormalizeGamePath(gamePath);
        Directory.CreateDirectory(normalizedGamePath);
        var tempFilesWritten = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCommit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new LocalInstallationStateStore(async (_, cancellationToken) =>
        {
            tempFilesWritten.TrySetResult();
            await releaseCommit.Task.WaitAsync(cancellationToken);
        });
        var commitTask = store.CommitAsync(normalizedGamePath, CreateCommit());
        await tempFilesWritten.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var vm = new SetupWizardViewModel(new LocalizationService(), new GameInstallationPath(), store, new LocalDiagnostics(), new StubFilePickerService())
        {
            GamePath = gamePath
        };

        vm.NextCommand.Execute(null);
        await WaitForGamePathStatusAsync(vm, SetupWizardGamePathStatus.Checking);
        Assert.False(vm.CanGoNext);

        releaseCommit.TrySetResult();
        await commitTask;
        await WaitForGamePathStatusAsync(vm, SetupWizardGamePathStatus.ValidInstallation);
        Assert.True(vm.CanGoNext);
    }

    [Fact]
    public async Task GamePathStatus_WhenOldReadIsCancelled_DoesNotOverwriteNewPathStatus()
    {
        var oldGamePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var normalizedOldGamePath = new GameInstallationPath().NormalizeGamePath(oldGamePath);
        var newGamePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(normalizedOldGamePath);
        var tempFilesWritten = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCommit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new LocalInstallationStateStore(async (_, cancellationToken) =>
        {
            tempFilesWritten.TrySetResult();
            await releaseCommit.Task.WaitAsync(cancellationToken);
        });
        var commitTask = store.CommitAsync(normalizedOldGamePath, CreateCommit());
        await tempFilesWritten.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var vm = new SetupWizardViewModel(new LocalizationService(), new GameInstallationPath(), store, new LocalDiagnostics(), new StubFilePickerService())
        {
            GamePath = oldGamePath
        };
        vm.NextCommand.Execute(null);
        await WaitForGamePathStatusAsync(vm, SetupWizardGamePathStatus.Checking);

        vm.GamePath = newGamePath;
        await WaitForGamePathStatusAsync(vm, SetupWizardGamePathStatus.AvailableForInstallation);
        releaseCommit.TrySetResult();
        await commitTask;

        Assert.Equal(SetupWizardGamePathStatus.AvailableForInstallation, vm.GamePathStatus);
    }

    [Fact]
    public async Task GamePathStatusText_WhenLanguageChanges_RaisesPropertyChanged()
    {
        var localizer = new LocalizationService();
        var vm = new SetupWizardViewModel(
            localizer,
            new GameInstallationPath(),
            new LocalInstallationStateStore(),
            new LocalDiagnostics(), new StubFilePickerService())
        {
            GamePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
        };
        vm.NextCommand.Execute(null);
        await WaitForGamePathStatusAsync(vm, SetupWizardGamePathStatus.AvailableForInstallation);
        var changed = false;
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SetupWizardViewModel.GamePathStatusText))
            {
                changed = true;
            }
        };

        localizer.SetLanguage(LauncherLanguages.Japanese);

        Assert.True(changed);
    }

    private static async Task WaitForGamePathStatusAsync(
        SetupWizardViewModel viewModel,
        SetupWizardGamePathStatus expectedStatus)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        PropertyChangedEventHandler? handler = null;
        handler = (_, args) =>
        {
            if (args.PropertyName == nameof(SetupWizardViewModel.GamePathStatus)
                && viewModel.GamePathStatus == expectedStatus)
            {
                completion.TrySetResult();
            }
        };
        viewModel.PropertyChanged += handler;
        try
        {
            if (viewModel.GamePathStatus == expectedStatus)
            {
                return;
            }

            await completion.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            viewModel.PropertyChanged -= handler;
        }
    }

    private static void AdvanceToLastStep(SetupWizardViewModel viewModel)
    {
        // 步数上限护栏：未来若新增步骤门控引入异步依赖，这里快速失败
        // 而非热自旋挂死测试进程（参见 P0 整改中 Headless 同类修复）。
        for (var guard = 0; !viewModel.IsLastStep && guard < 10; guard++)
        {
            viewModel.NextCommand.Execute(null);
        }

        Assert.True(viewModel.IsLastStep, "向导未在上限步数内推进到末步。");
    }

    private static LocalInstallationStateCommit CreateCommit() => new(
        "1.2.3",
        "manifest.json",
        "BlueArchive",
        ["--test"],
        [new LocalInstallationFile("BlueArchive.exe", 4, "1234")]);
}
