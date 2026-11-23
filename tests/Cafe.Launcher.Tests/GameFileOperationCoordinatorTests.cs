using Cafe.Launcher.Core.Constants;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Services;

namespace Cafe.Launcher.Tests;

public sealed class GameFileOperationCoordinatorTests
{
    [Fact]
    public void ActiveOperationChanged_ThrowingObserver_DoesNotLoseLeaseOrSkipOtherObservers()
    {
        var coordinator = new GameFileOperationCoordinator();
        var notifications = 0;
        coordinator.ActiveOperationChanged += (_, _) => throw new InvalidOperationException("observer failure");
        coordinator.ActiveOperationChanged += (_, _) => notifications++;
        var operation = Acquire(coordinator, BlueArchiveId, GameFileOperationKind.Download);
        Assert.Equal(BlueArchiveId, coordinator.ActiveOperation?.GameId);
        operation.Dispose();
        Assert.Null(coordinator.ActiveOperation);
        Assert.Equal(2, notifications);
        using var next = Acquire(coordinator, StellaId, GameFileOperationKind.Repair);
    }
    [Theory]
    [InlineData("blue-archive-jp", "stella-sora-cn")]
    [InlineData("stella-sora-cn", "blue-archive-jp")]
    public void SettingsWrite_ActiveGameRejected_OtherGameAndGlobalRemainAllowed(string activeId, string otherId)
    {
        var coordinator = new GameFileOperationCoordinator();
        using var operation = Acquire(coordinator, activeId, GameFileOperationKind.Download);
        Assert.False(coordinator.TryBeginSettingsWrite([activeId], out var denied));
        Assert.Null(denied);
        Assert.True(coordinator.TryBeginSettingsWrite([otherId], out var other));
        using var otherWrite = other;
        Assert.True(coordinator.TryBeginSettingsWrite([], out var global));
        using var globalWrite = global;
        Assert.Equal(activeId, coordinator.ActiveOperation?.GameId);
    }

    [Fact]
    public void SettingsWrite_UntilEveryLeaseReleased_BlocksOnlyItsOwnGameWithoutQueueing()
    {
        var coordinator = new GameFileOperationCoordinator();
        Assert.True(coordinator.TryBeginSettingsWrite([BlueArchiveId, BlueArchiveId], out var first));
        Assert.True(coordinator.TryBeginSettingsWrite([BlueArchiveId], out var second));
        using (var other = Acquire(coordinator, StellaId, GameFileOperationKind.Download))
        {
            Assert.Equal(StellaId, coordinator.ActiveOperation?.GameId);
        }
        first!.Dispose();
        first.Dispose();
        Assert.Equal(GameFileOperationAdmissionCode.SettingsChanging,
            coordinator.TryAcquire(BlueArchiveId, GameFileOperationKind.Repair).Code);
        second!.Dispose();
        Assert.Null(coordinator.ActiveOperation);
        using var operation = Acquire(coordinator, BlueArchiveId, GameFileOperationKind.Repair);
        Assert.Equal(BlueArchiveId, coordinator.ActiveOperation?.GameId);
    }

    [Fact]
    public async Task SettingsWrite_RacingWithTaskAdmission_NeverAllowsBothForSameGame()
    {
        var coordinator = new GameFileOperationCoordinator();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        IDisposable? writeLease = null;
        var write = Task.Run(async () =>
        {
            await start.Task;
            return coordinator.TryBeginSettingsWrite([BlueArchiveId], out writeLease);
        });
        var task = Task.Run(async () =>
        {
            await start.Task;
            return coordinator.TryAcquire(BlueArchiveId, GameFileOperationKind.Download);
        });
        start.SetResult();
        var allowedWrite = await write;
        var admission = await task;
        using var persistedWrite = writeLease;
        using var operation = admission.Lease;
        Assert.NotEqual(allowedWrite, admission.IsAllowed);
    }

