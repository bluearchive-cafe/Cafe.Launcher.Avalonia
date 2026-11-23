using System.ComponentModel;
using Cafe.Launcher.Constants;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Constants;

namespace Cafe.Launcher.Tests;

public sealed class SettingsEditorTests
{
    [Fact]
    public void GetSavedSnapshot_WhenCurrentHasUnsavedChanges_ReturnsAppliedSnapshot()
    {
        var editor = new SettingsEditor();
        editor.ApplySnapshot(new LauncherSettings
        {
            PatchUrlGroup = PatchUrlGroups.Official
        });

        editor.Current.PatchUrlGroup = PatchUrlGroups.Cafe;

        var saved = editor.GetSavedSnapshot();

        Assert.Equal(PatchUrlGroups.Official, saved.PatchUrlGroup);
    }

    [Fact]
    public void CurrentPropertyChange_MarksDirtyAndDiscardRestoresSnapshot()
    {
        var editor = new SettingsEditor();
        editor.ApplySnapshot(new LauncherSettings { Language = LauncherLanguages.Auto });

        editor.Current.Language = LauncherLanguages.Japanese;

        Assert.True(editor.IsDirty);
        Assert.Equal(LauncherLanguages.Japanese, editor.Current.Language);

        editor.Discard();

        Assert.False(editor.IsDirty);
        Assert.Equal(LauncherLanguages.Auto, editor.Current.Language);
    }

    [Fact]
    public void CurrentPropertyChange_WhenRevertedToSavedValue_ClearsDirty()
    {
        var editor = new SettingsEditor();
        editor.ApplySnapshot(new LauncherSettings { Language = LauncherLanguages.Auto });

        editor.Current.Language = LauncherLanguages.Japanese;
        editor.Current.Language = LauncherLanguages.Auto;

        Assert.False(editor.IsDirty);
    }

    [Fact]
    public void EnableHttp2Change_MarksDirtyAndDiscardRestoresSnapshot()
    {
        var editor = new SettingsEditor();
        editor.ApplySnapshot(new LauncherSettings { EnableHttp2 = false });

        editor.Current.EnableHttp2 = true;

        Assert.True(editor.IsDirty);

        editor.Discard();

        Assert.False(editor.IsDirty);
        Assert.False(editor.Current.EnableHttp2);
    }

    [Fact]
    public void ApplySnapshot_LoadsAllFields()
    {
        var editor = new SettingsEditor();
        var settings = new LauncherSettings
        {
            GamePath = @"D:\Games",
            LaunchCheckMode = LaunchCheckModes.RemoteManifest,
            ProxyMode = ProxyModes.System,
            CloseBehavior = CloseBehaviors.Exit,
            Language = LauncherLanguages.Japanese,
            ThemeMode = ThemeModes.Dark,
            ThemeColorMode = ThemeColorModes.Custom,
            ThemeColorExtractionAlgorithm = ThemeColorExtractionAlgorithms.Wu,
            ThemeColorVariant = ThemeColorVariants.Expressive,
            NeutralColorStrategy = NeutralColorStrategies.SeedFollowing,
            CustomThemeColor = "#FF00FF00",
            DownloadSpeedLimit = DownloadSpeedLimits.Speed10MBs,
            EnableStartupUpdateCheck = false,
            ShowRemoteContentCard = false,
            PatchUrlGroup = PatchUrlGroups.Cafe,
            CustomBackgroundPath = @"C:\wallpaper.png",
            BackgroundSource = BackgroundSources.Custom,
            BackgroundFit = BackgroundFits.Fill,
            BackgroundFillColor = "#FF112233",
            ResourcePanelUid = "12345",
            UpdateChannel = UpdateChannels.Beta
        };

        editor.ApplySnapshot(settings);

        var current = editor.Current;
        Assert.Equal(@"D:\Games", current.GamePath);
        Assert.Equal(LaunchCheckModes.RemoteManifest, current.LaunchCheckMode);
        Assert.Equal(ProxyModes.System, current.ProxyMode);
        Assert.Equal(CloseBehaviors.Exit, current.CloseBehavior);
        Assert.Equal(LauncherLanguages.Japanese, current.Language);
        Assert.Equal(ThemeModes.Dark, current.ThemeMode);
        Assert.Equal(ThemeColorModes.Custom, current.ThemeColorMode);
        Assert.Equal(ThemeColorExtractionAlgorithms.Wu, current.ThemeColorExtractionAlgorithm);
        Assert.Equal(ThemeColorVariants.Expressive, current.ThemeColorVariant);
        Assert.Equal(NeutralColorStrategies.SeedFollowing, current.NeutralColorStrategy);
        Assert.Equal("#FF00FF00", current.CustomThemeColor);
        Assert.Equal(DownloadSpeedLimits.Speed10MBs, current.DownloadSpeedLimit);
        Assert.False(current.EnableStartupUpdateCheck);
        Assert.False(current.ShowRemoteContentCard);
        Assert.Equal(PatchUrlGroups.Cafe, current.PatchUrlGroup);
        Assert.Equal(@"C:\wallpaper.png", current.CustomBackgroundPath);
        Assert.Equal(BackgroundSources.Custom, current.BackgroundSource);
        Assert.Equal(BackgroundFits.Fill, current.BackgroundFit);
        Assert.Equal("#FF112233", current.BackgroundFillColor);
        Assert.Equal("12345", current.ResourcePanelUid);
        Assert.Equal(UpdateChannels.Beta, current.UpdateChannel);
        Assert.False(editor.IsDirty);
    }

