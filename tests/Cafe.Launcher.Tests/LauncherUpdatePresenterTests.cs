using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Core.Constants;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Services;
using Cafe.Launcher.Core.Services.Diagnostics;
using Cafe.Launcher.Core.Services.Update;
using Cafe.Launcher.Testing;
using Cafe.Launcher.UI.Features.SetupWizard;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.UI.ViewModels;
using Xunit;

namespace Cafe.Launcher.Tests;

/// <summary>
/// 更新结果的 trigger 策略表：启动检查只呈现「有更新」，失败与已最新保持静默；手动检查三态都呈现。
/// 启动 toast 的动作复用本次结果开对话框，不发起第二次检查。
/// </summary>
[Collection(nameof(LocalizationServiceTestIsolation))]
public sealed class LauncherUpdatePresenterTests : IDisposable
{
    private readonly TestDirectory tempDirectory = TestDirectory.Create();

    public void Dispose() => tempDirectory.Dispose();

    [Fact]
    public void Present_StartupFailure_StaysSilent()
    {
        var context = CreateContext(LauncherUpdateInAppAvailability.Available);

        context.Presenter.Present(
            LauncherUpdateCheckResult.Failed(message: "simulated check failure"),
            LauncherUpdateTrigger.Startup);

        Assert.Empty(context.Toasts);
    }

    [Fact]
    public void Present_StartupUpToDate_StaysSilent()
    {
        var context = CreateContext(LauncherUpdateInAppAvailability.Available);

        context.Presenter.Present(
            LauncherUpdateCheckResult.Succeeded("1.0.0", [], isUpdateAvailable: false),
            LauncherUpdateTrigger.Startup);

        Assert.Empty(context.Toasts);
    }

    [Fact]
    public void Present_StartupUpdateAvailable_ShowsExtendedInfoToast()
    {
        var context = CreateContext(LauncherUpdateInAppAvailability.Available);

        context.Presenter.Present(
            LauncherUpdateCheckResult.Succeeded("9.9.9", CreateFiles(), isUpdateAvailable: true),
            LauncherUpdateTrigger.Startup);

        var toast = Assert.Single(context.Toasts);
        Assert.Equal(ToastSeverity.Info, toast.Severity);
        Assert.Equal(ToastDuration.Extended, toast.Duration);
        Assert.Contains("9.9.9", toast.Message, StringComparison.Ordinal);
        Assert.NotNull(toast.PrimaryAction);
    }

    [Fact]
    public async Task Present_StartupUpdateAction_OpensDialogFromTheSameResult()
    {
        // 动作必须复用 toast 对应的结果：再次检查会让「有更新」与随后的失败或「已最新」互相矛盾。
        var context = CreateContext(LauncherUpdateInAppAvailability.PlatformUnsupported);

        context.Presenter.Present(
            LauncherUpdateCheckResult.Succeeded(
                "9.9.9",
                CreateFiles(),
                isUpdateAvailable: true,
                releaseNotes: "release notes"),
            LauncherUpdateTrigger.Startup);

        var toast = Assert.Single(context.Toasts);
        var outcome = await toast.PrimaryAction!.ExecuteAsync(CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.True(context.Dialogs.IsUpdateAvailableVisible);
        Assert.Equal("9.9.9", context.Dialogs.UpdateAvailableVersion);
        Assert.Equal("release notes", context.Dialogs.UpdateReleaseNotes);
        Assert.False(context.Dialogs.UpdateSupportsInAppApply);
        Assert.Equal(
            LauncherUpdateInAppAvailability.PlatformUnsupported,
            context.Dialogs.UpdateUnavailableReason);
        Assert.Equal(1, context.SelfUpdateService.ResolveCallCount);
    }

    [Fact]
    public void Present_ManualFailure_ShowsErrorToast()
    {
        var context = CreateContext(LauncherUpdateInAppAvailability.Available);

        context.Presenter.Present(
            LauncherUpdateCheckResult.Failed(message: "simulated check failure"),
            LauncherUpdateTrigger.Manual);

        var toast = Assert.Single(context.Toasts);
        Assert.Equal(ToastSeverity.Error, toast.Severity);
        Assert.Contains("simulated check failure", toast.Message, StringComparison.Ordinal);
    }

    private PresenterContext CreateContext(LauncherUpdateInAppAvailability availability)
    {
        var localizer = new LocalizationService();
        var toastService = new ToastService();
        var toasts = new List<ToastNotification>();
        toastService.ToastRaised += toasts.Add;
        var dialogs = new DialogsViewModel(
            LauncherProfiles.Cafe,
            localizer,
            new NoticeStateService(TestDataRoot.ForDirectory(tempDirectory.Sub(Guid.NewGuid().ToString("N")))),
            new SetupWizardViewModel(
                localizer,
                new GameInstallationPath(LauncherProfiles.BlueArchiveJapan),
                new LocalInstallationStateStore(LauncherProfiles.BlueArchiveJapan),
                new LocalDiagnostics(),
                new StubFilePickerService()),
            new LocalDiagnostics(),
            action =>
            {
                action();
                return Task.CompletedTask;
            });
        var selfUpdateService = new StubSelfUpdateService(availability);
        var presenter = new LauncherUpdatePresenter(
            localizer,
            toastService,
            dialogs,
            selfUpdateService);

        return new PresenterContext(presenter, toasts, dialogs, selfUpdateService);
    }

    private static IReadOnlyList<ReleaseFile> CreateFiles() =>
    [
        new ReleaseFile
        {
            Name = "Cafe.Launcher_v9.9.9.zip",
            Url = "https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia/releases/download/v9.9.9/Cafe.Launcher_v9.9.9.zip",
            Size = 100
        }
    ];

    private sealed record PresenterContext(
        LauncherUpdatePresenter Presenter,
        IReadOnlyList<ToastNotification> Toasts,
        DialogsViewModel Dialogs,
        StubSelfUpdateService SelfUpdateService);

    private sealed class StubSelfUpdateService(LauncherUpdateInAppAvailability availability)
        : ILauncherSelfUpdateService
    {
        public int ResolveCallCount { get; private set; }

        public LauncherUpdateInAppAvailability ResolveInAppAvailability(IReadOnlyList<ReleaseFile> files)
        {
            ResolveCallCount++;
            return availability;
        }

        public Task<LauncherSelfUpdatePreparation> PrepareAsync(
            IReadOnlyList<ReleaseFile> files,
            string version,
            IProgress<LauncherUpdateProgress>? progress,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
