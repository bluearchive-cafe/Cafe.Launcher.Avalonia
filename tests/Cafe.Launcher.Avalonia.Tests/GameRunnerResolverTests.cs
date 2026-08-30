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

    [Fact]
    public async Task ResolveAsync_ForwardsSameOptionsToAvailabilityChecks()
    {
        var runner = new FakeGameRunner("umu", isSupportedPlatform: true, isAvailable: true);
        var resolver = new GameRunnerResolver([runner]);
        var options = new GameRuntimeOptions(RunnerPath: "/opt/umu/bin/umu-run");

        await resolver.ResolveAsync(options: options);
        Assert.Same(options, runner.LastAvailabilityOptions);

        await resolver.ResolveAsync(preferredRunnerId: "umu", options: options);
        Assert.Same(options, runner.LastAvailabilityOptions);
    }

    [Fact]
    public async Task ResolveWithDiagnosticsAsync_AutoMode_CarriesWinningRunnerAvailability()
    {
        var unavailable = new FakeGameRunner("umu", isSupportedPlatform: true, isAvailable: false);
        var available = new FakeGameRunner("wine", isSupportedPlatform: true, isAvailable: true);
        var resolver = new GameRunnerResolver([unavailable, available]);

        var resolution = await resolver.ResolveWithDiagnosticsAsync();

        Assert.Same(available, resolution.Runner);
        Assert.NotNull(resolution.Availability);
        Assert.True(resolution.Availability!.Available);
    }

    [Fact]
    public async Task ResolveWithDiagnosticsAsync_WithUnavailablePreferredId_CarriesFailureEvidence()
    {
        var runner = new FakeGameRunner("umu", isSupportedPlatform: true, isAvailable: false);
        var resolver = new GameRunnerResolver([runner]);

        var resolution = await resolver.ResolveWithDiagnosticsAsync(preferredRunnerId: "umu");

        Assert.Null(resolution.Runner);
        Assert.NotNull(resolution.Availability);
        Assert.False(resolution.Availability!.Available);
    }

    private sealed class FakeGameRunner(string id, bool isSupportedPlatform, bool isAvailable) : IGameRunner
    {
        public GameRuntimeOptions? LastAvailabilityOptions { get; private set; }

        public string Id => id;

        public bool IsSupportedPlatform => isSupportedPlatform;

        public Task<GameRunnerAvailability> CheckAvailabilityAsync(
            GameRuntimeOptions options,
            CancellationToken cancellationToken = default)
        {
            LastAvailabilityOptions = options;
            return Task.FromResult(new GameRunnerAvailability(
                isAvailable
                    ? GameRunnerAvailabilityStatus.Available
                    : GameRunnerAvailabilityStatus.NotFound));
        }

        public Task<GameProcess> StartAsync(
            GameLaunchRequest request,
            GameRuntimeOptions options,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("FakeGameRunner never starts processes.");

        public string? GetEffectivePrefixPath(GameLaunchRequest request, GameRuntimeOptions options) => null;

        public string? GetEffectiveProtonPath(GameRuntimeOptions options) => null;
    }
}
