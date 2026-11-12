using System.ComponentModel;
using Cafe.Launcher.UI.Constants;
using Cafe.Launcher.UI.Features.SetupWizard;
using Cafe.Launcher.UI.Helpers;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.Core.Services.Diagnostics;
using Cafe.Launcher.Testing;
using Cafe.Launcher.UI.ViewModels;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Services;
using Cafe.Launcher.Core.Constants;

namespace Cafe.Launcher.Tests;

[Collection(nameof(LocalizationServiceTestIsolation))]
public sealed class SetupWizardViewModelTests
{
    /// <summary>向导路径校验（防抖 + 后台写探测）的就绪预算。</summary>
    private static readonly TimeSpan GateSettleBudget = TimeSpan.FromSeconds(5);

    private static SetupWizardViewModel CreateViewModel(
        Func<AgreementDocuments.Content>? agreementLoader = null)
    {
        var localizer = new LocalizationService();
        localizer.SetLanguage(LauncherLanguages.English);
        return new(
            localizer,
            new GameInstallationPath(LauncherProfiles.BlueArchiveJapan),
            new LocalInstallationStateStore(LauncherProfiles.BlueArchiveJapan),
            new LocalDiagnostics(),
            new StubFilePickerService(),
            agreementLoader: agreementLoader);
    }

    /// <summary>接受协议并推进到游戏路径步骤（步骤 3）。</summary>
    private static void AdvanceToGamePath(SetupWizardViewModel viewModel)
    {
        viewModel.MarkAgreementRead();
        viewModel.IsAgreementAccepted = true;
        viewModel.NextCommand.Execute(null);
        viewModel.NextCommand.Execute(null);
        viewModel.NextCommand.Execute(null);
    }

    /// <summary>推进到协议步骤（步骤 2），不预先接受协议。</summary>
    private static void AdvanceToAgreement(SetupWizardViewModel viewModel)
    {
        viewModel.NextCommand.Execute(null);
        viewModel.NextCommand.Execute(null);
    }

    [Fact]
    public void InitialState_IsStep0_CanGoNext_CannotGoPrevious()
    {
        var vm = CreateViewModel();
        Assert.Equal(0, vm.Step);
        Assert.Equal("Step 1 of 7", vm.StepProgress);
        Assert.True(vm.IsFirstStep);
        Assert.False(vm.IsLastStep);
        Assert.False(vm.CanGoPrevious);
        Assert.True(vm.CanGoNext);
    }

    [Fact]
    public void NextCommand_MovesFromWelcomeToLanguageStep()
    {
        var vm = CreateViewModel();
        vm.NextCommand.Execute(null);
        Assert.Equal(1, vm.Step);
        Assert.False(vm.IsAgreementStep);
        Assert.True(vm.CanGoPrevious);
        Assert.False(vm.IsFirstStep);
        Assert.True(vm.CanGoNext);
    }

    [Fact]
    public void NextCommand_AgreementStepWithoutAcceptance_DoesNotAdvance()
    {
        var vm = CreateViewModel();
        AdvanceToAgreement(vm);
        vm.NextCommand.Execute(null);
        Assert.Equal(2, vm.Step);
        Assert.False(vm.CanGoNext);
    }

    [Fact]
    public void MarkAgreementRead_EnablesCheckboxWithoutAccepting()
    {
        var vm = CreateViewModel(
            () => new AgreementDocuments.Content("terms", LoadError: null));
        AdvanceToAgreement(vm);

        vm.MarkAgreementRead();

        Assert.True(vm.HasReadAgreement);
        Assert.True(vm.IsAgreementAvailable);
        Assert.False(vm.IsAgreementAccepted);
        Assert.False(vm.CanGoNext);
    }

    [Fact]
    public void MarkAgreementRead_WhenDocumentsFailedToLoad_KeepsTheGateLocked()
    {
        // 守卫（AUD-UI-005）：加载失败时回退正文只有「标题 + 错误串」，内容不足一屏会
        // 立刻满足滚动阅读判定；同意门禁必须保持锁定，并把错误留在正文里便于诊断。
        var vm = CreateViewModel(
            () => new AgreementDocuments.Content(string.Empty, "missing terms asset"));
        AdvanceToAgreement(vm);

        vm.MarkAgreementRead();

        Assert.False(vm.HasReadAgreement);
        Assert.False(vm.IsAgreementAvailable);
        Assert.False(vm.CanGoNext);
        Assert.Contains("missing terms asset", vm.AgreementMarkdown, StringComparison.Ordinal);
        Assert.Equal(2, vm.Step);
    }