    [Fact]
    public void Commit_ModifiesField_IsDirtyTrue()
    {
        var editor = new SettingsEditor();
        editor.ApplySnapshot(new LauncherSettings { Language = LauncherLanguages.Auto });

        editor.Commit(s => s.Language = LauncherLanguages.Japanese);

        Assert.Equal(LauncherLanguages.Japanese, editor.Current.Language);
        Assert.True(editor.IsDirty);
    }

    [Fact]
    public void CurrentPropertyChange_WhenM3AppearanceSettingChanges_MarksDirtyAndDiscardRestoresSnapshot()
    {
        var editor = new SettingsEditor();
        editor.ApplySnapshot(new LauncherSettings
        {
            ThemeColorExtractionAlgorithm = ThemeColorExtractionAlgorithms.CelebiScore,
            ThemeColorVariant = ThemeColorVariants.TonalSpot,
            NeutralColorStrategy = NeutralColorStrategies.BrandBlue
        });

        editor.Current.ThemeColorExtractionAlgorithm = ThemeColorExtractionAlgorithms.Wu;
        editor.Current.ThemeColorVariant = ThemeColorVariants.Expressive;
        editor.Current.NeutralColorStrategy = NeutralColorStrategies.SeedFollowing;

        Assert.True(editor.IsDirty);

        editor.Discard();

        Assert.False(editor.IsDirty);
        Assert.Equal(ThemeColorExtractionAlgorithms.CelebiScore, editor.Current.ThemeColorExtractionAlgorithm);
        Assert.Equal(ThemeColorVariants.TonalSpot, editor.Current.ThemeColorVariant);
        Assert.Equal(NeutralColorStrategies.BrandBlue, editor.Current.NeutralColorStrategy);
    }

    [Fact]
    public void CurrentPropertyChange_WhenRememberWindowPositionAndSizeChanges_MarksDirty()
    {
        var editor = new SettingsEditor();
        editor.ApplySnapshot(new LauncherSettings { RememberWindowPositionAndSize = false });

        editor.Current.RememberWindowPositionAndSize = true;

        Assert.True(editor.IsDirty);
    }

    [Fact]
    public void Commit_ModifiesMultipleFields_AllApplied()
    {
        var editor = new SettingsEditor();
        editor.ApplySnapshot(new LauncherSettings { Language = LauncherLanguages.Auto, ProxyMode = ProxyModes.Direct });

        editor.Commit(s =>
        {
            s.Language = LauncherLanguages.Japanese;
            s.ProxyMode = ProxyModes.System;
        });

        Assert.Equal(LauncherLanguages.Japanese, editor.Current.Language);
        Assert.Equal(ProxyModes.System, editor.Current.ProxyMode);
        Assert.True(editor.IsDirty);
    }

