using Cafe.Launcher.Core.Constants;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Services;
using Cafe.Launcher.Core.Services.Diagnostics;
using Cafe.Launcher.Core.Services.GameRuntime;
using Cafe.Launcher.Testing;

namespace Cafe.Launcher.Tests;

public sealed class GameContextRegistryTests
{
    [Fact]
    public void GetOrCreate_WhenViewingGamesRepeatedly_ReusesContextsWithIndependentTrackers()
    {
        using var directory = TestDirectory.Create();
        var releases = new List<string>();
        using var registry = new GameContextRegistry(directory.DataRoot, (profile, paths) =>
            CreateContext(profile, paths, [new RecordingDisposable(profile.RuntimeId, releases)]));

        var ba = registry.GetOrCreate("blue-archive-jp");
        var stella = registry.GetOrCreate("stella-sora-cn");

        Assert.Same(ba, registry.GetOrCreate("blue-archive-jp"));
        Assert.Same(stella, registry.GetOrCreate("stella-sora-cn"));
        Assert.NotSame(ba.ProcessTracker, stella.ProcessTracker);
        Assert.NotSame(ba.ApiClient, stella.ApiClient);
        Assert.Same(directory.DataRoot, ba.Paths.DataRoot);
        Assert.Same(directory.DataRoot, stella.Paths.DataRoot);
        Assert.NotSame(ba.RunnerOutputCapture, stella.RunnerOutputCapture);
        Assert.NotSame(ba.CompatibilityEnvironmentPrecheck, stella.CompatibilityEnvironmentPrecheck);
        Assert.NotSame(ba.PrefixMetadataStore, stella.PrefixMetadataStore);
        Assert.Empty(releases);
    }

    [Fact]
    public async Task GetOrCreate_ConcurrentRequestsForOneGame_ConstructOnlyOnce()
    {
        using var directory = TestDirectory.Create();
        var constructions = 0;
        using var registry = new GameContextRegistry(directory.DataRoot, (profile, paths) =>
        {
            Interlocked.Increment(ref constructions);
            return CreateContext(profile, paths, []);
        });
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = Enumerable.Range(0, 24).Select(async _ =>
        {
            await start.Task;
            return registry.GetOrCreate("stella-sora-cn");
        }).ToArray();

        start.SetResult();
        var contexts = await Task.WhenAll(requests);

        Assert.Equal(1, constructions);
        Assert.All(contexts, context => Assert.Same(contexts[0], context));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("unknown-game")]
    [InlineData("../blue-archive-jp")]
    [InlineData("BLUE-ARCHIVE-JP")]
    public void GetOrCreate_UnknownGame_RejectsBeforeCallingFactory(string? gameId)
    {
        using var directory = TestDirectory.Create();
        var constructions = 0;
        using var registry = new GameContextRegistry(directory.DataRoot, (profile, paths) =>
        {
            constructions++;
            return CreateContext(profile, paths, []);
        });

        Assert.ThrowsAny<ArgumentException>(() => registry.GetOrCreate(gameId!));

        Assert.Equal(0, constructions);
        Assert.Empty(Directory.EnumerateFileSystemEntries(directory));
    }

    [Fact]
    public void GetOrCreate_WhenFactoryFails_RetriesWithoutCachingTheFailure()
    {
        using var directory = TestDirectory.Create();
        var constructions = 0;
        using var registry = new GameContextRegistry(directory.DataRoot, (profile, paths) =>
        {
            constructions++;
            return constructions == 1
                ? throw new InvalidOperationException("simulated factory failure")
                : CreateContext(profile, paths, []);
        });

        Assert.Throws<InvalidOperationException>(() => registry.GetOrCreate("blue-archive-jp"));
        var context = registry.GetOrCreate("blue-archive-jp");

        Assert.Same(context, registry.GetOrCreate("blue-archive-jp"));
        Assert.Equal(2, constructions);
    }