    [Fact]
    public void MarkAgreementRead_WhenDocumentsRecoverOnNextLaunch_UnlocksNormally()
    {
        // 失败后下一次启动正常加载时，阅读门禁语义不变：读完即可勾选并前进。
        var vm = CreateViewModel(
            () => new AgreementDocuments.Content("terms", LoadError: null));
        AdvanceToAgreement(vm);

        vm.MarkAgreementRead();
        vm.IsAgreementAccepted = true;

        Assert.True(vm.HasReadAgreement);
        Assert.True(vm.CanGoNext);
    }

    [Fact]
    public async Task CompleteCommand_WithoutAcceptingAgreement_DoesNotApply()
    {
        var vm = CreateViewModel();
        var applied = false;
        vm.SettingsApplied += _ => { applied = true; return Task.CompletedTask; };

        await vm.CompleteCommand.ExecuteAsync(null);

        Assert.False(applied);
    }

    [Fact]
    public void NextCommand_FirstEntryToGamePathStepWithEmptyGamePath_FillsDefaultGamePath()
    {
        var installationPath = new GameInstallationPath(LauncherProfiles.BlueArchiveJapan);
        var vm = new SetupWizardViewModel(
            new LocalizationService(),
            installationPath,
            new LocalInstallationStateStore(LauncherProfiles.BlueArchiveJapan),
            new LocalDiagnostics(), new StubFilePickerService());

        AdvanceToGamePath(vm);

        Assert.Equal(3, vm.Step);
        Assert.Equal(installationPath.GetDefaultGamePath(), vm.GamePath);
    }

