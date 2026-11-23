using Cafe.Launcher.Composition;
using Cafe.Launcher.Core.Constants;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Services;
using Cafe.Launcher.Core.Services.Diagnostics;
using Cafe.Launcher.Testing;
using Cafe.Launcher.UI.Features.Games;
using Cafe.Launcher.UI.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Cafe.Launcher.Tests;

[Collection(nameof(LocalizationServiceTestIsolation))]
public sealed class GameLibraryViewModelTests : IDisposable
{
    private readonly TestDirectory directory = TestDirectory.Create();
    private readonly UnifiedLogger logger;
    private readonly ServiceProvider services;

    public GameLibraryViewModelTests()
    {
        logger = new UnifiedLogger(Path.Combine(directory, "logs"));
        var registrations = new ServiceCollection();
        registrations.AddLauncherServices(logger, launcherDataRoot: directory.DataRoot);
        services = registrations.BuildServiceProvider();
    }

    [Fact]
    public async Task TrySelectGameAsync_WhenSelectionChanges_RetainsBothFamiliesAndPersistsExplicitIdentity()
    {
        var games = services.GetRequiredService<GameLibraryViewModel>();
        var original = games.Active;
        original.Operations.ProgressValue = 42;
        var stella = games.Get(LauncherProfiles.StellaSoraChina.RuntimeId);
        Assert.False(stella.Operations.IsPresentationActive);

        Assert.True(await games.TrySelectGameAsync(stella.GameId));
        Assert.Same(stella, games.Active);
        Assert.False(original.Operations.IsPresentationActive);
        Assert.True(stella.Operations.IsPresentationActive);
        Assert.False(games.IsResourcePanelAvailable);
        Assert.True(await games.TrySelectGameAsync(original.GameId));

        Assert.Same(original, games.Active);
        Assert.Equal(42, original.Operations.ProgressValue);
        Assert.True(games.IsResourcePanelAvailable);
        Assert.Equal(2, games.Entries.Count);
        var saved = await services.GetRequiredService<ILauncherSettingsService>().ReadAsync();
        Assert.Equal(original.GameId, saved.LastViewedGameId);
        Assert.NotSame(original.Core.ProcessTracker, stella.Core.ProcessTracker);
        Assert.NotSame(original.Core.Runtime, stella.Core.Runtime);
        Assert.NotSame(original.Download, stella.Download);
        Assert.NotSame(original.SessionMonitor, stella.SessionMonitor);
    }

    [Fact]
    public async Task TrySelectGameAsync_WhenModalOrDirtyDraftBlocksSelection_DoesNotPersistOrRefresh()
    {
        var games = services.GetRequiredService<GameLibraryViewModel>();
        var original = games.Active;
        var refreshed = false;
        games.CanSwitch = () => false;
        games.RefreshRequested = (_, _) =>
        {
            refreshed = true;
            return Task.CompletedTask;
        };

        Assert.False(await games.TrySelectGameAsync(LauncherProfiles.StellaSoraChina.RuntimeId));

        Assert.Same(original, games.Active);
        Assert.False(refreshed);
        Assert.False(File.Exists(directory.DataRoot.SettingsPath));
    }

    [Fact]
    public async Task TrySelectGameAsync_WhenModalAppearsDuringPersistence_RestoresSavedSelectionAndKeepsOriginalFamily()
    {
        var writer = new DelayedSelectionWriter(services.GetRequiredService<ISavedSettingsWriter>());
        using var games = new GameLibraryViewModel(services.GetRequiredService<GamePresentationFactory>(), writer,
            services.GetRequiredService<IGameFileOperationCoordinator>(), services.GetRequiredService<LocalizationService>(),
            services.GetRequiredService<ToastService>(), services.GetRequiredService<IErrorHandlingService>());
        var original = games.Active;
        var canSwitch = true;
        var refreshed = false;
        games.CanSwitch = () => canSwitch;
        games.RefreshRequested = (_, _) =>
        {
            refreshed = true;
            return Task.CompletedTask;
        };
        var pending = games.TrySelectGameAsync(LauncherProfiles.StellaSoraChina.RuntimeId);
        await writer.FirstWriteCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        canSwitch = false;
        writer.ReleaseFirstWrite.SetResult();

        Assert.False(await pending);
        Assert.Same(original, games.Active);
        Assert.True(original.Operations.IsPresentationActive);
        Assert.False(games.Get(LauncherProfiles.StellaSoraChina.RuntimeId).Operations.IsPresentationActive);
        Assert.False(refreshed);
        Assert.Equal(original.GameId, (await services.GetRequiredService<ILauncherSettingsService>().ReadAsync()).LastViewedGameId);
    }

