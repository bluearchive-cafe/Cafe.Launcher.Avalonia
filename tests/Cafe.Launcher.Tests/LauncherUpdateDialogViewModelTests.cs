using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.UI.Constants;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.Core.Services;
using Cafe.Launcher.Core.Services.Update;
using Cafe.Launcher.UI.ViewModels;
using Cafe.Launcher.Testing;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Constants;

namespace Cafe.Launcher.Tests;

[Collection(nameof(LocalizationServiceTestIsolation))]
public sealed class LauncherUpdateDialogViewModelTests
{
    private static LauncherUpdateDialogViewModel CreateViewModel() =>
        new(LauncherProfiles.Cafe, new LocalizationService());

    [Fact]
    public void Show_WithoutSelfUpdate_EnablesBrowserHandOffImmediately()
    {
        var localizer = new LocalizationService();
        var viewModel = CreateViewModel();
        var files = CreateFiles();

        viewModel.Show(
            "1.2.0", files, LauncherUpdateInAppAvailability.PlatformUnsupported);

        Assert.True(viewModel.IsVisible);
        Assert.Equal("1.2.0", viewModel.Version);
        Assert.False(viewModel.SupportsInAppApply);
        Assert.True(viewModel.CanConfirm);
        // 主动作文案是「前往发布页」而不是「下载」：浏览器交接分支的措辞即出口语义。
        Assert.Equal(
            localizer.T(LocalizationKeys.LauncherUpdateOpenReleasePage),
            viewModel.PrimaryActionText);
    }

    [Fact]
    public void Show_WithReleaseNotes_ExposesMarkdownPreview()
    {
        var viewModel = CreateViewModel();

        viewModel.Show(
            "1.2.0",
            CreateFiles(),
            LauncherUpdateInAppAvailability.Available,
            releaseNotes: "## Highlights\n\n![Banner](https://example.com/banner.png)\n\n[Details](https://example.com/release)");

        Assert.True(viewModel.HasReleaseNotes);
        Assert.Equal("## Highlights\n\n![Banner](https://example.com/banner.png)\n\n[Details](https://example.com/release)", viewModel.ReleaseNotes);
    }