    [Fact]
    public void ApplySnapshot_AfterCommit_ClearsDirty()
    {
        var editor = new SettingsEditor();
        editor.ApplySnapshot(new LauncherSettings { Language = LauncherLanguages.Auto });
        editor.Commit(s => s.Language = LauncherLanguages.Japanese);
        Assert.True(editor.IsDirty);

        editor.ApplySnapshot(new LauncherSettings { Language = LauncherLanguages.Japanese });

        Assert.False(editor.IsDirty);
        Assert.Equal(LauncherLanguages.Japanese, editor.Current.Language);
    }

    [Fact]
    public void Discard_RevertsToLastSnapshot()
    {
        var editor = new SettingsEditor();
        editor.ApplySnapshot(new LauncherSettings
        {
            Language = LauncherLanguages.Auto,
            ThemeMode = ThemeModes.System
        });
        editor.Commit(s =>
        {
            s.Language = LauncherLanguages.Japanese;
            s.ThemeMode = ThemeModes.Dark;
        });

        editor.Discard();

        Assert.Equal(LauncherLanguages.Auto, editor.Current.Language);
        Assert.Equal(ThemeModes.System, editor.Current.ThemeMode);
        Assert.False(editor.IsDirty);
    }

    [Fact]
    public void Discard_WithoutModification_NoOp()
    {
        var editor = new SettingsEditor();
        editor.ApplySnapshot(new LauncherSettings { Language = LauncherLanguages.Japanese });

        editor.Discard();

        Assert.Equal(LauncherLanguages.Japanese, editor.Current.Language);
        Assert.False(editor.IsDirty);
    }

    [Fact]
    public void GetSnapshot_ReturnsCompleteLauncherSettings()
    {
        var editor = new SettingsEditor();
        editor.ApplySnapshot(new LauncherSettings
        {
            Language = LauncherLanguages.English,
            ThemeMode = ThemeModes.Light,
            ProxyMode = ProxyModes.Direct
        });

        var snapshot = editor.GetSnapshot();
        snapshot.Language = LauncherLanguages.Japanese;

        Assert.Equal(LauncherLanguages.Japanese, snapshot.Language);
        Assert.Equal(LauncherLanguages.English, editor.Current.Language);
        Assert.Equal(ThemeModes.Light, snapshot.ThemeMode);
        Assert.Equal(ProxyModes.Direct, snapshot.ProxyMode);
        // Verify all default-valued fields are present (not null/missing)
        Assert.NotNull(snapshot.GamePath);
        Assert.NotNull(snapshot.LaunchCheckMode);
    }

    [Fact]
    public void DefaultValues_MatchLauncherSettingsDefaults()
    {
        // 生产组合根注入宿主身份；不带身份的编辑器在预发布构建上会退化为稳定通道默认值。
        var editor = new SettingsEditor(BuildInfo.Identity);

        var current = editor.Current;
        Assert.Equal("", current.GamePath);
        Assert.Equal(LaunchCheckModes.LocalManifest, current.LaunchCheckMode);
        Assert.Equal(ProxyModes.Auto, current.ProxyMode);
        Assert.Equal(CloseBehaviors.Minimize, current.CloseBehavior);
        Assert.Equal(LauncherLanguages.Auto, current.Language);
        Assert.Equal(ThemeModes.System, current.ThemeMode);
        Assert.Equal(ThemeColorModes.Default, current.ThemeColorMode);
        Assert.Equal(LauncherConstants.DefaultThemeColor, current.CustomThemeColor);
        Assert.Empty(current.ThemeColorPalette);
        Assert.Equal(0, current.SelectedThemeColorPaletteIndex);
        Assert.Equal(DownloadSpeedLimits.Unlimited, current.DownloadSpeedLimit);
        Assert.True(current.EnableStartupUpdateCheck);
        Assert.True(current.ShowRemoteContentCard);
        // PatchUrlGroup defaults to Cafe when UI culture is Chinese, otherwise Official.
        var expectedGroup = System.Globalization.CultureInfo.CurrentUICulture.Name is
            "zh-CN" or "zh-TW" or "zh-HK" or "zh-MO" or "zh-SG" or "zh-Hans" or "zh-Hant"
            ? PatchUrlGroups.Cafe
            : PatchUrlGroups.Official;
        Assert.Equal(expectedGroup, current.PatchUrlGroup);
        Assert.Equal("", current.CustomBackgroundPath);
        Assert.Equal(BackgroundSources.Bundled, current.BackgroundSource);
        Assert.Equal(BackgroundFits.UniformToFill, current.BackgroundFit);
        Assert.Equal("#FF000000", current.BackgroundFillColor);
        Assert.Equal("", current.ResourcePanelUid);
        Assert.Equal(
            BuildInfo.LauncherVersion.Contains('-', StringComparison.Ordinal)
                ? UpdateChannels.Beta
                : UpdateChannels.Stable,
            current.UpdateChannel);
    }