    [Fact]
    public async Task TrySelectGameAsync_WhenPriorTargetsRefreshFinishesLate_DoesNotSwitchDisplayOrOverwriteOtherFamily()
    {
        var games = services.GetRequiredService<GameLibraryViewModel>();
        var original = games.Active;
        original.Operations.ProgressValue = 12;
        original.Snapshot = new LauncherStatusSnapshot { GameId = original.GameId };
        var refreshEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRefresh = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        games.RefreshRequested = async (target, _) =>
        {
            refreshEntered.SetResult();
            await releaseRefresh.Task.WaitAsync(TimeSpan.FromSeconds(5));
            target.Operations.ProgressValue = 77;
        };
        var selectingStella = games.TrySelectGameAsync(LauncherProfiles.StellaSoraChina.RuntimeId);
        await refreshEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        try
        {
            Assert.True(await games.TrySelectGameAsync(original.GameId));
        }
        finally
        {
            releaseRefresh.SetResult();
        }
        await selectingStella.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Same(original, games.Active);
        Assert.Equal(12, original.Operations.ProgressValue);
        Assert.Equal(77, games.Get(LauncherProfiles.StellaSoraChina.RuntimeId).Operations.ProgressValue);
    }

    [Fact]
    public async Task BackgroundTask_WhenViewingAnotherGame_ReturnsTheOwningFamilyUntilItsLeaseEnds()
    {
        var games = services.GetRequiredService<GameLibraryViewModel>();
        var coordinator = services.GetRequiredService<IGameFileOperationCoordinator>();
        var admission = coordinator.TryAcquire(LauncherProfiles.BlueArchiveJapan.RuntimeId, GameFileOperationKind.Download);
        using var lease = Assert.IsAssignableFrom<IGameFileOperationLease>(admission.Lease);

        Assert.True(await games.TrySelectGameAsync(LauncherProfiles.StellaSoraChina.RuntimeId));

        Assert.True(games.HasBackgroundTask);
        Assert.Equal(LauncherProfiles.BlueArchiveJapan.RuntimeId, games.BackgroundTask!.GameId);
        Assert.Equal(GameFileOperationAdmissionCode.Busy,
            coordinator.TryAcquire(LauncherProfiles.StellaSoraChina.RuntimeId, GameFileOperationKind.Repair).Code);
        await games.ShowBackgroundTaskCommand.ExecuteAsync(null);
        Assert.Equal(LauncherProfiles.BlueArchiveJapan.RuntimeId, games.Active.GameId);
        Assert.False(games.HasBackgroundTask);
        lease.Dispose();
        Assert.Null(coordinator.ActiveOperation);
    }

    public void Dispose()
    {
        services.Dispose();
        logger.Dispose();
        directory.Dispose();
    }

    private sealed class DelayedSelectionWriter(ISavedSettingsWriter inner) : ISavedSettingsWriter
    {
        private int updates;
        public TaskCompletionSource FirstWriteCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirstWrite { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<LauncherSettings> SaveDraftAsync(CancellationToken cancellationToken = default) => inner.SaveDraftAsync(cancellationToken);
        public Task<LauncherSettings> SaveScopedDraftAsync(LauncherSettings draft, LauncherSettings baseline,
            string? gameId, CancellationToken cancellationToken = default) =>
            inner.SaveScopedDraftAsync(draft, baseline, gameId, cancellationToken);
        public Task<LauncherSettings> ReplaceAsync(Func<LauncherSettings, LauncherSettings> createReplacement, CancellationToken cancellationToken = default) =>
            inner.ReplaceAsync(createReplacement, cancellationToken);
        public Task<LauncherSettings> ReplaceAsync(LauncherSettings settings, CancellationToken cancellationToken = default) =>
            inner.ReplaceAsync(settings, cancellationToken);
        public Task<LauncherSettings> UpdateGameAsync(string gameId, Action<GameSettings> change, CancellationToken cancellationToken = default) =>
            inner.UpdateGameAsync(gameId, change, cancellationToken);
        public async Task<LauncherSettings> UpdateAsync(Action<LauncherSettings> change, CancellationToken cancellationToken = default)
        {
            var saved = await inner.UpdateAsync(change, cancellationToken);
            if (Interlocked.Increment(ref updates) == 1)
            {
                FirstWriteCompleted.SetResult();
                await ReleaseFirstWrite.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            }
            return saved;
        }
    }
}
