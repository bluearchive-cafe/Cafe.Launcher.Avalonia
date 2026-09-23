using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class ShutdownDeferralTests
{
    [Fact]
    public void ShouldCancelRequest_AfterDeferralBeforeCommit_ReturnsTrue()
    {
        var deferral = new ShutdownDeferral();

        deferral.Defer();

        Assert.True(deferral.ShouldCancelRequest);
    }

    [Fact]
    public void ShouldCancelRequest_AfterCommit_ReturnsFalse()
    {
        var deferral = new ShutdownDeferral();
        deferral.Defer();

        deferral.Commit();

        Assert.False(deferral.ShouldCancelRequest);
    }
}
