using System.ComponentModel;
using System.Diagnostics;
using Cafe.Launcher.Core.Services.GameRuntime;

namespace Cafe.Launcher.Tests;

public sealed class ProcessServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FindRunningExeNamesBySnapshot_WhenEnumerationFails_PropagatesTheFailure(bool accessDenied)
    {
        Exception failure = accessDenied ? new Win32Exception(5) : new InvalidOperationException("snapshot failed");

        var actual = Assert.ThrowsAny<Exception>(() => ProcessService.FindRunningExeNamesBySnapshot(
            ["BlueArchive"], () => throw failure, CancellationToken.None));

        Assert.Same(failure, actual);
    }

    [Fact]
    public void FindRunningExeNamesBySnapshot_WhenOneProcessIsUnavailable_StillFindsOtherProcesses()
    {
        using var current = Process.GetCurrentProcess();
        var names = ProcessService.FindRunningExeNamesBySnapshot(
            [current.ProcessName], () => [new Process(), Process.GetCurrentProcess()], CancellationToken.None);

        Assert.Equal([current.ProcessName], names);
    }
}