    [Theory]
    [InlineData("## Loaded notes", false)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    public async Task LoadReleaseNotesAsync_WhenCompleted_ReplacesLoadingWithContentOrFailure(string notes, bool failed)
    {
        using var viewModel = CreateViewModel();
        var service = new ControlledUpdateService();
        viewModel.Show("1.2.0", CreateFiles(), LauncherUpdateInAppAvailability.Available);

        var loading = viewModel.LoadReleaseNotesAsync(service);

        Assert.True(viewModel.IsVisible);
        Assert.True(viewModel.IsReleaseNotesLoading);
        Assert.False(viewModel.HasReleaseNotesFailed);
        Assert.True(viewModel.CanConfirm);
        service.Result.SetResult(notes);
        await loading;

        Assert.False(viewModel.IsReleaseNotesLoading);
        Assert.Equal(failed, viewModel.HasReleaseNotesFailed);
        Assert.Equal(!failed, viewModel.HasReleaseNotes);
        Assert.True(viewModel.CanConfirm);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoadReleaseNotesAsync_WhenClosedOrReplaced_CancelsAndIgnoresLateResult(bool replace)
    {
        using var viewModel = CreateViewModel();
        var service = new ControlledUpdateService();
        viewModel.Show("1.2.0", [], LauncherUpdateInAppAvailability.Available);
        var loading = viewModel.LoadReleaseNotesAsync(service);

        if (replace)
            viewModel.Show("1.3.0", [], LauncherUpdateInAppAvailability.Available, "new notes");
        else
            viewModel.CancelCommand.Execute(null);
        Assert.True(service.Token.IsCancellationRequested);
        service.Result.SetResult("old notes");
        await loading;

        Assert.Equal(replace ? "new notes" : "", viewModel.ReleaseNotes);
        Assert.Equal(replace, viewModel.IsVisible);
    }

    [Fact]
    public async Task LoadReleaseNotesAsync_WithInlineNotes_DoesNotRequestAgain()
    {
        using var viewModel = CreateViewModel();
        var service = new ControlledUpdateService();
        viewModel.Show("1.2.0", [], LauncherUpdateInAppAvailability.Available, "inline notes");

        await viewModel.LoadReleaseNotesAsync(service);

        Assert.False(service.WasRequested);
        Assert.False(viewModel.IsReleaseNotesLoading);
        Assert.False(viewModel.HasReleaseNotesFailed);
    }

    [Fact]
    public void OpenReleasePage_WhenNotesFailed_KeepsUpdateActionsAvailable()
    {
        using var viewModel = CreateViewModel();
        string? opened = null;
        viewModel.OpenReleasePageRequested += url => opened = url;
        viewModel.Show("1.2.0", [], LauncherUpdateInAppAvailability.Available);

        viewModel.OpenReleasePageCommand.Execute(null);

        Assert.Equal(LauncherProfiles.Cafe.GitHubReleasesPageUrl, opened);
        Assert.True(viewModel.IsVisible);
        Assert.True(viewModel.CanConfirm);
    }

    [Fact]
    public void Confirm_WithoutSelfUpdate_OpensReleasePageAndCloses()
    {
        var viewModel = CreateViewModel();
        string? requestedUrl = null;
        viewModel.OpenReleasePageRequested += url => requestedUrl = url;
        viewModel.Show(
            "1.2.0", CreateFiles(), LauncherUpdateInAppAvailability.PackageUnverifiable);

        viewModel.ConfirmCommand.Execute(null);

        Assert.False(viewModel.IsVisible);
        Assert.Equal(LauncherProfiles.Cafe.GitHubReleasesPageUrl, requestedUrl);
    }

    [Fact]
    public void Show_WhenSelfUpdateSupported_EnablesInAppFlow()
    {
        var viewModel = CreateViewModel();

        viewModel.Show("1.2.0", CreateFiles(), LauncherUpdateInAppAvailability.Available);

        Assert.True(viewModel.SupportsInAppApply);
        Assert.True(viewModel.CanConfirm);
        Assert.False(viewModel.IsApplying);
    }

    [Theory]
    [InlineData(
        LauncherUpdateInAppAvailability.PlatformUnsupported,
        LocalizationKeys.LauncherUpdateInAppUnavailablePlatform)]
    [InlineData(
        LauncherUpdateInAppAvailability.HelperMissing,
        LocalizationKeys.LauncherUpdateInAppUnavailableHelper)]
    [InlineData(
        LauncherUpdateInAppAvailability.PackageUnverifiable,
        LocalizationKeys.LauncherUpdateInAppUnavailablePackage)]
    public void Show_WhenInAppApplyIsUnavailable_NamesTheCauseTheVerdictReported(
        LauncherUpdateInAppAvailability availability,
        LocalizationKeys expectedKey)
    {
        var localizer = new LocalizationService();
        var viewModel = CreateViewModel();

        viewModel.Show("1.2.0", CreateFiles(), availability);

        // 「前往发布页」必须说清是哪一种「不行」：平台、这份安装，还是这一版发布。
        Assert.True(viewModel.IsInAppUnavailable);
        Assert.Equal(localizer.T(expectedKey), viewModel.InAppUnavailableNoticeText);
        Assert.NotEmpty(viewModel.InAppUnavailableNoticeText);
    }

    [Fact]
    public void Show_ForEveryUnavailableCause_StaysThreeDistinctExplanations()
    {
        var viewModel = CreateViewModel();
        var notices = new List<string>();

        foreach (var availability in new[]
                 {
                     LauncherUpdateInAppAvailability.PlatformUnsupported,
                     LauncherUpdateInAppAvailability.HelperMissing,
                     LauncherUpdateInAppAvailability.PackageUnverifiable
                 })
        {
            viewModel.Show("1.2.0", CreateFiles(), availability);
            notices.Add(viewModel.InAppUnavailableNoticeText);
        }

        // 三种原因必须说三件事：压回一句话就是又回到「此设备无法…」那种笼统归因。
        Assert.Equal(3, notices.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Show_WhenAvailable_ShowsNoHandOffNotice()
    {
        var viewModel = CreateViewModel();

        viewModel.Show("1.2.0", CreateFiles(), LauncherUpdateInAppAvailability.Available);

        Assert.False(viewModel.IsInAppUnavailable);
        Assert.Empty(viewModel.InAppUnavailableNoticeText);
    }

    [Fact]
    public void MarkFailed_AfterAnAttempt_ReportsNoCapabilityVerdict()
    {
        var viewModel = CreateViewModel();
        viewModel.Show("1.2.0", CreateFiles(), LauncherUpdateInAppAvailability.Available);
        viewModel.BeginApply();

        viewModel.MarkFailed();

        // 一次失败不是能力判定：失败缘由由 toast 说明，对话框不能改口归因给平台或安装。
        Assert.False(viewModel.SupportsInAppApply);
        Assert.False(viewModel.IsInAppUnavailable);
        Assert.Empty(viewModel.InAppUnavailableNoticeText);
    }

    [Fact]
    public void RefreshLocalizedText_WhileTheHandOffNoticeIsVisible_RecomputesItsText()
    {
        var viewModel = CreateViewModel();
        viewModel.Show(
            "1.2.0", CreateFiles(), LauncherUpdateInAppAvailability.HelperMissing);
        var notified = false;
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(LauncherUpdateDialogViewModel.InAppUnavailableNoticeText))
            {
                notified = true;
            }
        };

        viewModel.RefreshLocalizedText();

        Assert.True(notified, "切换语言后说明行未通告重算。");
    }

    [Fact]
    public void Confirm_WhenSelfUpdateSupported_RaisesStart()
    {
        var viewModel = CreateViewModel();
        var raised = 0;
        viewModel.SelfUpdateStartRequested += (_, _) => raised++;
        viewModel.Show("1.2.0", CreateFiles(), LauncherUpdateInAppAvailability.Available);

        viewModel.ConfirmCommand.Execute(null);

        Assert.Equal(1, raised);
    }

    [Fact]
    public void ApplyStates_ToggleConfirmAndStatusText()
    {
        var viewModel = CreateViewModel();
        viewModel.Show("1.2.0", CreateFiles(), LauncherUpdateInAppAvailability.Available);

        viewModel.BeginApply();

        Assert.True(viewModel.IsApplying);
        Assert.True(viewModel.IsDownloading);
        Assert.False(viewModel.CanConfirm);

        viewModel.ReportProgress(0.5, 5 * 1024 * 1024);
        Assert.Equal(50d, viewModel.Progress);
        Assert.Equal("5MB/s", viewModel.DownloadSpeedText);

        viewModel.MarkReady();

        Assert.False(viewModel.IsDownloading);
        Assert.True(viewModel.IsReadyToRestart);
        Assert.True(viewModel.CanConfirm);
        Assert.Equal(100d, viewModel.Progress);
        Assert.Empty(viewModel.DownloadSpeedText);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.StatusText));
    }