    [Fact]
    public void PropertyChanged_FiresOnCommit()
    {
        var editor = new SettingsEditor();
        editor.ApplySnapshot(new LauncherSettings());
        string? changedProperty = null;
        editor.PropertyChanged += (_, e) => changedProperty = e.PropertyName;

        editor.Commit(s => s.Language = LauncherLanguages.Japanese);

        // Current and IsDirty should both fire
        Assert.NotNull(changedProperty);
    }

    [Fact]
    public void PropertyChanged_FiresOnApplySnapshot()
    {
        var editor = new SettingsEditor();
        string? changedProperty = null;
        editor.PropertyChanged += (_, e) => changedProperty = e.PropertyName;

        editor.ApplySnapshot(new LauncherSettings { Language = LauncherLanguages.Japanese });

        Assert.NotNull(changedProperty);
    }

    [Fact]
    public void PropertyChanged_FiresOnDiscard()
    {
        var editor = new SettingsEditor();
        editor.ApplySnapshot(new LauncherSettings { Language = LauncherLanguages.Auto });
        editor.Commit(s => s.Language = LauncherLanguages.Japanese);
        string? changedProperty = null;
        editor.PropertyChanged += (_, e) => changedProperty = e.PropertyName;

        editor.Discard();

        Assert.NotNull(changedProperty);
    }

    [Fact]
    public void GameRuntimeRunnerChange_WhenDiscarded_MarksDirtyThenRestoresSnapshot()
    {
        var editor = new SettingsEditor();
        editor.ApplySnapshot(new LauncherSettings());

        editor.Current.GameRuntime.Runner = GameRuntimeRunners.Umu;

        Assert.True(editor.IsDirty);

        editor.Discard();

        Assert.False(editor.IsDirty);
        Assert.Equal(GameRuntimeRunners.Auto, editor.Current.GameRuntime.Runner);
    }

    [Fact]
    public void GameRuntimePathChange_WhenReverted_MarksDirtyThenClearsIt()
    {
        var editor = new SettingsEditor();
        editor.ApplySnapshot(new LauncherSettings());

        editor.Current.GameRuntime.RunnerPath = "/opt/umu/bin/umu-run";
        Assert.True(editor.IsDirty);

        editor.Current.GameRuntime.PrefixPath = "/home/user/prefixes/ba";
        Assert.True(editor.IsDirty);

        editor.Current.GameRuntime.RunnerPath = null;
        editor.Current.GameRuntime.PrefixPath = null;
        Assert.False(editor.IsDirty);
    }

    [Fact]
    public void GameRuntimePropertyChange_WhenNestedValueChanges_RaisesCurrentPropertyChanged()
    {
        var editor = new SettingsEditor();
        editor.ApplySnapshot(new LauncherSettings());
        var propertyNames = new List<string>();
        editor.CurrentPropertyChanged += (_, e) => propertyNames.Add(e.PropertyName ?? string.Empty);

        editor.Current.GameRuntime.ProtonPath = "/home/user/Proton";

        Assert.Contains(nameof(GameRuntimeSettings.ProtonPath), propertyNames);
    }

