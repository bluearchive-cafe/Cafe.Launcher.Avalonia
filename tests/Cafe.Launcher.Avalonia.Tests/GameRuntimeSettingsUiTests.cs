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
    public async Task RefreshGameRuntimeStatus_WithVisibleRunners_BuildsFilteredSummary()
    {
        var localizer = new LocalizationService();
        var options = new SettingsOptionsViewModel(localizer, new DiskSpaceService());
        var runtime = new StubGameRuntime(
        [
            new GameRuntimeStatusEntry(
                "native",
                new GameRunnerAvailability(GameRunnerAvailabilityStatus.Available)),
            new GameRuntimeStatusEntry(
                "umu",
                new GameRunnerAvailability(
                    GameRunnerAvailabilityStatus.Available,
                    Version: "1.4.4",
                    ExecutablePath: "/usr/bin/umu-run")),
            new GameRuntimeStatusEntry(
                "wine",
                new GameRunnerAvailability(GameRunnerAvailabilityStatus.NotFound, Message: "wine was not found on PATH."))
        ]);
        using var settings = CreateSettingsViewModel(localizer, options, runtime);

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
            new StubGameRuntime([]));
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
        var runtime = new StubGameRuntime(
        [
            new GameRuntimeStatusEntry(
                "umu",
                new GameRunnerAvailability(GameRunnerAvailabilityStatus.NotFound, Message: "umu-run was not found on PATH."))
        ]);
        using var settings = CreateSettingsViewModel(localizer, options, runtime);

        settings.LoadFromSnapshot(new LauncherSettings());
        await settings.PendingGameRuntimeStatusRefresh!;
        Assert.Contains(
            localizer.T("gameRuntimeStatusNotFound"),
            settings.GameRuntimeStatusSummary,
            StringComparison.Ordinal);

        runtime.Entries =
        [
            new GameRuntimeStatusEntry(
                "umu",
                new GameRunnerAvailability(
                    GameRunnerAvailabilityStatus.Available,
                    Version: "1.4.4",
                    ExecutablePath: "/usr/bin/umu-run"))
        ];
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
        IGameRuntime gameRuntime) =>
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
            gameRuntime,
            new StubFilePickerService());

    private sealed class StubGameRuntime(IReadOnlyList<GameRuntimeStatusEntry> entries) : IGameRuntime
    {
        public IReadOnlyList<GameRuntimeStatusEntry> Entries { get; set; } = entries;

        public Task<GameRuntimeLaunchResult> LaunchAsync(
            GameLaunchRequest request,
            GameRuntimeConfiguration configuration,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("StubGameRuntime never launches games.");

        public Task<IReadOnlyList<GameRuntimeStatusEntry>> GetStatusesAsync(
            GameRuntimeConfiguration configuration,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Entries);
    }
}