    [Fact]
    public void GetOrCreate_WhenFactoryReusesAnotherGamesTracker_RejectsTheNewContext()
    {
        using var directory = TestDirectory.Create();
        var tracker = TestGameProcessTracker.None();
        var releases = new List<string>();
        using var registry = new GameContextRegistry(directory.DataRoot, (profile, paths) =>
            CreateContext(profile, paths, [new RecordingDisposable(profile.RuntimeId, releases)], tracker: tracker));
        var ba = registry.GetOrCreate("blue-archive-jp");

        Assert.Throws<InvalidOperationException>(() => registry.GetOrCreate("stella-sora-cn"));

        Assert.Same(ba, registry.GetOrCreate("blue-archive-jp"));
        Assert.Equal(["stella-sora-cn"], releases);
    }

    [Fact]
    public void GetOrCreate_WhenRejectedContextOwnsASharedTracker_PreservesTheCachedContextsTracker()
    {
        using var directory = TestDirectory.Create();
        var tracker = new DisposableProcessTracker();
        var releases = new List<string>();
        var registry = new GameContextRegistry(directory.DataRoot, (profile, paths) =>
            CreateContext(profile, paths, [tracker, new RecordingDisposable(profile.RuntimeId, releases)], tracker: tracker));
        var ba = registry.GetOrCreate("blue-archive-jp");

        Assert.Throws<InvalidOperationException>(() => registry.GetOrCreate("stella-sora-cn"));

        Assert.Equal(["stella-sora-cn"], releases);
        Assert.False(tracker.IsDisposed);
        Assert.False(ba.ProcessTracker.HasLiveTrackedProcess);
        Assert.Same(ba, registry.GetOrCreate("blue-archive-jp"));

        registry.Dispose();

        Assert.True(tracker.IsDisposed);
        Assert.Equal(1, tracker.DisposeCount);
        Assert.Equal(["stella-sora-cn", "blue-archive-jp"], releases);
    }

    [Fact]
    public void Dispose_AfterBothGamesWereCreated_ReleasesContextsAndResourcesInReverseOrder()
    {
        using var directory = TestDirectory.Create();
        var releases = new List<string>();
        var registry = new GameContextRegistry(directory.DataRoot, (profile, paths) => CreateContext(profile, paths,
        [
            new RecordingDisposable(profile.RuntimeId + ":first", releases),
            new RecordingDisposable(profile.RuntimeId + ":second", releases)
        ]));
        registry.GetOrCreate("blue-archive-jp");
        registry.GetOrCreate("stella-sora-cn");

        registry.Dispose();
        registry.Dispose();

        Assert.Equal(
            ["stella-sora-cn:second", "stella-sora-cn:first", "blue-archive-jp:second", "blue-archive-jp:first"],
            releases);
        Assert.Throws<ObjectDisposedException>(() => registry.GetOrCreate("blue-archive-jp"));
        Assert.Throws<ObjectDisposedException>(() => registry.GetOrCreate("stella-sora-cn"));
    }

    [Fact]
    public void Dispose_WhenAnOwnedResourceFails_StillReleasesOtherResourcesAndContexts()
    {
        using var directory = TestDirectory.Create();
        var releases = new List<string>();
        var registry = new GameContextRegistry(directory.DataRoot, (profile, paths) => CreateContext(profile, paths,
        [
            new RecordingDisposable(profile.RuntimeId + ":first", releases),
            new RecordingDisposable(profile.RuntimeId + ":second", releases, fail: true)
        ]));
        registry.GetOrCreate("blue-archive-jp");
        registry.GetOrCreate("stella-sora-cn");

        Assert.Throws<AggregateException>(registry.Dispose);

        Assert.Equal(
            ["stella-sora-cn:second", "stella-sora-cn:first", "blue-archive-jp:second", "blue-archive-jp:first"],
            releases);
        registry.Dispose();
        Assert.Throws<ObjectDisposedException>(() => registry.GetOrCreate("blue-archive-jp"));
    }