    [Fact]
    public void StellaGamePropertyChange_WhenDiscarded_RestoresOnlySavedGameState()
    {
        var editor = new SettingsEditor();
        editor.ApplySnapshot(new LauncherSettings { GamePath = @"C:\Games\BlueArchive_JP" });
        var stella = editor.Current.GetGameSettings(LauncherProfiles.StellaSoraChina.RuntimeId);
        var propertyNames = new List<string?>();
        editor.CurrentPropertyChanged += (_, args) => propertyNames.Add(args.PropertyName);

        stella.GamePath = @"D:\Games\StellaSora_CN";

        Assert.True(editor.IsDirty);
        Assert.Equal(nameof(GameSettings.GamePath), Assert.Single(propertyNames));
        Assert.Equal(@"C:\Games\BlueArchive_JP", editor.Current.GetGameSettings(LauncherProfiles.BlueArchiveJapan.RuntimeId).GamePath);
        Assert.Empty(editor.GetSavedSnapshot().GetGameSettings(LauncherProfiles.StellaSoraChina.RuntimeId).GamePath);

        editor.Discard();

        Assert.False(editor.IsDirty);
        Assert.Empty(editor.Current.GetGameSettings(LauncherProfiles.StellaSoraChina.RuntimeId).GamePath);
        Assert.Equal(@"C:\Games\BlueArchive_JP", editor.Current.GetGameSettings(LauncherProfiles.BlueArchiveJapan.RuntimeId).GamePath);
        propertyNames.Clear();
        stella.GamePath = "detached";
        Assert.Empty(propertyNames);
        Assert.False(editor.IsDirty);
    }

    [Fact]
    public void StellaRuntimePropertyChange_WhenReverted_NotifiesAndClearsDirty()
    {
        var editor = new SettingsEditor();
        editor.ApplySnapshot(new LauncherSettings());
        var runtime = editor.Current.GetGameSettings(LauncherProfiles.StellaSoraChina.RuntimeId).GameRuntime;
        var propertyNames = new List<string?>();
        editor.CurrentPropertyChanged += (_, args) => propertyNames.Add(args.PropertyName);

        runtime.ProtonPath = "/home/user/StellaProton";

        Assert.True(editor.IsDirty);
        Assert.Equal(nameof(GameRuntimeSettings.ProtonPath), Assert.Single(propertyNames));
        Assert.Null(editor.Current.GameRuntime.ProtonPath);

        runtime.ProtonPath = null;

        Assert.False(editor.IsDirty);
        Assert.Equal(2, propertyNames.Count);
    }

