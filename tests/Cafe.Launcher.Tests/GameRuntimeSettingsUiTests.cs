using System.Threading;
using System.Xml.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Cafe.Launcher.UI.Features.Settings;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.UI.Services.GameRuntime;
using Cafe.Launcher.UI.ViewModels;
using Cafe.Launcher.Testing;
using Cafe.Launcher.Core.Services.GameRuntime;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Services;

namespace Cafe.Launcher.Tests;

[Collection(nameof(LocalizationServiceTestIsolation))]
public sealed class GameRuntimeSettingsUiTests
{
    [Fact]
    public void MangoHudSetting_VisibilityUsesLinuxPlatformRatherThanHiddenSettingsOverride()
    {
        var document = XDocument.Load(TestRepository.InPresentation("Views", "SettingsGameSection.axaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var row = Assert.Single(document.Descendants(), element =>
            element.Attribute(xaml + "Name")?.Value == "MangoHudSetting");

        Assert.Equal("{Binding Shell.IsLinuxPlatform}", row.Attribute("IsVisible")?.Value);
        var toggle = Assert.Single(row.Descendants(), element => element.Name.LocalName == "ToggleSwitch");
        Assert.Equal(
            "{Binding Settings.Editor.Current.GameRuntime.EnableMangoHud, Mode=TwoWay}",
            toggle.Attribute("IsChecked")?.Value);
    }

    [Fact]
    public void MangoHudSetting_EditSaveAndDiscard_PreservesStateAndDefaultsOffForOldSettings()
    {
        var oldSettings = JsonSerializer.Deserialize<LauncherSettings>("{}")!;
        Assert.False(oldSettings.GameRuntime.EnableMangoHud);
        var editor = new SettingsEditor();
        editor.ApplySnapshot(oldSettings);

        editor.Current.GameRuntime.EnableMangoHud = true;
        Assert.True(editor.IsDirty);
        Assert.False(editor.GetSavedSnapshot().GameRuntime.EnableMangoHud);
        editor.Discard();
        Assert.False(editor.Current.GameRuntime.EnableMangoHud);
        Assert.False(editor.IsDirty);

        editor.Current.GameRuntime.EnableMangoHud = true;
        var json = JsonSerializer.Serialize(editor.GetSnapshot());
        Assert.Contains("\"enableMangoHud\":true", json, StringComparison.Ordinal);
        var saved = JsonSerializer.Deserialize<LauncherSettings>(json)!;
        editor.ApplySnapshot(saved);
        Assert.True(editor.Current.GameRuntime.EnableMangoHud);
        Assert.True(editor.GetSavedSnapshot().GameRuntime.EnableMangoHud);
        Assert.False(editor.IsDirty);
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
        await settings.PendingGameRuntimeStatusRefresh.WaitAsync(TimeSpan.FromSeconds(5));

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
        await settings.PendingGameRuntimeStatusRefresh.WaitAsync(TimeSpan.FromSeconds(5));
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
        await settings.PendingGameRuntimeStatusRefresh.WaitAsync(TimeSpan.FromSeconds(5));
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
            null!,
            localizer,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            options,
            new SettingsAppearanceViewModel(new SettingsEditor(), new ThemeApplier()),
            new RecordingErrorHandlingService(),
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