    [Fact]
    public async Task LoadAsync_StellaContext_UsesItsOwnSettingsAndKeepsTheRootSettingsAvailable()
    {
        using var directory = TestDirectory.Create();
        var settings = LauncherSettings.CreateDefaults();
        settings.ThemeMode = ThemeModes.Dark;
        settings.GetGameSettings("blue-archive-jp").GamePath = Path.Combine(directory, "ba", "YostarGames", "BlueArchive_JP");
        settings.GetGameSettings("blue-archive-jp").PatchUrlGroup = PatchUrlGroups.Cafe;
        settings.GetGameSettings("stella-sora-cn").GamePath = Path.Combine(directory, "stella", "YostarGames", "StellaSora_CN");
        settings.GetGameSettings("stella-sora-cn").PatchUrlGroup = PatchUrlGroups.Official;
        settings.GetGameSettings("stella-sora-cn").GameRuntime.EnableMangoHud = true;
        var api = new RecordingApiClient();
        using var registry = new GameContextRegistry(directory.DataRoot, (profile, paths) =>
            CreateContext(profile, paths, [], api: api, settings: settings));

        var snapshot = await registry.GetOrCreate("stella-sora-cn").CoreService.LoadAsync();

        Assert.Equal("stella-sora-cn", snapshot.GameId);
        Assert.Equal(settings.GetGameSettings("stella-sora-cn").GamePath, snapshot.LocalGame.GamePath);
        Assert.Equal(settings.GetGameSettings("stella-sora-cn").GamePath, snapshot.GameSettings.GamePath);
        Assert.Equal(settings.GamePath, snapshot.Settings.GamePath);
        Assert.Equal(ThemeModes.Dark, snapshot.Settings.ThemeMode);
        Assert.Equal([PatchUrlGroups.Official], api.PatchGroups);
        Assert.NotSame(snapshot.Settings.GetGameSettings("stella-sora-cn"), snapshot.GameSettings);
        snapshot.GameSettings.GameRuntime.EnableMangoHud = false;
        Assert.True(snapshot.Settings.GetGameSettings("stella-sora-cn").GameRuntime.EnableMangoHud);
    }

    [Fact]
    public async Task LoadAsync_EmptyStellaPath_UsesStellaDefaultWithoutChangingTheBaProjection()
    {
        using var directory = TestDirectory.Create();
        var settings = LauncherSettings.CreateDefaults();
        settings.GamePath = Path.Combine(directory, "existing-ba", "BlueArchive_JP");
        settings.GetGameSettings("stella-sora-cn").GamePath = "";
        var installationPath = new FixedInstallationPath(Path.Combine(directory, "stella-default", "YostarGames", "StellaSora_CN"));
        using var registry = new GameContextRegistry(directory.DataRoot, (profile, paths) =>
            CreateContext(profile, paths, [], settings: settings, installationPath: installationPath));
        var context = registry.GetOrCreate("stella-sora-cn");

        var snapshot = await context.CoreService.LoadAsync();

        Assert.Equal(context.InstallationPath.GetDefaultGamePath(), snapshot.GameSettings.GamePath);
        Assert.Equal(context.InstallationPath.GetDefaultGamePath(), snapshot.LocalGame.GamePath);
        Assert.Equal(settings.GamePath, snapshot.Settings.GamePath);
    }

    [Theory]
    [InlineData("blue-archive-jp")]
    [InlineData("stella-sora-cn")]
    public void Snapshot_WithoutAnExplicitGameSettingsCopy_ClonesOnlyItsDeclaredGame(string gameId)
    {
        var settings = LauncherSettings.CreateDefaults();
        settings.GetGameSettings("blue-archive-jp").GamePath = "ba-path";
        settings.GetGameSettings("stella-sora-cn").GamePath = "stella-path";
        var snapshot = new LauncherStatusSnapshot { GameId = gameId, Settings = settings };

        Assert.Equal(settings.GetGameSettings(gameId).GamePath, snapshot.GameSettings.GamePath);
        Assert.NotSame(settings.GetGameSettings(gameId), snapshot.GameSettings);
        snapshot.GameSettings.GamePath = "snapshot-only";
        Assert.NotEqual("snapshot-only", settings.GetGameSettings(gameId).GamePath);
    }