    [Fact]
    public void StellaRuntimePropertyChange_WhenDiscarded_RestoresSavedRuntime()
    {
        var editor = new SettingsEditor();
        editor.ApplySnapshot(new LauncherSettings());
        var previousRuntime = editor.Current.GetGameSettings(LauncherProfiles.StellaSoraChina.RuntimeId).GameRuntime;
        previousRuntime.Runner = GameRuntimeRunners.Wine;
        previousRuntime.PrefixPath = "/home/user/prefixes/stella";

        editor.Discard();

        var restored = editor.Current.GetGameSettings(LauncherProfiles.StellaSoraChina.RuntimeId).GameRuntime;
        Assert.False(editor.IsDirty);
        Assert.Equal(GameRuntimeRunners.Auto, restored.Runner);
        Assert.Null(restored.PrefixPath);
        Assert.NotSame(previousRuntime, restored);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GameRuntimeReplacement_UnsubscribesPreviousRuntimeAndObservesNewRuntime(bool useStella)
    {
        var editor = new SettingsEditor();
        editor.ApplySnapshot(new LauncherSettings());
        var gameId = useStella ? LauncherProfiles.StellaSoraChina.RuntimeId : LauncherProfiles.BlueArchiveJapan.RuntimeId;
        var game = editor.Current.GetGameSettings(gameId);
        var oldRuntime = game.GameRuntime;
        var replacement = new GameRuntimeSettings();
        var propertyNames = new List<string?>();
        editor.CurrentPropertyChanged += (_, args) => propertyNames.Add(args.PropertyName);

        game.GameRuntime = replacement;

        Assert.Equal(nameof(GameSettings.GameRuntime), Assert.Single(propertyNames));
        Assert.False(editor.IsDirty);
        propertyNames.Clear();
        oldRuntime.RunnerPath = "detached-runner";
        Assert.Empty(propertyNames);
        Assert.False(editor.IsDirty);

        replacement.RunnerPath = "observed-runner";

        Assert.True(editor.IsDirty);
        Assert.Equal(nameof(GameRuntimeSettings.RunnerPath), Assert.Single(propertyNames));
        replacement.RunnerPath = null;
        Assert.False(editor.IsDirty);
    }

    [Fact]
    public void GamesReplacement_UnsubscribesPreviousGamesAndRuntimesAndObservesNewGames()
    {
        var editor = new SettingsEditor();
        editor.ApplySnapshot(new LauncherSettings());
        var previousGames = editor.Current.Games;
        var replacement = editor.Current.DeepClone().Games;
        var propertyNames = new List<string?>();
        editor.CurrentPropertyChanged += (_, args) => propertyNames.Add(args.PropertyName);

        editor.Current.Games = replacement;

        Assert.Equal(nameof(LauncherSettings.Games), Assert.Single(propertyNames));
        Assert.False(editor.IsDirty);
        propertyNames.Clear();
        foreach (var game in previousGames.Values)
        {
            game.GamePath = "detached-game";
            game.GameRuntime.PrefixPath = "detached-prefix";
        }

        Assert.Empty(propertyNames);
        Assert.False(editor.IsDirty);
        editor.Current.GetGameSettings(LauncherProfiles.StellaSoraChina.RuntimeId).GamePath = "observed-game";

        Assert.True(editor.IsDirty);
        Assert.Equal(nameof(GameSettings.GamePath), Assert.Single(propertyNames));
        editor.Current.GetGameSettings(LauncherProfiles.StellaSoraChina.RuntimeId).GamePath = "";
        Assert.False(editor.IsDirty);

        propertyNames.Clear();
        editor.Current.GetGameSettings(LauncherProfiles.StellaSoraChina.RuntimeId).GameRuntime.RunnerPath = "observed-runtime";
        Assert.True(editor.IsDirty);
        Assert.Equal(nameof(GameRuntimeSettings.RunnerPath), Assert.Single(propertyNames));
    }

    [Theory]
    [InlineData(nameof(LauncherSettings.GamePath))]
    [InlineData(nameof(LauncherSettings.PatchUrlGroup))]
    [InlineData(nameof(LauncherSettings.LaunchCheckMode))]
    [InlineData(nameof(LauncherSettings.GameRuntime))]
    [InlineData(nameof(LauncherSettings.AfterLaunchBehavior))]
    [InlineData(nameof(LauncherSettings.ResourcePanelUid))]
    [InlineData(nameof(LauncherSettings.ResourcePanelUidSource))]
    public void CompatibilityAliasChange_WhenRootForwardsChildChange_NotifiesEditorOnce(string propertyName)
    {
        var editor = new SettingsEditor();
        editor.ApplySnapshot(new LauncherSettings { LastViewedGameId = LauncherProfiles.StellaSoraChina.RuntimeId });
        var propertyNames = new List<string?>();
        editor.CurrentPropertyChanged += (_, args) => propertyNames.Add(args.PropertyName);
        var property = typeof(LauncherSettings).GetProperty(propertyName)!;
        object value = property.PropertyType == typeof(GameRuntimeSettings)
            ? new GameRuntimeSettings { Runner = GameRuntimeRunners.Wine }
            : "changed-value";

        property.SetValue(editor.Current, value);

        Assert.True(editor.IsDirty);
        Assert.Equal(propertyName, Assert.Single(propertyNames));
    }

    [Fact]
    public void ApplySnapshot_WhenPreviousGamesLaterChange_DoesNotNotifyOrDirtyTheNewDraft()
    {
        var editor = new SettingsEditor();
        var previous = editor.Current;
        editor.ApplySnapshot(new LauncherSettings());
        var propertyNames = new List<string?>();
        editor.CurrentPropertyChanged += (_, args) => propertyNames.Add(args.PropertyName);

        foreach (var game in previous.Games.Values)
        {
            game.GamePath = "detached-game";
            game.GameRuntime.RunnerPath = "detached-runtime";
        }

        Assert.Empty(propertyNames);
        Assert.False(editor.IsDirty);
    }
}
