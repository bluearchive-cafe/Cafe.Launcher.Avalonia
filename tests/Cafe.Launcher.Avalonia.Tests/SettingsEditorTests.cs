using System;
using System.ComponentModel;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

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
    public void CurrentPropertyChanged_ObserverSeesUpdatedDirtyState()
    {
        var editor = new SettingsEditor();
        editor.ApplySnapshot(new LauncherSettings { Language = LauncherLanguages.Auto });
        var dirtyWhenChanged = false;
        editor.CurrentPropertyChanged += (_, _) => dirtyWhenChanged = editor.IsDirty;

        editor.Current.Language = LauncherLanguages.Japanese;

        Assert.True(dirtyWhenChanged);
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
            MotionMode = MotionModes.Reduced,
            ThemeColorMode = ThemeColorModes.Custom,
            CustomThemeColor = "#FF00FF00",
            ThemeColorPalette = ["#FF00FF00", "#FF112233"],
            SelectedThemeColorPaletteIndex = 1,
            DownloadSpeedLimit = DownloadSpeedLimits.Speed10MBs,
            EnableStartupUpdateCheck = false,
            ShowRemoteContentCard = false,
            PatchUrlGroup = PatchUrlGroups.Cafe,
            CustomBackgroundPath = @"C:\wallpaper.png",
            BackgroundSource = BackgroundSources.Custom,
            BackgroundFit = BackgroundFits.Fill,
            BackgroundFillColor = "#FF112233",
            ResourcePanelUid = "12345",
            ResourcePanelUidSource = ResourcePanelUidSources.Custom,
            StatusDetailMode = StatusDetailModes.Compact,
            UpdateChannel = UpdateChannels.Beta,
            LogLevel = LogLevels.Debug
        };

        editor.ApplySnapshot(settings);

        var current = editor.Current;
        Assert.Equal(@"D:\Games", current.GamePath);
        Assert.Equal(LaunchCheckModes.RemoteManifest, current.LaunchCheckMode);
        Assert.Equal(ProxyModes.System, current.ProxyMode);
        Assert.Equal(CloseBehaviors.Exit, current.CloseBehavior);
        Assert.Equal(LauncherLanguages.Japanese, current.Language);
        Assert.Equal(ThemeModes.Dark, current.ThemeMode);
        Assert.Equal(MotionModes.Reduced, current.MotionMode);
        Assert.Equal(ThemeColorModes.Custom, current.ThemeColorMode);
        Assert.Equal("#FF00FF00", current.CustomThemeColor);
        Assert.Equal(["#FF00FF00", "#FF112233"], current.ThemeColorPalette);
        Assert.Equal(1, current.SelectedThemeColorPaletteIndex);
        Assert.Equal(DownloadSpeedLimits.Speed10MBs, current.DownloadSpeedLimit);
        Assert.False(current.EnableStartupUpdateCheck);
        Assert.False(current.ShowRemoteContentCard);
        Assert.Equal(PatchUrlGroups.Cafe, current.PatchUrlGroup);
        Assert.Equal(@"C:\wallpaper.png", current.CustomBackgroundPath);
        Assert.Equal(BackgroundSources.Custom, current.BackgroundSource);
        Assert.Equal(BackgroundFits.Fill, current.BackgroundFit);
        Assert.Equal("#FF112233", current.BackgroundFillColor);
        Assert.Equal("12345", current.ResourcePanelUid);
        Assert.Equal(ResourcePanelUidSources.Custom, current.ResourcePanelUidSource);
        Assert.Equal(StatusDetailModes.Compact, current.StatusDetailMode);
        Assert.Equal(UpdateChannels.Beta, current.UpdateChannel);
        Assert.Equal(LogLevels.Debug, current.LogLevel);
        Assert.False(editor.IsDirty);
    }

    [Fact]
    public void ApplySnapshot_WhenPaletteChangesLater_KeepsEditorPaletteIsolated()
    {
        var editor = new SettingsEditor();
        var settings = new LauncherSettings
        {
            ThemeColorPalette = ["#FF00FF00"],
            SelectedThemeColorPaletteIndex = 0
        };

        editor.ApplySnapshot(settings);
        settings.ThemeColorPalette.Add("#FF112233");
        var currentSnapshot = editor.GetSnapshot();
        currentSnapshot.ThemeColorPalette.Add("#FF445566");
        var savedSnapshot = editor.GetSavedSnapshot();
        savedSnapshot.ThemeColorPalette.Clear();

        Assert.Equal(["#FF00FF00"], editor.Current.ThemeColorPalette);
        Assert.Equal(["#FF00FF00"], editor.GetSnapshot().ThemeColorPalette);
        Assert.Equal(["#FF00FF00"], editor.GetSavedSnapshot().ThemeColorPalette);
        Assert.False(editor.IsDirty);
    }

    [Theory]
    [MemberData(nameof(PersistedSettingMutations))]
    public void CurrentPropertyChange_WhenAnyPersistedSettingChanges_MarksDirtyAndRevertingClearsDirty(
        Action<LauncherSettings> mutate,
        Action<LauncherSettings> revert)
    {
        var editor = new SettingsEditor();
        editor.ApplySnapshot(new LauncherSettings
        {
            ThemeColorPalette = ["#FF00FF00"],
            SelectedThemeColorPaletteIndex = 0,
            UpdateChannel = UpdateChannels.Stable,
            LogLevel = LogLevels.Information
        });

        mutate(editor.Current);

        Assert.True(editor.IsDirty);

        revert(editor.Current);

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
        var editor = new SettingsEditor();

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

    public static TheoryData<Action<LauncherSettings>, Action<LauncherSettings>> PersistedSettingMutations()
    {
        return new TheoryData<Action<LauncherSettings>, Action<LauncherSettings>>
        {
            { settings => settings.GamePath = @"D:\Games", settings => settings.GamePath = "" },
            { settings => settings.LaunchCheckMode = LaunchCheckModes.RemoteManifest, settings => settings.LaunchCheckMode = LaunchCheckModes.LocalManifest },
            { settings => settings.ProxyMode = ProxyModes.Direct, settings => settings.ProxyMode = ProxyModes.Auto },
            { settings => settings.CloseBehavior = CloseBehaviors.Exit, settings => settings.CloseBehavior = CloseBehaviors.Minimize },
            { settings => settings.Language = LauncherLanguages.Japanese, settings => settings.Language = LauncherLanguages.Auto },
            { settings => settings.ThemeMode = ThemeModes.Dark, settings => settings.ThemeMode = ThemeModes.System },
            { settings => settings.MotionMode = MotionModes.Reduced, settings => settings.MotionMode = MotionModes.System },
            { settings => settings.ThemeColorMode = ThemeColorModes.Custom, settings => settings.ThemeColorMode = ThemeColorModes.Default },
            { settings => settings.CustomThemeColor = "#FF112233", settings => settings.CustomThemeColor = LauncherConstants.DefaultThemeColor },
            { settings => settings.ThemeColorPalette = ["#FF112233"], settings => settings.ThemeColorPalette = ["#FF00FF00"] },
            { settings => settings.SelectedThemeColorPaletteIndex = 1, settings => settings.SelectedThemeColorPaletteIndex = 0 },
            { settings => settings.DownloadSpeedLimit = DownloadSpeedLimits.Speed10MBs, settings => settings.DownloadSpeedLimit = DownloadSpeedLimits.Unlimited },
            { settings => settings.EnableStartupUpdateCheck = false, settings => settings.EnableStartupUpdateCheck = true },
            { settings => settings.ShowRemoteContentCard = false, settings => settings.ShowRemoteContentCard = true },
            { settings => settings.PatchUrlGroup = PatchUrlGroups.Cafe, settings => settings.PatchUrlGroup = PatchUrlGroups.Official },
            { settings => settings.CustomBackgroundPath = @"C:\wallpaper.png", settings => settings.CustomBackgroundPath = "" },
            { settings => settings.BackgroundSource = BackgroundSources.Custom, settings => settings.BackgroundSource = BackgroundSources.Bundled },
            { settings => settings.BackgroundFit = BackgroundFits.Fill, settings => settings.BackgroundFit = BackgroundFits.UniformToFill },
            { settings => settings.BackgroundFillColor = "#FF112233", settings => settings.BackgroundFillColor = "#FF000000" },
            { settings => settings.ResourcePanelUid = "12345", settings => settings.ResourcePanelUid = "" },
            { settings => settings.ResourcePanelUidSource = ResourcePanelUidSources.Custom, settings => settings.ResourcePanelUidSource = ResourcePanelUidSources.Auto },
            { settings => settings.StatusDetailMode = StatusDetailModes.Hidden, settings => settings.StatusDetailMode = StatusDetailModes.Compact },
            { settings => settings.UpdateChannel = UpdateChannels.Beta, settings => settings.UpdateChannel = UpdateChannels.Stable },
            { settings => settings.LogLevel = LogLevels.Debug, settings => settings.LogLevel = LogLevels.Information }
        };
    }
}
