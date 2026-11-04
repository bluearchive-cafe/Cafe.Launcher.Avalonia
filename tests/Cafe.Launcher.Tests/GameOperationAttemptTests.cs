using Cafe.Launcher.Core.Models;
using Cafe.Launcher.UI.Features.GameOperations;

namespace Cafe.Launcher.Tests;

public sealed class GameOperationAttemptTests
{
    [Fact]
    public void Evaluate_AllOperationsAndStates_OnlyAllowedRequestsCarryExecutableAttempts()
    {
        foreach (var operation in Enum.GetValues<GameOperationPolicy.Operation>())
        foreach (var state in Enum.GetValues<LauncherRuntimeState>())
        {
            var snapshot = new LauncherStatusSnapshot { RuntimeState = state };
            var admission = GameOperationAttempt.Evaluate(operation, snapshot);
            Assert.Equal(GameOperationPolicy.Decide(operation, state), admission.Decision);
            if (admission.Decision == GameOperationDecision.Allowed)
                Assert.Same(snapshot, Assert.IsType<GameOperationAttempt>(admission.Attempt).SnapshotFor(operation));
            else
                Assert.Null(admission.Attempt);
        }
    }

    [Fact]
    public void SnapshotFor_DifferentOperation_RejectsCapabilityReuse()
    {
        var admission = GameOperationAttempt.Evaluate(GameOperationPolicy.Operation.Repair,
            new LauncherStatusSnapshot { RuntimeState = LauncherRuntimeState.Ready });
        Assert.Throws<InvalidOperationException>(() => admission.Attempt!.SnapshotFor(GameOperationPolicy.Operation.Uninstall));
    }

    [Fact]
    public void Evaluate_AfterConfirmationStateChanges_DoesNotReuseAnEarlierAdmission()
    {
        var snapshot = new LauncherStatusSnapshot { RuntimeState = LauncherRuntimeState.Ready };
        Assert.NotNull(GameOperationAttempt.Evaluate(GameOperationPolicy.Operation.Uninstall, snapshot).Attempt);
        snapshot.RuntimeState = LauncherRuntimeState.NotInstalled;
        Assert.Null(GameOperationAttempt.Evaluate(GameOperationPolicy.Operation.Uninstall, snapshot).Attempt);
    }
}
