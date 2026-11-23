using System.IO.Pipes;
using System.Text;
using Cafe.Launcher.Services;
using Cafe.Launcher.Testing;

namespace Cafe.Launcher.Tests;

public sealed class CrossProcessGameLaunchChannelTests
{
    [Fact]
    public void Send_TwoDifferentTargets_DeliversEachIdentityWithoutASelectionFallback()
    {
        using var directory = TestDirectory.Create();
        var name = "CafeTestLaunch-" + Guid.NewGuid().ToString("N");
        using var channel = new CrossProcessGameLaunchChannel(name, directory.DataRoot);
        channel.EnsureBound();

        foreach (var gameId in new[] { "stella-sora-cn", "blue-archive-jp" })
        {
            Assert.True(CrossProcessGameLaunchChannel.Send(name, directory.DataRoot, new CrossProcessGameLaunchRequest(gameId)));
            Assert.True(channel.WaitOne(TimeSpan.FromSeconds(5)));
            Assert.True(channel.TryTake(out var request));
            Assert.Equal(gameId, request?.GameId);
        }

        Assert.False(channel.TryTake(out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Send_InvalidPayload_DoesNotBecomeALaunchRequest(bool oversized)
    {
        using var directory = TestDirectory.Create();
        var name = "CafeTestLaunch-" + Guid.NewGuid().ToString("N");
        using var channel = new CrossProcessGameLaunchChannel(name, directory.DataRoot);
        channel.EnsureBound();
        var payload = Encoding.UTF8.GetBytes(oversized
            ? new string(' ', CrossProcessGameLaunchRequest.MaxPayloadBytes + 1)
            : "{\"gameId\":\"unknown-game\"}");
        using (var sender = new NamedPipeClientStream(".", CrossProcessGameLaunchChannel.GetPipeName(name, directory.DataRoot),
                   PipeDirection.Out, PipeOptions.CurrentUserOnly))
        {
            sender.Connect(1000);
            sender.Write(payload);
        }

        Assert.True(CrossProcessGameLaunchChannel.Send(name, directory.DataRoot,
            new CrossProcessGameLaunchRequest("stella-sora-cn")));
        Assert.True(channel.WaitOne(TimeSpan.FromSeconds(5)));
        Assert.True(channel.TryTake(out var request));
        Assert.Equal("stella-sora-cn", request?.GameId);
        Assert.False(channel.TryTake(out _));
    }

    [Fact]
    public void EndpointIdentity_DifferentDataRoots_DoNotShareATargetChannel()
    {
        using var first = TestDirectory.Create();
        using var second = TestDirectory.Create();

        Assert.NotEqual(CrossProcessGameLaunchChannel.GetPipeName("CafeLauncher", first.DataRoot),
            CrossProcessGameLaunchChannel.GetPipeName("CafeLauncher", second.DataRoot));
        Assert.NotEqual(CrossProcessGameLaunchChannel.GetPipeName("CafeLauncher", first.DataRoot),
            CrossProcessGameLaunchChannel.GetPipeName("OtherProduct", first.DataRoot));
    }
}
