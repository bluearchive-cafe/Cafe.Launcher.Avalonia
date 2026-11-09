using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Services;
using Cafe.Launcher.Core.Services.Update;
using Cafe.Launcher.Testing;
using Cafe.Launcher.UI.Features.Shell;

namespace Cafe.Launcher.Tests;

/// <summary>
/// 协调器只负责把准备结果翻译成壳的终态事件；「下载失败」与「目标地址被拒」
/// 必须各走各的展示分支，否则 #33 里可行动的 hosts/代理指引会退化成笼统网络失败。
/// </summary>
public sealed class ShellSelfUpdateCoordinatorTests
{
    [Fact]
    public async Task Begin_WhenTheTargetWasRejected_ReportsDownloadBlocked()
    {
        var service = new ScriptedSelfUpdateService(
            LauncherSelfUpdatePreparation.Failed(
                "Remote URL resolves to a blocked network address. Blocked: 127.0.0.1",
                LauncherSelfUpdateFailureKind.UrlRejected));
        var coordinator = CreateCoordinator(service);
        var finished = Subscribe(coordinator);

        coordinator.Begin("9.9.9", []);

        var result = await finished.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(ShellSelfUpdateOutcome.DownloadBlocked, result.Outcome);
        Assert.Null(result.Exception);
    }

    [Theory]
    [InlineData(LauncherSelfUpdateFailureKind.Network)]
    [InlineData(LauncherSelfUpdateFailureKind.Io)]
    [InlineData(LauncherSelfUpdateFailureKind.Verification)]
    public async Task Begin_WhenTheFailureIsNotAnAddressRejection_ReportsDownloadFailed(
        LauncherSelfUpdateFailureKind failureKind)
    {
        var service = new ScriptedSelfUpdateService(
            LauncherSelfUpdatePreparation.Failed("download failed", failureKind));
        var coordinator = CreateCoordinator(service);
        var finished = Subscribe(coordinator);

        coordinator.Begin("9.9.9", []);

        var result = await finished.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(ShellSelfUpdateOutcome.DownloadFailed, result.Outcome);
        Assert.Null(result.Exception);
    }

    [Fact]
    public async Task Begin_WhenThePackageIsReady_ReportsReadyAndStagesThePreparation()
    {
        var preparation = LauncherSelfUpdatePreparation.Ready(
            LauncherUpdateTarget.WindowsPortable,
            "C:/staged/pkg.zip",
            new string('a', 64));
        var coordinator = CreateCoordinator(new ScriptedSelfUpdateService(preparation));
        var finished = Subscribe(coordinator);

        coordinator.Begin("9.9.9", []);

        var result = await finished.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(ShellSelfUpdateOutcome.Ready, result.Outcome);
        Assert.Same(preparation, coordinator.PendingPreparation);
    }

    private static ShellSelfUpdateCoordinator CreateCoordinator(ILauncherSelfUpdateService service) =>
        new(service, new StubWindowsLauncherUpdateApplier(), CancellationToken.None);

    private static Task<(ShellSelfUpdateOutcome Outcome, Exception? Exception)> Subscribe(
        ShellSelfUpdateCoordinator coordinator)
    {
        var completion = new TaskCompletionSource<(ShellSelfUpdateOutcome Outcome, Exception? Exception)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.Finished += (outcome, exception) => completion.TrySetResult((outcome, exception));
        return completion.Task;
    }

    private sealed class ScriptedSelfUpdateService(LauncherSelfUpdatePreparation preparation)
        : ILauncherSelfUpdateService
    {
        public LauncherUpdateInAppAvailability ResolveInAppAvailability(IReadOnlyList<ReleaseFile> files) =>
            LauncherUpdateInAppAvailability.Available;

        public Task<LauncherSelfUpdatePreparation> PrepareAsync(
            IReadOnlyList<ReleaseFile> files,
            string version,
            IProgress<LauncherUpdateProgress>? progress,
            CancellationToken cancellationToken) => Task.FromResult(preparation);
    }
}