    [Fact]
    public void ActiveOperationChanged_ObservesCommittedState_OnlyForSuccessfulAdmissionAndFinalRelease()
    {
        var coordinator = new GameFileOperationCoordinator();
        var observed = new List<string?>();
        coordinator.ActiveOperationChanged += (_, _) => observed.Add(coordinator.ActiveOperation?.GameId);
        var operation = Acquire(coordinator, BlueArchiveId, GameFileOperationKind.Download);
        Assert.False(coordinator.TryAcquire(StellaId, GameFileOperationKind.Repair).IsAllowed);
        operation.Dispose();
        operation.Dispose();
        Assert.Equal([BlueArchiveId, null], observed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TryAcquire_WhenRequestsRace_AllowsExactlyOneWithoutReentry(bool sameGame)
    {
        var coordinator = new GameFileOperationCoordinator();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = Task.Run(async () =>
        {
            await start.Task;
            return coordinator.TryAcquire(BlueArchiveId, GameFileOperationKind.Download);
        });
        var second = Task.Run(async () =>
        {
            await start.Task;
            return coordinator.TryAcquire(sameGame ? BlueArchiveId : StellaId, GameFileOperationKind.Repair);
        });
        start.SetResult();
        var results = await Task.WhenAll(first, second);
        var allowed = Assert.Single(results, admission => admission.IsAllowed);
        using var lease = Assert.IsAssignableFrom<IGameFileOperationLease>(allowed.Lease);
        var denied = Assert.Single(results, admission => !admission.IsAllowed);

        Assert.Equal(GameFileOperationAdmissionCode.Allowed, allowed.Code);
        Assert.Null(allowed.BlockingOperation);
        Assert.Equal(GameFileOperationAdmissionCode.Busy, denied.Code);
        Assert.Null(denied.Lease);
        Assert.Equal(lease.Operation, denied.BlockingOperation);
        Assert.Equal(lease.Operation, coordinator.ActiveOperation);
    }

    [Fact]
    public void TryAcquire_WhenBusy_DoesNotQueueTheRejectedRequest()
    {
        var coordinator = new GameFileOperationCoordinator();
        var first = Acquire(coordinator, BlueArchiveId, GameFileOperationKind.Download);

        var denied = coordinator.TryAcquire(StellaId, GameFileOperationKind.Repair);
        first.Dispose();

        Assert.Equal(GameFileOperationAdmissionCode.Busy, denied.Code);
        Assert.Null(denied.Lease);
        Assert.Null(coordinator.ActiveOperation);
        using var requestedAgain = Acquire(coordinator, StellaId, GameFileOperationKind.Repair);
        Assert.Equal(StellaId, coordinator.ActiveOperation!.GameId);
        Assert.NotEqual(first.Operation.OperationId, requestedAgain.Operation.OperationId);
    }

    [Fact]
    public void TryAcquire_WhilePausedJourneyRetainsItsLease_RemainsBusy()
    {
        var coordinator = new GameFileOperationCoordinator();
        using var journey = Acquire(coordinator, BlueArchiveId, GameFileOperationKind.Download);
        var operation = journey.Operation;

        // Pause does not release the journey's lease. A status change alone cannot free this slot.
        var otherGame = coordinator.TryAcquire(StellaId, GameFileOperationKind.Download);
        var sameGame = coordinator.TryAcquire(BlueArchiveId, GameFileOperationKind.Repair);

        Assert.Equal(GameFileOperationAdmissionCode.Busy, otherGame.Code);
        Assert.Equal(GameFileOperationAdmissionCode.Busy, sameGame.Code);
        Assert.Same(operation, coordinator.ActiveOperation);
        Assert.Same(operation, otherGame.BlockingOperation);
        Assert.Same(operation, sameGame.BlockingOperation);
    }

    [Fact]
    public async Task Dispose_WhenOldLeaseIsReleasedAgain_DoesNotReleaseTheNextJourney()
    {
        var coordinator = new GameFileOperationCoordinator();
        var previous = Acquire(coordinator, BlueArchiveId, GameFileOperationKind.Download);
        previous.Dispose();
        using var current = Acquire(coordinator, BlueArchiveId, GameFileOperationKind.Repair);
        Assert.NotEqual(previous.Operation.OperationId, current.Operation.OperationId);

        await Task.Run(previous.Dispose);
        previous.Dispose();

        Assert.Same(current.Operation, coordinator.ActiveOperation);
        Assert.Equal(GameFileOperationAdmissionCode.Busy,
            coordinator.TryAcquire(StellaId, GameFileOperationKind.Uninstall).Code);
        Assert.True(coordinator.TryBeginShutdown(out var whenIdle));
        Assert.False(whenIdle.IsCompleted);
        previous.Dispose();
        Assert.False(whenIdle.IsCompleted);
        current.Dispose();
        await whenIdle;
        Assert.Null(coordinator.ActiveOperation);
    }

    [Theory]
    [InlineData(GameFileOperationKind.Download)]
    [InlineData(GameFileOperationKind.Repair)]
    public async Task TryBeginShutdown_WhenJourneyIsActive_ClosesAdmissionUntilFinalLeaseRelease(GameFileOperationKind kind)
    {
        var coordinator = new GameFileOperationCoordinator();
        using var lease = Acquire(coordinator, StellaId, kind);

        Assert.True(coordinator.TryBeginShutdown(out var whenIdle));
        Assert.True(coordinator.TryBeginShutdown(out var repeatedWhenIdle));

        Assert.True(coordinator.IsShuttingDown);
        Assert.Same(whenIdle, repeatedWhenIdle);
        Assert.False(whenIdle.IsCompleted);
        var denied = coordinator.TryAcquire(BlueArchiveId, GameFileOperationKind.Download);
        Assert.Equal(GameFileOperationAdmissionCode.ShuttingDown, denied.Code);
        Assert.Same(lease.Operation, denied.BlockingOperation);
        Assert.Null(denied.Lease);
        Assert.Same(lease.Operation, coordinator.ActiveOperation);

        lease.Dispose();
        await whenIdle;

        Assert.Null(coordinator.ActiveOperation);
        Assert.True(coordinator.IsShuttingDown);
        Assert.Equal(GameFileOperationAdmissionCode.ShuttingDown,
            coordinator.TryAcquire(StellaId, kind).Code);
    }

    [Fact]
    public async Task TryBeginShutdown_WhenIdle_CompletesImmediatelyAndRejectsNewTasks()
    {
        var coordinator = new GameFileOperationCoordinator();

        Assert.True(coordinator.TryBeginShutdown(out var whenIdle));
        await whenIdle;

        Assert.True(coordinator.IsShuttingDown);
        var denied = coordinator.TryAcquire(BlueArchiveId, GameFileOperationKind.Uninstall);
        Assert.Equal(GameFileOperationAdmissionCode.ShuttingDown, denied.Code);
        Assert.Null(denied.BlockingOperation);
        Assert.Null(coordinator.ActiveOperation);
    }

    [Fact]
    public async Task TryBeginShutdown_WhenAcquisitionRaces_AtomicallyBlocksOrWaitsForThatLease()
    {
        var coordinator = new GameFileOperationCoordinator();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var acquisition = Task.Run(async () =>
        {
            await start.Task;
            return coordinator.TryAcquire(BlueArchiveId, GameFileOperationKind.Download);
        });
        var shutdown = Task.Run(async () =>
        {
            await start.Task;
            var allowed = coordinator.TryBeginShutdown(out var whenIdle);
            return (allowed, whenIdle);
        });
        start.SetResult();
        var admission = await acquisition;
        var (canShutdown, whenIdle) = await shutdown;

        Assert.True(canShutdown);
        Assert.True(coordinator.IsShuttingDown);
        if (admission.IsAllowed)
        {
            using var lease = Assert.IsAssignableFrom<IGameFileOperationLease>(admission.Lease);
            Assert.False(whenIdle.IsCompleted);
            lease.Dispose();
        }
        else
        {
            Assert.Equal(GameFileOperationAdmissionCode.ShuttingDown, admission.Code);
            Assert.Null(admission.Lease);
        }

        await whenIdle;
        Assert.Null(coordinator.ActiveOperation);
    }

    [Fact]
    public async Task TryBeginShutdown_WhenUninstallIsActive_RejectsWithoutClosingAdmission()
    {
        var coordinator = new GameFileOperationCoordinator();
        using var uninstall = Acquire(coordinator, BlueArchiveId, GameFileOperationKind.Uninstall);

        Assert.False(coordinator.TryBeginShutdown(out var whenUninstallEnds));

        Assert.False(coordinator.IsShuttingDown);
        Assert.False(whenUninstallEnds.IsCompleted);
        Assert.Equal(GameFileOperationAdmissionCode.Busy,
            coordinator.TryAcquire(StellaId, GameFileOperationKind.Download).Code);

        uninstall.Dispose();
        await whenUninstallEnds;
        using var next = Acquire(coordinator, StellaId, GameFileOperationKind.Download);
        Assert.False(coordinator.IsShuttingDown);
    }

    [Fact]
    public async Task CancelShutdown_WhenJourneyIsStillActive_PreservesOwnershipAndRestoresFutureAdmission()
    {
        var coordinator = new GameFileOperationCoordinator();
        using var current = Acquire(coordinator, BlueArchiveId, GameFileOperationKind.Repair);
        Assert.True(coordinator.TryBeginShutdown(out var whenIdle));

        coordinator.CancelShutdown();
        coordinator.CancelShutdown();

        Assert.False(coordinator.IsShuttingDown);
        Assert.False(whenIdle.IsCompleted);
        Assert.Same(current.Operation, coordinator.ActiveOperation);
        Assert.Equal(GameFileOperationAdmissionCode.Busy,
            coordinator.TryAcquire(StellaId, GameFileOperationKind.Download).Code);
        current.Dispose();
        await whenIdle;
        using var next = Acquire(coordinator, StellaId, GameFileOperationKind.Download);
        Assert.Same(next.Operation, coordinator.ActiveOperation);
    }

    [Fact]
    public void CancelShutdown_WhenNoJourneyIsActive_RestoresImmediateAdmission()
    {
        var coordinator = new GameFileOperationCoordinator();
        Assert.True(coordinator.TryBeginShutdown(out _));

        coordinator.CancelShutdown();

        using var lease = Acquire(coordinator, BlueArchiveId, GameFileOperationKind.Download);
        Assert.False(coordinator.IsShuttingDown);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData("BlueArchive_JP")]
    [InlineData("BLUE-ARCHIVE-JP")]
    public void TryAcquire_WhenIdentityIsUnknown_RejectsWithoutCreatingAnOperation(string? gameId)
    {
        var coordinator = new GameFileOperationCoordinator();

        var denied = coordinator.TryAcquire(gameId!, GameFileOperationKind.Download);

        Assert.Equal(GameFileOperationAdmissionCode.UnknownGame, denied.Code);
        Assert.False(denied.IsAllowed);
        Assert.Null(denied.Lease);
        Assert.Null(denied.BlockingOperation);
        Assert.Null(coordinator.ActiveOperation);
        Assert.False(coordinator.IsShuttingDown);
    }

    [Fact]
    public void TryAcquire_WhenKindIsUndefined_ThrowsWithoutCreatingAnOperation()
    {
        var coordinator = new GameFileOperationCoordinator();

        Assert.Throws<ArgumentOutOfRangeException>(() => coordinator.TryAcquire(BlueArchiveId, (GameFileOperationKind)999));

        Assert.Null(coordinator.ActiveOperation);
    }

    [Fact]
    public void ActiveOperation_WhenCallerCreatesDifferentSnapshot_KeepsOriginalTargetAndIdentity()
    {
        var coordinator = new GameFileOperationCoordinator();
        using var lease = Acquire(coordinator, BlueArchiveId, GameFileOperationKind.Download);
        var original = lease.Operation;
        var other = original with { GameId = StellaId, Kind = GameFileOperationKind.Uninstall, OperationId = Guid.NewGuid() };

        Assert.Equal(StellaId, other.GameId);
        Assert.Equal(BlueArchiveId, lease.Operation.GameId);
        Assert.Equal(GameFileOperationKind.Download, lease.Operation.Kind);
        Assert.NotEqual(Guid.Empty, original.OperationId);
        Assert.Same(original, coordinator.ActiveOperation);
        var denied = coordinator.TryAcquire(StellaId, GameFileOperationKind.Repair);
        Assert.Same(original, denied.BlockingOperation);
    }

    private static IGameFileOperationLease Acquire(
        GameFileOperationCoordinator coordinator,
        string gameId,
        GameFileOperationKind kind)
    {
        var admission = coordinator.TryAcquire(gameId, kind);
        Assert.Equal(GameFileOperationAdmissionCode.Allowed, admission.Code);
        return Assert.IsAssignableFrom<IGameFileOperationLease>(admission.Lease);
    }

    private static string BlueArchiveId => LauncherProfiles.BlueArchiveJapan.RuntimeId;
    private static string StellaId => LauncherProfiles.StellaSoraChina.RuntimeId;
}