    [Fact]
    public void GamePathStep_WhenGamePathWasAlreadySetOrCleared_DoesNotOverwriteItOnReentry()
    {
        var vm = CreateViewModel();
        using var existingPath = TestDirectory.Create();
        vm.GamePath = existingPath;

        AdvanceToGamePath(vm);

        Assert.Equal(existingPath, vm.GamePath);
        vm.GamePath = "";
        vm.PreviousCommand.Execute(null);
        Assert.Equal(2, vm.Step);
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
    public async Task NextCommand_AtReviewStep_DoesNotMove()
    {
        var vm = CreateViewModel();
        vm.GamePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        AdvanceToGamePath(vm);
        await WaitForGamePathStatusAsync(vm, SetupWizardGamePathStatus.AvailableForInstallation);
        for (var i = 0; i < 3; i++)
            vm.NextCommand.Execute(null);
        Assert.Equal(6, vm.Step);
        Assert.True(vm.IsLastStep);
        vm.NextCommand.Execute(null);
        Assert.Equal(6, vm.Step);
    }

    [Fact]
    public async Task StepSequence_AdvancesThroughAllSteps()
    {
        var vm = CreateViewModel();
        Assert.Equal(0, vm.Step);
        Assert.True(vm.IsFirstStep);
        Assert.False(vm.IsLastStep);
        Assert.False(vm.IsAgreementStep);

        vm.MarkAgreementRead();
        vm.IsAgreementAccepted = true;
        vm.NextCommand.Execute(null);
        Assert.Equal(1, vm.Step);
        Assert.False(vm.IsAgreementStep);

        vm.NextCommand.Execute(null);
        Assert.Equal(2, vm.Step);
        Assert.True(vm.IsAgreementStep);

        vm.NextCommand.Execute(null);
        Assert.Equal(3, vm.Step);

        vm.GamePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        await WaitForGamePathStatusAsync(vm, SetupWizardGamePathStatus.AvailableForInstallation);
        vm.NextCommand.Execute(null);
        Assert.Equal(4, vm.Step);

        vm.NextCommand.Execute(null);
        Assert.Equal(5, vm.Step);

        vm.NextCommand.Execute(null);
        Assert.Equal(6, vm.Step);
        Assert.True(vm.IsLastStep);
    }

    [Fact]
    public async Task CanGoNext_GamePathStepWithEmptyPath_ReturnsFalse()
    {
        var vm = CreateViewModel();
        AdvanceToGamePath(vm);
        vm.GamePath = "";
        await WaitForGamePathStatusAsync(vm, SetupWizardGamePathStatus.NotSelected);
        Assert.False(vm.CanGoNext);
    }

    [Fact]
    public async Task NextCommand_GamePathStepWithEmptyPath_DoesNotAdvance()
    {
        var vm = CreateViewModel();
        AdvanceToGamePath(vm);
        vm.GamePath = "";
        await WaitForGamePathStatusAsync(vm, SetupWizardGamePathStatus.NotSelected);
        vm.NextCommand.Execute(null);

        Assert.Equal(3, vm.Step);
        Assert.Equal("Step 4 of 7", vm.StepProgress);
        Assert.False(vm.CanGoNext);
    }

    [Fact]
    public async Task CanGoNext_GamePathStepWithNotInstalledPath_ReturnsTrue()
    {
        using var path = TestDirectory.Create();
        var vm = new SetupWizardViewModel(
            new LocalizationService(),
            new GameInstallationPath(LauncherProfiles.BlueArchiveJapan),
            new LocalInstallationStateStore(LauncherProfiles.BlueArchiveJapan),
            new LocalDiagnostics(), new StubFilePickerService());
        vm.GamePath = path;
        AdvanceToGamePath(vm);
        await WaitForGamePathStatusAsync(vm, SetupWizardGamePathStatus.AvailableForInstallation);
        Assert.True(vm.CanGoNext);
    }

    [Fact]
    public async Task CanGoNext_GamePathStepWithNotWritablePath_ReturnsFalse()
    {
        using var root = TestDirectory.Create();
        var blocker = Path.Combine(root, "blocker");
        File.WriteAllText(blocker, "not a directory");
        var vm = new SetupWizardViewModel(
            new LocalizationService(),
            new GameInstallationPath(LauncherProfiles.BlueArchiveJapan),
            new LocalInstallationStateStore(LauncherProfiles.BlueArchiveJapan),
            new LocalDiagnostics(), new StubFilePickerService());
        vm.GamePath = Path.Combine(blocker, LauncherProfiles.BlueArchiveJapan.RootFolderName, LauncherProfiles.BlueArchiveJapan.GameFolderName);
        AdvanceToGamePath(vm);
        await WaitForGamePathStatusAsync(vm, SetupWizardGamePathStatus.NotWritable);
        Assert.True(vm.IsGamePathNotWritable);
        Assert.False(vm.IsGamePathReady);
        Assert.False(vm.CanGoNext);
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
        await AdvanceToLastStep(vm);
        Assert.NotNull(vm.LanguageDisplayName);
        Assert.NotNull(vm.DownloadSourceDisplayName);
        Assert.NotNull(vm.ProxyDisplayName);

        var items = vm.SummaryItems;
        Assert.Equal(4, items.Count);
        Assert.Equal(new[] { 1, 4, 3, 5 }, items.Select(item => item.EditStep).ToArray());
        Assert.Equal(
            new[] { "Translate", "CloudDownload", "FolderSearch", "SwapHorizontal" },
            items.Select(item => item.Icon.ToString()).ToArray());
        Assert.Equal(
            new[] { false, true, true, true },
            items.Select(item => item.HasDivider).ToArray());
        Assert.All(items, item =>
        {
            Assert.False(string.IsNullOrWhiteSpace(item.Label));
            Assert.False(string.IsNullOrWhiteSpace(item.EditLabel));
            Assert.Same(vm.GoToStepCommand, item.EditCommand);
        });
        Assert.Equal(vm.LanguageDisplayName, items[0].Value);
        Assert.Equal(vm.DownloadSourceDisplayName, items[1].Value);
        Assert.Equal(vm.GamePathDisplay, items[2].Value);
        Assert.Equal(vm.ProxyDisplayName, items[3].Value);
    }

    [Fact]
    public void GamePathDisplay_PathWithinBudget_EqualsRawPath()
    {
        var vm = CreateViewModel();
        vm.GamePath = @"D:\Test\Path";

        Assert.Equal(@"D:\Test\Path", vm.GamePathDisplay);
    }

    [Fact]
    public void GamePathChange_RaisesGamePathDisplayNotification()
    {
        var vm = CreateViewModel();
        var changes = 0;
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(vm.GamePathDisplay))
            {
                changes++;
            }
        };

