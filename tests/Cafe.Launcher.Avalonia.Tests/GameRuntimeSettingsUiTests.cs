using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Features.Settings;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.GameRuntime;
using Cafe.Launcher.Avalonia.ViewModels;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class GameRuntimeSettingsUiTests
{
    static GameRuntimeSettingsUiTests()
    {
        TestLocalizationHelper.Initialize();
    }

    [Fact]
    public void GameRuntimeRunnerOptions_OnLinux_ExcludeNative()
    {
        var localizer = new LocalizationService();
        var options = new SettingsOptionsViewModel(localizer, new DiskSpaceService());

        var codes = options.GameRuntimeRunner.Select(option => option.Code).ToList();

        Assert.Equal(
            [GameRuntimeRunners.Auto, GameRuntimeRunners.Umu, GameRuntimeRunners.Wine],
            codes);
        Assert.DoesNotContain(GameRuntimeRunners.Native, codes);
    }

    [Fact]
    public async Task GetStatusesAsync_WithPreferredUmu_AppliesCustomPathOnlyToUmu()
    {
        var options = new GameRuntimeOptions(RunnerPath: "/opt/umu/bin/umu-run");
        var umu = new StubRunner("umu", GameRunnerAvailabilityStatus.Available, "1.4.4", "/usr/bin/umu-run");
        var wine = new StubRunner("wine", GameRunnerAvailabilityStatus.NotFound, null, null);
        var service = new GameRuntimeStatusService([umu, wine]);

        var entries = await service.GetStatusesAsync("umu", options);

        Assert.Equal(2, entries.Count);
        Assert.Equal("umu", entries[0].RunnerId);
        Assert.Equal("wine", entries[1].RunnerId);
        Assert.Same(options, umu.LastOptions);
        Assert.NotNull(wine.LastOptions);
        Assert.Null(wine.LastOptions!.RunnerPath);
    }

    [Fact]
    public async Task GetStatusesAsync_InAutoMode_IgnoresCustomPathForEveryRunner()
    {
        var options = new GameRuntimeOptions(RunnerPath: "/usr/bin/wine");
        var umu = new StubRunner("umu", GameRunnerAvailabilityStatus.Available, "1.4.4", "/usr/bin/umu-run");
        var wine = new StubRunner("wine", GameRunnerAvailabilityStatus.Available, "9.0", "/usr/bin/wine");
        var service = new GameRuntimeStatusService([umu, wine]);

        await service.GetStatusesAsync(preferredRunnerId: null, options: options);

        Assert.NotNull(umu.LastOptions);
        Assert.NotNull(wine.LastOptions);
        Assert.Null(umu.LastOptions!.RunnerPath);
        Assert.Null(wine.LastOptions!.RunnerPath);
    }

    [Fact]
    public async Task RefreshGameRuntimeStatus_WithVisibleRunners_BuildsFilteredSummary()
    {
        var localizer = new LocalizationService();
        var options = new SettingsOptionsViewModel(localizer, new DiskSpaceService());
        var statusService = new GameRuntimeStatusService(new IGameRunner[]
        {
            new StubRunner("native", GameRunnerAvailabilityStatus.Available, null, null),
            new StubRunner("umu", GameRunnerAvailabilityStatus.Available, "1.4.4", "/usr/bin/umu-run"),
            new StubRunner("wine", GameRunnerAvailabilityStatus.NotFound, null, null),
        });
        using var settings = CreateSettingsViewModel(localizer, options, statusService);

        settings.LoadFromSnapshot(new LauncherSettings());
        await settings.PendingGameRuntimeStatusRefresh!;

        var summary = settings.GameRuntimeStatusSummary;
        var expectedDetail = localizer.F("gameRuntimeStatusDetailFormat", "/usr/bin/umu-run", "1.4.4");
        Assert.Contains(localizer.T("gameRuntimeRunnerUmu"), summary, StringComparison.Ordinal);
        Assert.Contains(localizer.T("gameRuntimeStatusAvailable"), summary, StringComparison.Ordinal);
        Assert.Contains(expectedDetail, summary, StringComparison.Ordinal);
        Assert.Contains(localizer.T("gameRuntimeRunnerWine"), summary, StringComparison.Ordinal);
        Assert.Contains(localizer.T("gameRuntimeStatusNotFound"), summary, StringComparison.Ordinal);
        // Native is no longer offered in the runner list, so its status is not shown either.
        Assert.DoesNotContain(localizer.T("gameRuntimeRunnerNative"), summary, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildGameRuntimeStatusSummary_UnsupportedRunnerWithoutPath_ShowsNameAndStatusOnly()
    {
        var localizer = new LocalizationService();
        var options = new SettingsOptionsViewModel(localizer, new DiskSpaceService());
        using var settings = CreateSettingsViewModel(
            localizer,
            options,
            new GameRuntimeStatusService(Array.Empty<IGameRunner>()));
        var entries = new[]
        {
            new GameRuntimeStatusEntry(
                "wine",
                new GameRunnerAvailability(GameRunnerAvailabilityStatus.Unsupported, Message: "Wine requires Linux."))
        };

        var summary = settings.BuildGameRuntimeStatusSummary(entries);

        var expected = localizer.F("gameRuntimeStatusEntryFormat", localizer.T("gameRuntimeRunnerWine"), localizer.T("gameRuntimeStatusUnsupported"));
        Assert.Equal(expected, summary);
    }

    [Fact]
    public async Task RefreshOptionDisplayNames_WithCachedEntries_RebuildsSummary()
    {
        var localizer = new LocalizationService();
        var options = new SettingsOptionsViewModel(localizer, new DiskSpaceService());
        var stub = new StubRunner("umu", GameRunnerAvailabilityStatus.NotFound, null, null);
        using var settings = CreateSettingsViewModel(
            localizer,
            options,
            new GameRuntimeStatusService(new IGameRunner[] { stub }));

        settings.LoadFromSnapshot(new LauncherSettings());
        await settings.PendingGameRuntimeStatusRefresh!;
        Assert.Contains(
            localizer.T("gameRuntimeStatusNotFound"),
            settings.GameRuntimeStatusSummary,
            StringComparison.Ordinal);

        stub.NextAvailability = new GameRunnerAvailability(
            GameRunnerAvailabilityStatus.Available,
            Version: "1.4.4",
            ExecutablePath: "/usr/bin/umu-run");
        settings.RefreshGameRuntimeStatus();
        await settings.PendingGameRuntimeStatusRefresh!;
        Assert.Contains("1.4.4", settings.GameRuntimeStatusSummary, StringComparison.Ordinal);

        // The display-name rebuild (language switch path) replays the cached
        // entries instead of clearing the summary.
        settings.RefreshOptionDisplayNames();
        Assert.Contains("1.4.4", settings.GameRuntimeStatusSummary, StringComparison.Ordinal);
        Assert.Contains(
            localizer.T("gameRuntimeStatusAvailable"),
            settings.GameRuntimeStatusSummary,
            StringComparison.Ordinal);
    }

    private static SettingsViewModel CreateSettingsViewModel(
        LocalizationService localizer,
        SettingsOptionsViewModel options,
        GameRuntimeStatusService statusService) =>
        new(
            null!,
            localizer,
            null!,
            null!,
            null!,
            null!,
            null!,
            options,
            new SettingsAppearanceViewModel(new SettingsEditor()),
            new FakeErrorHandlingService(),
            statusService);

    private sealed class StubRunner(
        string id,
        GameRunnerAvailabilityStatus status,
        string? version,
        string? executablePath) : IGameRunner
    {
        public GameRuntimeOptions? LastOptions { get; private set; }

        public GameRunnerAvailability? NextAvailability { get; set; }

        public string Id => id;

        public bool IsSupportedPlatform => true;

        public Task<GameRunnerAvailability> CheckAvailabilityAsync(
            GameRuntimeOptions options,
            CancellationToken cancellationToken = default)
        {
            LastOptions = options;
            return Task.FromResult(NextAvailability ?? new GameRunnerAvailability(
                status,
                Version: version,
                ExecutablePath: executablePath));
        }

        public Task<GameProcess> StartAsync(
            GameLaunchRequest request,
            GameRuntimeOptions options,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("StubRunner never starts processes.");

        public string? GetEffectivePrefixPath(GameLaunchRequest request, GameRuntimeOptions options) => null;

        public string? GetEffectiveProtonPath(GameRuntimeOptions options) => null;
    }
}
