using Cafe.Launcher.Core.Constants;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Services;
using Cafe.Launcher.Testing;
using Cafe.Launcher.UI.Services;

namespace Cafe.Launcher.Tests;

public sealed class SettingsScopeTests
{
    [Fact]
    public async Task SaveScopedDraftAsync_GlobalEdit_PreservesOtherGamesAndNewerWindowState()
    {
        using var rig = new Rig();
        await rig.Writer.ReplaceAsync(new LauncherSettings());
        var baseline = rig.Editor.GetSavedSnapshot();
        var draft = baseline.DeepClone();
        draft.ThemeMode = ThemeModes.Dark;
        draft.GetGameSettings(LauncherProfiles.BlueArchiveJapan.RuntimeId).GamePath = "outside-scope";
        await rig.Writer.UpdateAsync(saved =>
        {
            saved.WindowWidth = 1440;
            saved.GetGameSettings(LauncherProfiles.StellaSoraChina.RuntimeId).AfterLaunchBehavior = AfterLaunchBehaviors.KeepOpen;
        });

        await rig.Writer.SaveScopedDraftAsync(draft, baseline, gameId: null);

        var saved = await rig.Service.ReadAsync();
        Assert.Equal(ThemeModes.Dark, saved.ThemeMode);
        Assert.Equal(1440, saved.WindowWidth);
        Assert.Empty(saved.GamePath);
        Assert.Equal(AfterLaunchBehaviors.KeepOpen, saved.GetGameSettings(LauncherProfiles.StellaSoraChina.RuntimeId).AfterLaunchBehavior);
        Assert.False(rig.Editor.IsDirty);
    }

    [Theory]
    [InlineData("blue-archive-jp")]
    [InlineData("stella-sora-cn")]
    public async Task SaveScopedDraftAsync_GameEdit_OnlyCommitsItsChangesAndRetainsConcurrentRuntimeFields(string gameId)
    {
        using var rig = new Rig();
        await rig.Writer.ReplaceAsync(new LauncherSettings());
        var baseline = rig.Editor.GetSavedSnapshot();
        var draft = baseline.DeepClone();
        draft.ThemeMode = ThemeModes.Dark;
        draft.GetGameSettings(gameId).UseBackgroundOverride = true;
        draft.GetGameSettings(gameId).BackgroundSource = BackgroundSources.Custom;
        draft.GetGameSettings(gameId).CustomBackgroundPath = "game-image.png";
        draft.GetGameSettings(gameId).GameRuntime.EnableMangoHud = true;
        await rig.Writer.UpdateGameAsync(gameId, game => game.GameRuntime.PrefixPath = "/newer-prefix");

        await rig.Writer.SaveScopedDraftAsync(draft, baseline, gameId);

        var saved = await rig.Service.ReadAsync();
        var game = saved.GetGameSettings(gameId);
        Assert.Equal(ThemeModes.System, saved.ThemeMode);
        Assert.True(game.UseBackgroundOverride);
        Assert.Equal("game-image.png", game.CustomBackgroundPath);
        Assert.Equal("/newer-prefix", game.GameRuntime.PrefixPath);
        Assert.True(game.GameRuntime.EnableMangoHud);
        Assert.False(saved.Games.Single(pair => pair.Key != gameId).Value.UseBackgroundOverride);
    }

    [Fact]
    public async Task UpdateAsync_WhileAppearanceIsDirty_PreservesDraftAndAdvancesSavedBaseline()
    {
        using var rig = new Rig();
        await rig.Writer.ReplaceAsync(new LauncherSettings());
        rig.Editor.Current.ThemeMode = ThemeModes.Dark;

        await rig.Writer.UpdateAsync(saved => saved.WindowHeight = 840);

        Assert.Equal(ThemeModes.Dark, rig.Editor.Current.ThemeMode);
        Assert.Equal(ThemeModes.System, rig.Editor.GetSavedSnapshot().ThemeMode);
        Assert.Equal(840, rig.Editor.Current.WindowHeight);
        Assert.True(rig.Editor.IsScopeDirty(gameId: null));
        rig.Editor.Discard();
        Assert.Equal(840, rig.Editor.Current.WindowHeight);
        Assert.Equal(ThemeModes.System, rig.Editor.Current.ThemeMode);
    }

    [Fact]
    public async Task SaveScopedDraftAsync_UninitializedLegacyEditor_RefusesToOverwriteMigrationData()
    {
        using var rig = new Rig();
        const string original = """{"gamePath":"must-survive","language":"ja"}""";
        await File.WriteAllTextAsync(rig.Directory.DataRoot.SettingsPath, original);
        var baseline = rig.Editor.GetSavedSnapshot();
        var draft = baseline.DeepClone();
        draft.ThemeMode = ThemeModes.Dark;

        await Assert.ThrowsAsync<SettingsFormatException>(() => rig.Writer.SaveScopedDraftAsync(draft, baseline, gameId: null));

        Assert.Equal(original, await File.ReadAllTextAsync(rig.Directory.DataRoot.SettingsPath));
    }

    [Fact]
    public async Task SaveScopedDraftAsync_BackgroundDuringDownload_DoesNotAcquireAnOperationalSettingsLease()
    {
        using var rig = new Rig();
        await rig.Writer.ReplaceAsync(new LauncherSettings());
        var gameId = LauncherProfiles.BlueArchiveJapan.RuntimeId;
        using var taskLease = rig.Operations.TryAcquire(gameId, GameFileOperationKind.Download).Lease!;
        var baseline = rig.Editor.GetSavedSnapshot();
        var draft = baseline.DeepClone();
        draft.GetGameSettings(gameId).UseBackgroundOverride = true;
        draft.GetGameSettings(gameId).BackgroundVideoVolume = 150;

        await rig.Writer.SaveScopedDraftAsync(draft, baseline, gameId);

        Assert.Equal(100, rig.Editor.Current.GetGameSettings(gameId).BackgroundVideoVolume);
        Assert.False(rig.Editor.IsDirty);
        Assert.Equal(gameId, rig.Operations.ActiveOperation!.GameId);
        baseline = rig.Editor.GetSavedSnapshot();
        draft = baseline.DeepClone();
        draft.GetGameSettings(gameId).GamePath = rig.Directory.Sub("new-install");
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Writer.SaveScopedDraftAsync(draft, baseline, gameId));
        Assert.Empty((await rig.Service.ReadAsync()).GamePath);
    }

    private sealed class Rig : IDisposable
    {
        internal TestDirectory Directory { get; } = TestDirectory.Create();
        internal SettingsEditor Editor { get; } = new();
        internal GameFileOperationCoordinator Operations { get; } = new();
        internal LauncherSettingsService Service { get; }
        internal SavedSettingsWriter Writer { get; }

        internal Rig()
        {
            Service = new LauncherSettingsService(Directory.DataRoot);
            Writer = new SavedSettingsWriter(Service, Editor, Operations);
        }

        public void Dispose()
        {
            Writer.Dispose();
            Service.Dispose();
            Directory.Dispose();
        }
    }
}