    [Fact]
    public void Snapshot_WhenTheDeclaredGameSettingsAreMissing_RejectsInsteadOfUsingBaSettings()
    {
        var settings = LauncherSettings.CreateDefaults();
        settings.GamePath = "ba-path";
        settings.Games.Remove("stella-sora-cn");
        var snapshot = new LauncherStatusSnapshot { GameId = "stella-sora-cn", Settings = settings };

        Assert.Throws<InvalidOperationException>(() => snapshot.GameSettings);
    }

    private static LauncherGameContext CreateContext(
        YostarGameProfile profile,
        GameDataPaths paths,
        IReadOnlyList<IDisposable> ownedResources,
        IGameProcessTracker? tracker = null,
        RecordingApiClient? api = null,
        LauncherSettings? settings = null,
        IGameInstallationPath? installationPath = null)
    {
        var diagnostics = LocalDiagnostics.Owning(new UnifiedLogger(paths.Root));
        api ??= new RecordingApiClient();
        var store = new LocalInstallationStateStore(profile);
        installationPath ??= new GameInstallationPath(profile);
        var core = new LauncherStatusSnapshotService(profile.RuntimeId, api, store, installationPath,
            new FixedSettingsService(settings ?? LauncherSettings.CreateDefaults()), diagnostics);
        return new LauncherGameContext(profile, paths, api, store, installationPath, core,
            tracker ?? TestGameProcessTracker.None(), [diagnostics, .. ownedResources],
            new RunnerOutputCapture(paths), new CompatibilityEnvironmentPrecheck(paths), new PrefixMetadataStore(paths));
    }

    private sealed class FixedSettingsService(LauncherSettings settings) : ILauncherSettingsService
    {
        public Task<LauncherSettings> ReadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(settings.DeepClone());
    }

    private sealed class FixedInstallationPath(string defaultPath) : IGameInstallationPath
    {
        public string GetDefaultGamePath() => defaultPath;

        public string NormalizeGamePath(string path) => Path.GetFullPath(path);
    }

    private sealed class RecordingDisposable(string name, List<string> releases, bool fail = false) : IDisposable
    {
        public void Dispose()
        {
            releases.Add(name);
            if (fail)
            {
                throw new InvalidOperationException("simulated disposal failure");
            }
        }
    }

    private sealed class DisposableProcessTracker : IGameProcessTracker, IDisposable
    {
        public bool IsDisposed { get; private set; }

        public int DisposeCount { get; private set; }

        public event Action? TrackedProcessExited
        {
            add { }
            remove { }
        }

        public bool HasLiveTrackedProcess
        {
            get
            {
                ObjectDisposedException.ThrowIf(IsDisposed, this);
                return false;
            }
        }

        public GameLaunchExitInfo? LastExit => null;

        public void Register(GameProcess process) => throw new NotSupportedException();

        public Task<IReadOnlyList<string>> FindRunningGameProcessesAsync(RunningGameQuery query,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>([]);

        public void Dispose()
        {
            IsDisposed = true;
            DisposeCount++;
        }
    }

    private sealed class RecordingApiClient : ILauncherApiClient
    {
        public List<string> PatchGroups { get; } = [];

        public Task<GameConfigResponse> GetGameConfigAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new GameConfigResponse());

        public Task<BaseConfigResponse> GetBaseConfigAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new BaseConfigResponse());

        public Task<CdnConfigResponse> GetCdnConfigAsync(string patchUrlGroup, CancellationToken cancellationToken = default)
        {
            PatchGroups.Add(patchUrlGroup);
            return Task.FromResult(new CdnConfigResponse());
        }

        public Task<OperationsResourceResponse> GetOperationsResourceAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new OperationsResourceResponse());

        public Task<SocialMediaResourceResponse> GetSocialMediaResourceAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new SocialMediaResourceResponse());

        public Task<InstallationConfigResponse> GetInstallationConfigAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new InstallationConfigResponse());

        public Task<ManifestUrlResponse> GetManifestUrlAsync(string version, string filePath, string patchUrlGroup,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<RemoteManifest> GetRemoteManifestAsync(string url, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public string RestoreOfficialPackageUrl(string url) => url;
    }
}
