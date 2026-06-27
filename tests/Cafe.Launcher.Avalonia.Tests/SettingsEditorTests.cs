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
            PatchUrlGroup = PatchUrlGroups.Official,
            ToastNotificationsEnabled = true
        });

        editor.Current.PatchUrlGroup = PatchUrlGroups.Cafe;
        editor.Current.ToastNotificationsEnabled = false;

        var saved = editor.GetSavedSnapshot();

        Assert.Equal(PatchUrlGroups.Official, saved.PatchUrlGroup);
        Assert.True(saved.ToastNotificationsEnabled);
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
            CustomThemeColor = "#FF00FF00",
            DownloadSpeedLimit = DownloadSpeedLimits.Speed10MBs,
            ToastNotificationsEnabled = false,
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
        Assert.Equal("#FF00FF00", current.CustomThemeColor);
        Assert.Equal(DownloadSpeedLimits.Speed10MBs, current.DownloadSpeedLimit);
        Assert.False(current.ToastNotificationsEnabled);
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
        Assert.Equal(ProxyModes.Direct, current.ProxyMode);
        Assert.Equal(CloseBehaviors.Minimize, current.CloseBehavior);
        Assert.Equal(LauncherLanguages.Auto, current.Language);
        Assert.Equal(ThemeModes.System, current.ThemeMode);
        Assert.Equal(ThemeColorModes.Default, current.ThemeColorMode);
        Assert.Equal(LauncherConstants.DefaultThemeColor, current.CustomThemeColor);
        Assert.Empty(current.ThemeColorPalette);
        Assert.Equal(0, current.SelectedThemeColorPaletteIndex);
        Assert.Equal(DownloadSpeedLimits.Unlimited, current.DownloadSpeedLimit);
        Assert.True(current.ToastNotificationsEnabled);
        Assert.True(current.ShowRemoteContentCard);
        Assert.Equal(PatchUrlGroups.Official, current.PatchUrlGroup);
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
}