    [Fact]
    public void Confirm_WhenReady_RaisesApply()
    {
        var viewModel = CreateViewModel();
        var applied = 0;
        viewModel.ApplyRequested += () => applied++;
        viewModel.Show("1.2.0", CreateFiles(), LauncherUpdateInAppAvailability.Available);
        viewModel.BeginApply();
        viewModel.MarkReady();

        viewModel.ConfirmCommand.Execute(null);

        Assert.Equal(1, applied);
    }

    [Fact]
    public void MarkFailed_ChangesPrimaryActionToReleasePage()
    {
        var localizer = new LocalizationService();
        var viewModel = CreateViewModel();
        string? requestedUrl = null;
        viewModel.OpenReleasePageRequested += url => requestedUrl = url;
        viewModel.Show("1.2.0", CreateFiles(), LauncherUpdateInAppAvailability.Available);
        viewModel.BeginApply();

        viewModel.MarkFailed();

        Assert.False(viewModel.IsApplying);
        Assert.False(viewModel.SupportsInAppApply);
        Assert.Equal(
            localizer.T(LocalizationKeys.LauncherUpdateOpenReleasePage),
            viewModel.PrimaryActionText);

        viewModel.ConfirmCommand.Execute(null);

        Assert.Equal(LauncherProfiles.Cafe.GitHubReleasesPageUrl, requestedUrl);
        Assert.False(viewModel.IsVisible);
    }

    [Fact]
    public void Cancel_WhileApplying_RaisesCancelAndResets()
    {
        var viewModel = CreateViewModel();
        var cancelled = 0;
        viewModel.CancelRequested += () => cancelled++;
        viewModel.Show("1.2.0", CreateFiles(), LauncherUpdateInAppAvailability.Available);
        viewModel.BeginApply();

        viewModel.CancelCommand.Execute(null);

        Assert.Equal(1, cancelled);
        Assert.False(viewModel.IsVisible);
        Assert.False(viewModel.IsApplying);
    }

    [Fact]
    public void Show_WhenReopened_InAppConfirmCarriesLatestFiles()
    {
        var viewModel = CreateViewModel();
        viewModel.Show("1.2.0", CreateFiles(), LauncherUpdateInAppAvailability.Available);
        viewModel.CancelCommand.Execute(null);

        Assert.False(viewModel.IsVisible);

        var secondFiles = new[]
        {
            new ReleaseFile
            {
                Name = "Cafe.Launcher_v1.3.0.zip",
                Url = "https://example.com/Cafe.Launcher_v1.3.0.zip",
                Size = 7000000
            }
        };
        IReadOnlyList<ReleaseFile>? requestedFiles = null;
        viewModel.SelfUpdateStartRequested += (_, files) => requestedFiles = files;
        viewModel.Show("1.3.0", secondFiles, LauncherUpdateInAppAvailability.Available);

        viewModel.ConfirmCommand.Execute(null);

        // 文件清单不再作为可选项暴露，但应用内更新确认时必须携带最新一轮的文件。
        Assert.Equal(secondFiles, requestedFiles);
    }

    private sealed class ControlledUpdateService : ILauncherUpdateService
    {
        public TaskCompletionSource<string> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token { get; private set; }
        public bool WasRequested { get; private set; }

        public Task<LauncherUpdateCheckResult> CheckForUpdateAsync(string updateChannel, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<string> LoadReleaseNotesAsync(string version, CancellationToken cancellationToken = default)
        {
            Token = cancellationToken;
            WasRequested = true;
            return Result.Task;
        }
    }

    private static ReleaseFile[] CreateFiles() =>
    [
        new()
        {
            Name = "Cafe.Launcher_v1.2.0.zip",
            Url = "https://example.com/Cafe.Launcher_v1.2.0.zip",
            Size = 5000000
        },
        new()
        {
            Name = "Cafe.Launcher_Setup_v1.2.0.exe",
            Url = "https://example.com/Cafe.Launcher_Setup_v1.2.0.exe",
            Size = 6000000
        }
    ];
}
