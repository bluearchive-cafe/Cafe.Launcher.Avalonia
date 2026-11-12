using Cafe.Launcher.UI.Constants;
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
        var toggle = Assert.Single(row.Descendants(), element => element.Name.LocalName == "CheckBox");
        Assert.Equal(
            "{Binding Settings.Editor.Current.GameRuntime.EnableMangoHud, Mode=TwoWay}",
            toggle.Attribute("IsChecked")?.Value);
    }

    [Fact]
    public void RuntimePathChooseButtons_UseActionNameAndDiscoveryFailureIsVisible()
    {
        var document = XDocument.Load(TestRepository.InPresentation("Views", "SettingsGameSection.axaml"));
        var chooseButtons = document
            .Descendants()
            .Where(element => element.Name.LocalName == "Button")
            .Where(element =>
                element.Attribute("Command")?.Value is "{Binding Settings.GameRuntime.ChoosePrefixPathCommand}"
                    or "{Binding Settings.GameRuntime.ChooseProtonPathCommand}")
            .ToArray();

        Assert.Equal(2, chooseButtons.Length);
        Assert.All(chooseButtons, button => Assert.Equal(
            "{Binding Shell.I18n[choose]}",
            button.Attribute("AutomationProperties.Name")?.Value));
        Assert.Contains(
            document.Descendants(),
            element => element.Attribute("Text")?.Value
                == "{Binding Shell.I18n[gameRuntimeProtonDiscoveryFailed]}");
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
        await settings.GameRuntime.PendingStatusRefresh.WaitAsync(TimeSpan.FromSeconds(5));

        var summary = settings.GameRuntime.StatusSummary;
        var expectedDetail = localizer.F(LocalizationKeys.GameRuntimeStatusDetailFormat, "/usr/bin/umu-run", "1.4.4");
        Assert.Contains(localizer.T(LocalizationKeys.GameRuntimeRunnerUmu), summary, StringComparison.Ordinal);
        Assert.Contains(localizer.T(LocalizationKeys.GameRuntimeStatusAvailable), summary, StringComparison.Ordinal);
        Assert.Contains(expectedDetail, summary, StringComparison.Ordinal);
        Assert.Contains(localizer.T(LocalizationKeys.GameRuntimeRunnerWine), summary, StringComparison.Ordinal);
        Assert.Contains(localizer.T(LocalizationKeys.GameRuntimeStatusNotFound), summary, StringComparison.Ordinal);
        // Native is no longer offered in the runner list, so its status is not shown either.
        Assert.DoesNotContain(localizer.T(LocalizationKeys.GameRuntimeRunnerNative), summary, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildStatusSummary_UnsupportedRunnerWithoutPath_ShowsNameAndStatusOnly()
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

        var summary = settings.GameRuntime.BuildStatusSummary(entries);

        var expected = localizer.F(LocalizationKeys.GameRuntimeStatusEntryFormat, localizer.T(LocalizationKeys.GameRuntimeRunnerWine), localizer.T(LocalizationKeys.GameRuntimeStatusUnsupported));
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
        await settings.GameRuntime.PendingStatusRefresh.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains(
            localizer.T(LocalizationKeys.GameRuntimeStatusNotFound),
            settings.GameRuntime.StatusSummary,
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
        settings.GameRuntime.RefreshStatus();
        await settings.GameRuntime.PendingStatusRefresh.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("1.4.4", settings.GameRuntime.StatusSummary, StringComparison.Ordinal);

        // The display-name rebuild (language switch path) replays the cached
        // entries instead of clearing the summary.
        settings.RefreshOptionDisplayNames();
        Assert.Contains("1.4.4", settings.GameRuntime.StatusSummary, StringComparison.Ordinal);
        Assert.Contains(
            localizer.T(LocalizationKeys.GameRuntimeStatusAvailable),
            settings.GameRuntime.StatusSummary,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, "/home/player/prefix")]
    [InlineData(false, "/home/player/GE-Proton")]
    [InlineData(true, null)]
    [InlineData(false, " ")]
    public async Task ChooseRuntimeFolder_OnlyEditsDraftAndCancellationPreservesPath(bool prefix, string? picked)
    {
        var picker = new StubFilePickerService
        {
            FolderPicker = (_, start) =>
            {
                Assert.Equal("/original", start);
                return Task.FromResult(picked);
            }
        };
        using var settings = CreateSettingsViewModel(
            new LocalizationService(),
            new SettingsOptionsViewModel(new LocalizationService(), new DiskSpaceService()),
            new StubGameRuntime([]), picker);
        var snapshot = new LauncherSettings();
        snapshot.GameRuntime.PrefixPath = "/original";
        snapshot.GameRuntime.ProtonPath = "/original";
        settings.LoadFromSnapshot(snapshot);

        await (prefix ? settings.GameRuntime.ChoosePrefixPathCommand : settings.GameRuntime.ChooseProtonPathCommand).ExecuteAsync(null);

        var expected = string.IsNullOrWhiteSpace(picked) ? "/original" : picked;
        Assert.Equal(expected, prefix ? settings.Editor.Current.GameRuntime.PrefixPath : settings.Editor.Current.GameRuntime.ProtonPath);
        Assert.Equal("/original", settings.Editor.GetSavedSnapshot().GameRuntime.PrefixPath);
        Assert.Equal("/original", settings.Editor.GetSavedSnapshot().GameRuntime.ProtonPath);
        await settings.DiscardChangesAsync();
        await settings.GameRuntime.PendingStatusRefresh.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("/original", settings.Editor.Current.GameRuntime.PrefixPath);
        Assert.Equal("/original", settings.Editor.Current.GameRuntime.ProtonPath);
        Assert.False(settings.IsSettingsDirty);
    }

    [Fact]
    public async Task ProtonBuilds_DiscoveryDoesNotSelectAndSelectionUsesDraftPath()
    {
        using var directory = TestDirectory.Create();
        var buildPath = directory.Sub("GE-Proton");
        Directory.CreateDirectory(buildPath);
        File.WriteAllText(Path.Combine(buildPath, "proton"), "#!/bin/sh");
        using var settings = CreateSettingsViewModel(
            new LocalizationService(),
            new SettingsOptionsViewModel(new LocalizationService(), new DiskSpaceService()),
            new StubGameRuntime([]), discovery: new ProtonBuildDiscovery([directory.Path]));
        settings.LoadFromSnapshot(new LauncherSettings());
        await settings.GameRuntime.PendingStatusRefresh.WaitAsync(TimeSpan.FromSeconds(5));

        var build = Assert.Single(settings.GameRuntime.Builds);
        Assert.True(settings.GameRuntime.HasBuilds);
        Assert.Null(settings.GameRuntime.SelectedBuild);
        Assert.False(settings.IsSettingsDirty);
        settings.GameRuntime.SelectedBuild = build.Code;
        Assert.Equal(buildPath, settings.Editor.Current.GameRuntime.ProtonPath);
        Assert.True(settings.IsSettingsDirty);
        Assert.Equal(build.Code, settings.GameRuntime.SelectedBuild);
        Assert.True(string.IsNullOrEmpty(settings.Editor.GetSavedSnapshot().GameRuntime.ProtonPath));

        settings.Editor.Current.GameRuntime.ProtonPath = "/custom/build";
        settings.GameRuntime.SelectedBuild = null;
        Assert.Equal("/custom/build", settings.Editor.Current.GameRuntime.ProtonPath);
        Assert.Null(settings.GameRuntime.SelectedBuild);
        await settings.DiscardChangesAsync();
        await settings.GameRuntime.PendingStatusRefresh.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(settings.GameRuntime.SelectedBuild);
        Assert.False(settings.IsSettingsDirty);
    }

    [Fact]
    public void SelectedProtonBuild_MatchesPathIgnoringTrailingSeparator()
    {
        using var settings = CreateSettingsViewModel(
            new LocalizationService(),
            new SettingsOptionsViewModel(new LocalizationService(), new DiskSpaceService()),
            new StubGameRuntime([]));
        var build = new SettingOption { DisplayName = "GE-Proton", Code = "/tools/GE-Proton" };
        settings.GameRuntime.Builds = [build];
        settings.Editor.Current.GameRuntime.ProtonPath = build.Code + Path.DirectorySeparatorChar;

        Assert.Equal(build.Code, settings.GameRuntime.SelectedBuild);
    }

    [Fact]
    public async Task ProtonBuildDiscovery_FailureShowsRetryableMessageAndRefreshRecovers()
    {
        var localizer = new LocalizationService();
        var discovery = new StubProtonBuildDiscovery();
        discovery.Responses.Enqueue(() => throw new IOException("scan failed"));
        using var settings = CreateSettingsViewModel(
            localizer,
            new SettingsOptionsViewModel(localizer, new DiskSpaceService()),
            new StubGameRuntime([]), discovery: discovery);

        settings.GameRuntime.RefreshStatusCommand.Execute(null);
        await settings.GameRuntime.PendingStatusRefresh.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(settings.GameRuntime.BuildDiscoveryFailed);
        Assert.False(settings.GameRuntime.HasBuilds);
        Assert.False(settings.GameRuntime.IsStatusRefreshing);

        discovery.Responses.Enqueue(() =>
            [new ProtonBuild("GE-Proton", "/tools/GE-Proton")]);
        settings.GameRuntime.RefreshStatusCommand.Execute(null);
        await settings.GameRuntime.PendingStatusRefresh.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(settings.GameRuntime.BuildDiscoveryFailed);
        Assert.Equal("/tools/GE-Proton", Assert.Single(settings.GameRuntime.Builds).Code);
    }

    [Fact]
    public async Task ProtonBuildDiscovery_LateCancelledResultCannotOverwriteLatest()
    {
        using var gate = new ManualResetEventSlim();
        using var firstStarted = new ManualResetEventSlim();
        var discovery = new StubProtonBuildDiscovery();
        discovery.Responses.Enqueue(() =>
        {
            firstStarted.Set();
            gate.Wait(TimeSpan.FromSeconds(5));
            return [new ProtonBuild("stale", "/stale/GE-Proton")];
        });
        discovery.Responses.Enqueue(() => [new ProtonBuild("latest", "/latest/GE-Proton")]);
        using var settings = CreateSettingsViewModel(
            new LocalizationService(),
            new SettingsOptionsViewModel(new LocalizationService(), new DiskSpaceService()),
            new StubGameRuntime([]), discovery: discovery);

        settings.GameRuntime.RefreshStatus();
        var staleRefresh = settings.GameRuntime.PendingStatusRefresh;
        Assert.True(firstStarted.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(settings.GameRuntime.IsStatusRefreshing);

        settings.GameRuntime.RefreshStatus();
        await settings.GameRuntime.PendingStatusRefresh.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(settings.GameRuntime.IsStatusRefreshing);
        Assert.Equal("/latest/GE-Proton", Assert.Single(settings.GameRuntime.Builds).Code);

        gate.Set();
        await staleRefresh.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("/latest/GE-Proton", Assert.Single(settings.GameRuntime.Builds).Code);
    }

    [Fact]
    public async Task RuntimeDraftChanges_RefreshesUsingLatestConfiguration()
    {
        var runtime = new StubGameRuntime([]);
        using var settings = CreateSettingsViewModel(
            new LocalizationService(),
            new SettingsOptionsViewModel(new LocalizationService(), new DiskSpaceService()), runtime);

        settings.Editor.Current.GameRuntime.Runner = GameRuntimeRunners.Wine;
        settings.Editor.Current.GameRuntime.RunnerPath = "/custom/wine";
        await settings.GameRuntime.PendingStatusRefresh.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(GameRuntimeRunners.Wine, runtime.LastConfiguration?.PreferredRunnerId);
        Assert.Equal("/custom/wine", runtime.LastConfiguration?.RunnerPath);
        Assert.Empty(settings.GameRuntime.Builds);
        Assert.False(settings.GameRuntime.HasBuilds);
    }

    [Fact]
    public async Task RefreshGameRuntimeStatus_LateCancelledResultCannotOverwriteLatest()
    {
        var first = new TaskCompletionSource<IReadOnlyList<GameRuntimeStatusEntry>>();
        var runtime = new StubGameRuntime([]) { Respond = (_, _) => first.Task };
        using var settings = CreateSettingsViewModel(
            new LocalizationService(),
            new SettingsOptionsViewModel(new LocalizationService(), new DiskSpaceService()), runtime);
        settings.GameRuntime.RefreshStatus();
        var staleRefresh = settings.GameRuntime.PendingStatusRefresh;
        runtime.Respond = null;
        settings.GameRuntime.RefreshStatus();
        await settings.GameRuntime.PendingStatusRefresh.WaitAsync(TimeSpan.FromSeconds(5));
        var latestSummary = settings.GameRuntime.StatusSummary;

        first.SetResult([new GameRuntimeStatusEntry("umu",
            new GameRunnerAvailability(GameRunnerAvailabilityStatus.Available, ExecutablePath: "/stale/umu"))]);
        await staleRefresh.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(latestSummary, settings.GameRuntime.StatusSummary);
        Assert.DoesNotContain("/stale/umu", settings.GameRuntime.StatusSummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefreshGameRuntimeStatus_FailureShowsRetryableMessageAndRefreshRecovers()
    {
        var localizer = new LocalizationService();
        var runtime = new StubGameRuntime([])
        {
            Respond = (_, _) => throw new IOException("probe failed")
        };
        using var settings = CreateSettingsViewModel(localizer,
            new SettingsOptionsViewModel(localizer, new DiskSpaceService()), runtime);
        settings.GameRuntime.RefreshStatusCommand.Execute(null);
        await settings.GameRuntime.PendingStatusRefresh.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(localizer.T(LocalizationKeys.GameRuntimeStatusFailed), settings.GameRuntime.StatusSummary);
        settings.RefreshOptionDisplayNames();
        Assert.Equal(localizer.T(LocalizationKeys.GameRuntimeStatusFailed), settings.GameRuntime.StatusSummary);

        runtime.Respond = null;
        settings.GameRuntime.RefreshStatusCommand.Execute(null);
        await settings.GameRuntime.PendingStatusRefresh.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(string.Empty, settings.GameRuntime.StatusSummary);
    }

    private static SettingsViewModel CreateSettingsViewModel(
        LocalizationService localizer,
        SettingsOptionsViewModel options,
        IGameRuntime gameRuntime,
        IFilePickerService? picker = null,
        IProtonBuildDiscovery? discovery = null) =>
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
            picker ?? new StubFilePickerService(), discovery ?? new ProtonBuildDiscovery([]));

    private sealed class StubProtonBuildDiscovery : IProtonBuildDiscovery
    {
        public Queue<Func<IReadOnlyList<ProtonBuild>>> Responses { get; } = new();

        public IReadOnlyList<ProtonBuild> Discover() =>
            Responses.Count > 0 ? Responses.Dequeue()() : [];
    }

    private sealed class StubGameRuntime(IReadOnlyList<GameRuntimeStatusEntry> entries) : IGameRuntime
    {
        public IReadOnlyList<GameRuntimeStatusEntry> Entries { get; set; } = entries;
        public GameRuntimeConfiguration? LastConfiguration { get; private set; }
        public Func<GameRuntimeConfiguration, CancellationToken, Task<IReadOnlyList<GameRuntimeStatusEntry>>>? Respond { get; set; }

        public Task<GameRuntimeLaunchResult> LaunchAsync(
            GameLaunchRequest request,
            GameRuntimeConfiguration configuration,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("StubGameRuntime never launches games.");

        public Task<IReadOnlyList<GameRuntimeStatusEntry>> GetStatusesAsync(
            GameRuntimeConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            LastConfiguration = configuration;
            return Respond?.Invoke(configuration, cancellationToken) ?? Task.FromResult(Entries);
        }
    }
}