        vm.GamePath = @"E:\" + new string('a', 80) + @"\YostarGames\BlueArchive_JP";

        Assert.Equal(1, changes);
        Assert.Contains(PathMiddleEllipsis.Ellipsis, vm.GamePathDisplay);
    }

    [Fact]
    public async Task CompleteCommand_RaisesSettingsApplied()
    {
        var vm = CreateViewModel();
        vm.MarkAgreementRead();
        vm.IsAgreementAccepted = true;
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
        Assert.Equal(LauncherAgreement.CurrentVersion, captured.AcceptedAgreementVersion);
    }

    [Fact]
    public async Task SkipCommand_AfterAcceptingAgreement_RaisesSettingsAppliedWithDefaults()
    {
        var vm = CreateViewModel();
        LauncherSettings? captured = null;
        vm.SettingsApplied += s => { captured = s; return Task.CompletedTask; };
        vm.MarkAgreementRead();
        vm.IsAgreementAccepted = true;
        vm.Language = LauncherLanguages.Japanese;
        await vm.SkipCommand.ExecuteAsync(null);
        Assert.NotNull(captured);
        Assert.Equal(LauncherLanguages.Auto, captured!.Language);
        Assert.Equal(LauncherAgreement.CurrentVersion, captured.AcceptedAgreementVersion);
    }

    [Fact]
    public async Task SkipCommand_WithoutAcceptingAgreement_RequestsExitWithoutApplying()
    {
        var vm = CreateViewModel();
        var applied = false;
        var exitRequested = false;
        vm.SettingsApplied += _ => { applied = true; return Task.CompletedTask; };
        vm.AgreementExitRequested += () => exitRequested = true;

        await vm.SkipCommand.ExecuteAsync(null);

        Assert.True(exitRequested);
        Assert.False(applied);
    }

    [Fact]
    public async Task CompleteCommand_InConsentUpdateMode_KeepsBaselineSettingsAndRecordsVersion()
    {
        var vm = CreateViewModel();
        var baseline = LauncherSettings.CreateDefaults();
        baseline.Language = LauncherLanguages.Japanese;
        baseline.GamePath = @"D:\Saved\BlueArchive_JP";
        baseline.PatchUrlGroup = PatchUrlGroups.Cafe;
        baseline.ProxyMode = ProxyModes.System;
        baseline.AcceptedAgreementVersion = "2020-01-01";
        LauncherSettings? captured = null;
        vm.SettingsApplied += s => { captured = s; return Task.CompletedTask; };

        vm.PrepareForAgreementUpdate(baseline);

        Assert.True(vm.IsConsentUpdateMode);
        Assert.True(vm.IsLastStep);
        Assert.Equal(2, vm.Step);
        Assert.False(vm.CanGoPrevious);
        Assert.False(vm.CanSkip);
        Assert.Equal("Step 1 of 1", vm.StepProgress);

        vm.MarkAgreementRead();
        vm.IsAgreementAccepted = true;
        await vm.CompleteCommand.ExecuteAsync(null);

        Assert.NotNull(captured);
        Assert.Equal(LauncherLanguages.Japanese, captured!.Language);
        Assert.Equal(@"D:\Saved\BlueArchive_JP", captured.GamePath);
        Assert.Equal(PatchUrlGroups.Cafe, captured.PatchUrlGroup);
        Assert.Equal(ProxyModes.System, captured.ProxyMode);
        Assert.Equal(LauncherAgreement.CurrentVersion, captured.AcceptedAgreementVersion);
    }

    [Fact]
    public async Task SkipCommand_InConsentUpdateMode_RequestsExitEvenAfterAcceptance()
    {
        var vm = CreateViewModel();
        var exitRequested = false;
        vm.AgreementExitRequested += () => exitRequested = true;
        vm.PrepareForAgreementUpdate(LauncherSettings.CreateDefaults());
        vm.MarkAgreementRead();
        vm.IsAgreementAccepted = true;

        await vm.SkipCommand.ExecuteAsync(null);

        Assert.True(exitRequested);
    }

    [Fact]
    public void LauncherAgreement_IsCurrent_OnlyWhenStoredVersionMatches()
    {
        Assert.False(LauncherAgreement.IsCurrent(null));
        Assert.False(LauncherAgreement.IsCurrent("2020-01-01"));
        Assert.True(LauncherAgreement.IsCurrent(LauncherAgreement.CurrentVersion));
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
    public async Task GoToStepCommand_ToCompletedStep_NavigatesBack()
    {
        var vm = CreateViewModel();
        vm.GamePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        AdvanceToGamePath(vm);
        await WaitForGamePathStatusAsync(vm, SetupWizardGamePathStatus.AvailableForInstallation);
        vm.NextCommand.Execute(null);
        Assert.Equal(4, vm.Step);

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
        AdvanceToGamePath(vm);

        await WaitForGamePathStatusAsync(vm, SetupWizardGamePathStatus.AvailableForInstallation);

        Assert.True(vm.IsGamePathReady);
        Assert.True(vm.CanGoNext);
    }

    [Fact]
    public async Task GamePathStatus_WhenStateFilesAreValid_IsValidInstallationAndCanGoNext()
    {
        using var gamePath = TestDirectory.Create();
        var normalizedGamePath = new GameInstallationPath(LauncherProfiles.BlueArchiveJapan).NormalizeGamePath(gamePath);
        var store = new LocalInstallationStateStore(LauncherProfiles.BlueArchiveJapan);
        Directory.CreateDirectory(normalizedGamePath);
        await store.CommitAsync(normalizedGamePath, CreateCommit());
        var vm = new SetupWizardViewModel(new LocalizationService(), new GameInstallationPath(LauncherProfiles.BlueArchiveJapan), store, new LocalDiagnostics(), new StubFilePickerService())
        {
            GamePath = gamePath
        };

        AdvanceToGamePath(vm);
        await WaitForGamePathStatusAsync(vm, SetupWizardGamePathStatus.ValidInstallation);

        Assert.True(vm.IsGamePathReady);
        Assert.True(vm.CanGoNext);
    }

    [Fact]
    public async Task GamePathStatus_WhenOnlyManifestExists_IsCorruptedInstallationAndCannotGoNext()
    {
        using var gamePath = TestDirectory.Create();
        var normalizedGamePath = new GameInstallationPath(LauncherProfiles.BlueArchiveJapan).NormalizeGamePath(gamePath);
        Directory.CreateDirectory(normalizedGamePath);
        await File.WriteAllTextAsync(Path.Combine(normalizedGamePath, "manifest.json"), "{}");
        var vm = CreateViewModel();
        vm.GamePath = gamePath;
        AdvanceToGamePath(vm);

        await WaitForGamePathStatusAsync(vm, SetupWizardGamePathStatus.CorruptedInstallation);

        Assert.False(vm.IsGamePathReady);
        Assert.False(vm.CanGoNext);
    }

    [Fact]
    public async Task GamePathPresentation_WhenInstallationIsCorrupted_HasTitleAndDescription()
    {
        using var gamePath = TestDirectory.Create();
        var normalizedGamePath = new GameInstallationPath(LauncherProfiles.BlueArchiveJapan).NormalizeGamePath(gamePath);
        Directory.CreateDirectory(normalizedGamePath);
        await File.WriteAllTextAsync(Path.Combine(normalizedGamePath, "manifest.json"), "{}");
        var localizer = new LocalizationService();
        var vm = new SetupWizardViewModel(
            localizer,
            new GameInstallationPath(LauncherProfiles.BlueArchiveJapan),
            new LocalInstallationStateStore(LauncherProfiles.BlueArchiveJapan),
            new LocalDiagnostics(), new StubFilePickerService());
        vm.GamePath = gamePath;
        AdvanceToGamePath(vm);

        await WaitForGamePathStatusAsync(vm, SetupWizardGamePathStatus.CorruptedInstallation);

        Assert.Equal(localizer.T(LocalizationKeys.SetupWizardGamePathStatusTitle), vm.GamePathPresentation.Title);
        Assert.Equal(localizer.T(LocalizationKeys.SetupWizardGamePathCorrupted), vm.GamePathPresentation.Description);
    }

    [Fact]
    public async Task GamePathStatus_WhenStateFileIsLocked_IsInaccessibleAndCannotGoNext()
    {
        using var gamePath = TestDirectory.Create();
        var normalizedGamePath = new GameInstallationPath(LauncherProfiles.BlueArchiveJapan).NormalizeGamePath(gamePath);
        var store = new LocalInstallationStateStore(LauncherProfiles.BlueArchiveJapan);
        Directory.CreateDirectory(normalizedGamePath);
        await store.CommitAsync(normalizedGamePath, CreateCommit());
        await using var locked = new FileStream(
            Path.Combine(normalizedGamePath, "manifest.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var vm = new SetupWizardViewModel(new LocalizationService(), new GameInstallationPath(LauncherProfiles.BlueArchiveJapan), store, new LocalDiagnostics(), new StubFilePickerService())
        {
            GamePath = gamePath
        };
        AdvanceToGamePath(vm);

        await WaitForGamePathStatusAsync(vm, SetupWizardGamePathStatus.Inaccessible);

        Assert.False(vm.IsGamePathReady);
        Assert.False(vm.CanGoNext);
    }

    [Fact]
    public async Task GamePathStatus_WhenPathCannotBeNormalized_IsInaccessibleAndCannotGoNext()
    {
        var vm = CreateViewModel();
        vm.GamePath = "\0";
        AdvanceToGamePath(vm);

        await WaitForGamePathStatusAsync(vm, SetupWizardGamePathStatus.Inaccessible);

        Assert.False(vm.IsGamePathReady);
        Assert.False(vm.CanGoNext);
    }

    [Fact]
    public async Task GamePathStatus_WhenChecking_CannotGoNextAndUpdatesWhenReadCompletes()
    {
        using var gamePath = TestDirectory.Create();
        var normalizedGamePath = new GameInstallationPath(LauncherProfiles.BlueArchiveJapan).NormalizeGamePath(gamePath);
        Directory.CreateDirectory(normalizedGamePath);
        var tempFilesWritten = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCommit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new LocalInstallationStateStore(LauncherProfiles.BlueArchiveJapan, async (_, cancellationToken) =>
        {
            tempFilesWritten.TrySetResult();
            await releaseCommit.Task.WaitAsync(cancellationToken);
        });
        var commitTask = store.CommitAsync(normalizedGamePath, CreateCommit());
        await tempFilesWritten.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var vm = new SetupWizardViewModel(new LocalizationService(), new GameInstallationPath(LauncherProfiles.BlueArchiveJapan), store, new LocalDiagnostics(), new StubFilePickerService())
        {
            GamePath = gamePath
        };

        AdvanceToGamePath(vm);
        await WaitForGamePathStatusAsync(vm, SetupWizardGamePathStatus.Checking);
        Assert.False(vm.CanGoNext);

        releaseCommit.TrySetResult();
        await commitTask.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitForGamePathStatusAsync(vm, SetupWizardGamePathStatus.ValidInstallation);
        Assert.True(vm.CanGoNext);
    }

    [Fact]
    public async Task GamePathStatus_WhenOldReadIsCancelled_DoesNotOverwriteNewPathStatus()
    {
        using var oldGamePath = TestDirectory.Create();
        var normalizedOldGamePath = new GameInstallationPath(LauncherProfiles.BlueArchiveJapan).NormalizeGamePath(oldGamePath);
        using var newGamePath = TestDirectory.Create();
        Directory.CreateDirectory(normalizedOldGamePath);
        var tempFilesWritten = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCommit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new LocalInstallationStateStore(LauncherProfiles.BlueArchiveJapan, async (_, cancellationToken) =>
        {
            tempFilesWritten.TrySetResult();
            await releaseCommit.Task.WaitAsync(cancellationToken);
        });
        var commitTask = store.CommitAsync(normalizedOldGamePath, CreateCommit());
        await tempFilesWritten.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var vm = new SetupWizardViewModel(new LocalizationService(), new GameInstallationPath(LauncherProfiles.BlueArchiveJapan), store, new LocalDiagnostics(), new StubFilePickerService())
        {
            GamePath = oldGamePath
        };
        AdvanceToGamePath(vm);
        await WaitForGamePathStatusAsync(vm, SetupWizardGamePathStatus.Checking);

        vm.GamePath = newGamePath;
        await WaitForGamePathStatusAsync(vm, SetupWizardGamePathStatus.AvailableForInstallation);
        releaseCommit.TrySetResult();
        await commitTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(SetupWizardGamePathStatus.AvailableForInstallation, vm.GamePathStatus);
    }

    [Fact]
    public async Task RefreshLocalizedText_RaisesPropertyChangedAndIsNoLongerSelfSubscribed()
    {
        var localizer = new LocalizationService();
        var vm = new SetupWizardViewModel(
            localizer,
            new GameInstallationPath(LauncherProfiles.BlueArchiveJapan),
            new LocalInstallationStateStore(LauncherProfiles.BlueArchiveJapan),
            new LocalDiagnostics(), new StubFilePickerService())
        {
            GamePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
        };
        AdvanceToGamePath(vm);
        await WaitForGamePathStatusAsync(vm, SetupWizardGamePathStatus.AvailableForInstallation);
        var changed = new List<string>();
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SetupWizardViewModel.GamePathStatusText))
            {
                changed.Add(args.PropertyName!);
            }
        };

        // D10：向导不再自订阅 localizer.LanguageChanged——语言变化改由 Shell 遍历
        // 呈现族分发（生产路径经它的宿主 DialogsViewModel）。本地化服务换语言本身
        // 不得再触发向导刷新。
        localizer.SetLanguage(LauncherLanguages.Japanese);
        Assert.Empty(changed);

        vm.RefreshLocalizedText();
        Assert.NotEmpty(changed);
    }

    [Fact]
    public async Task Dispose_WhileGamePathProbeInFlight_CancelsTheProbeToken()
    {
        var store = new BlockingInstallationStateStore();
        var vm = new SetupWizardViewModel(
            new LocalizationService(),
            new GameInstallationPath(LauncherProfiles.BlueArchiveJapan),
            store,
            new LocalDiagnostics(), new StubFilePickerService())
        {
            GamePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
        };
        AdvanceToGamePath(vm);
        await store.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        vm.Dispose();

        // 统一为「取消 → 退订 → 释放」：先取消在飞探测再回收源；若只 Dispose
        // 源而不取消，读任务将永远悬着（旧实现的行为）。
        Assert.True(store.ProbeToken.IsCancellationRequested);
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

    private static async Task AdvanceToLastStep(SetupWizardViewModel viewModel)
    {
        // 协议步骤是硬门禁：先按“读完 + 勾选”通过，再让路径校验决定后续步进。
        viewModel.MarkAgreementRead();
        viewModel.IsAgreementAccepted = true;

        // 路径校验为异步（防抖 + 后台写探测），推进循环在门控未就绪时
        // 有界等待而非热自旋：5 秒预算内未就绪即快速失败（参见 P0 整改
        // 中 Headless 同类修复）。
        await TestWait.UntilAsync(
            () => viewModel.IsLastStep || viewModel.CanGoNext,
            GateSettleBudget,
            "向导门控未在 5 秒预算内就绪。");

        for (var guard = 0; !viewModel.IsLastStep && guard < 100; guard++)
        {
            if (viewModel.CanGoNext)
            {
                viewModel.NextCommand.Execute(null);
            }
            else
            {
                await TestWait.UntilAsync(
                    () => viewModel.IsLastStep || viewModel.CanGoNext,
                    GateSettleBudget,
                    "向导门控未在 5 秒预算内就绪。");
            }
        }

        Assert.True(viewModel.IsLastStep, "向导未在上限步数内推进到末步。");
    }

    private static LocalInstallationStateCommit CreateCommit() => new(
        "1.2.3",
        "manifest.json",
        "BlueArchive",
        ["--test"],
        [new LocalInstallationFile("BlueArchive.exe", 4, "1234")]);

    /// <summary>读取被门控挂起的状态仓库替身：用于观察释放时探测令牌是否被取消。</summary>
    private sealed class BlockingInstallationStateStore : ILocalInstallationStateStore
    {
        public TaskCompletionSource ReadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationToken ProbeToken { get; private set; }

        public async Task<LocalInstallationState> ReadAsync(
            string gamePath,
            CancellationToken cancellationToken = default)
        {
            ProbeToken = cancellationToken;
            ReadStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Probe cancellation was not observed.");
        }

        public Task<LocalInstallationState> CommitAsync(
            string gamePath,
            LocalInstallationStateCommit commit,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<LocalInstallationState> DeleteAsync(
            string gamePath,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
