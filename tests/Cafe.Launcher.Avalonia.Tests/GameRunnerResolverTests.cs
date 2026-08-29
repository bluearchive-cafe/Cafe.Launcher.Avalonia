using System;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Services.GameRuntime;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class GameRunnerResolverTests
{
    [Fact]
    public async Task ResolveAsync_AutoMode_ReturnsFirstAvailableSupportedRunner()
    {
        var unavailable = new FakeGameRunner("umu", isSupportedPlatform: true, isAvailable: false);
        var available = new FakeGameRunner("wine", isSupportedPlatform: true, isAvailable: true);
        var resolver = new GameRunnerResolver([unavailable, available]);

        var resolved = await resolver.ResolveAsync();

        Assert.Same(available, resolved);
    }

    [Fact]
    public async Task ResolveAsync_AutoMode_SkipsRunnersForOtherPlatforms()
    {
        var unsupported = new FakeGameRunner("native", isSupportedPlatform: false, isAvailable: true);
        var supported = new FakeGameRunner("umu", isSupportedPlatform: true, isAvailable: true);
        var resolver = new GameRunnerResolver([unsupported, supported]);

        var resolved = await resolver.ResolveAsync();

        Assert.Same(supported, resolved);
    }

    [Fact]
    public async Task ResolveAsync_AutoMode_ReturnsNullWhenNoRunnerIsAvailable()
    {
        var unsupported = new FakeGameRunner("native", isSupportedPlatform: false, isAvailable: true);
        var unavailable = new FakeGameRunner("umu", isSupportedPlatform: true, isAvailable: false);
        var resolver = new GameRunnerResolver([unsupported, unavailable]);

        var resolved = await resolver.ResolveAsync();

        Assert.Null(resolved);
    }

    [Fact]
    public async Task ResolveAsync_WithPreferredId_ReturnsThatRunnerWhenAvailable()
    {
        var first = new FakeGameRunner("umu", isSupportedPlatform: true, isAvailable: true);
        var second = new FakeGameRunner("wine", isSupportedPlatform: true, isAvailable: true);
        var resolver = new GameRunnerResolver([first, second]);

        var resolved = await resolver.ResolveAsync(preferredRunnerId: "wine");

        Assert.Same(second, resolved);
    }

    [Fact]
    public async Task ResolveAsync_WithPreferredId_ReturnsNullWhenThatRunnerIsUnavailable()
    {
        var runner = new FakeGameRunner("umu", isSupportedPlatform: true, isAvailable: false);
        var resolver = new GameRunnerResolver([runner]);

        var resolved = await resolver.ResolveAsync(preferredRunnerId: "umu");

        Assert.Null(resolved);
    }

    [Fact]
    public async Task ResolveAsync_WithUnknownPreferredId_ReturnsNull()
    {
        var runner = new FakeGameRunner("umu", isSupportedPlatform: true, isAvailable: true);
        var resolver = new GameRunnerResolver([runner]);

        var resolved = await resolver.ResolveAsync(preferredRunnerId: "crossover");

        Assert.Null(resolved);
    }

    private sealed class FakeGameRunner(string id, bool isSupportedPlatform, bool isAvailable) : IGameRunner
    {
        public string Id => id;

        public bool IsSupportedPlatform => isSupportedPlatform;

        public Task<GameRunnerAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new GameRunnerAvailability(isAvailable));

        public Task<GameProcess> StartAsync(
            GameLaunchRequest request,
            GameRuntimeOptions options,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("FakeGameRunner never starts processes.");
    }
}
