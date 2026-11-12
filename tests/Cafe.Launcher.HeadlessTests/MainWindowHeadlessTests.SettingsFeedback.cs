using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.UI.Constants;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.Services;

namespace Cafe.Launcher.HeadlessTests;

public sealed partial class MainWindowHeadlessTests
{
    [AvaloniaTheory]
    [InlineData(1024, 640, false)]
    [InlineData(1024, 640, true)]
    [InlineData(1300, 754, false)]
    [InlineData(1300, 754, true)]
    public void SettingsFeedback_WhenCategoryChanges_ResetsScrollWithoutChangingDraftOrFocus(double width, double height, bool keyboard)
    {
        using var context = CreateContext();
        PrepareGoldenWindow(context);
        context.Window.Width = width;
        context.Window.Height = height;
        OpenSettings(context);
        var settings = context.ViewModel.Settings;
        settings.SelectedCategory = SettingsCategoryCodes.Appearance;
        settings.Appearance.IsAdvancedColorExpanded = true;
        Dispatcher.UIThread.RunJobs();
        var navigation = GetSettingsNavigation(context.Window);
        navigation.Focus(NavigationMethod.Tab);
        var scroll = context.Window.GetVisualDescendants().OfType<ScrollViewer>()
            .Single(control => control.Classes.Contains("settings-content-scroll"));
        scroll.Offset = new Vector(0, 120);
        Dispatcher.UIThread.RunJobs();
        Assert.True(scroll.Offset.Y > 0);

        if (keyboard)
        {
            context.Window.KeyPress(Key.Home, RawInputModifiers.None, PhysicalKey.Home, "");
            context.Window.KeyRelease(Key.Home, RawInputModifiers.None, PhysicalKey.Home, "");
        }
        else
        {
            settings.SelectedCategory = SettingsCategoryCodes.General;
        }
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(SettingsCategoryCodes.General, settings.SelectedCategory);
        Assert.Equal(0, scroll.Offset.Y);
        Assert.True(navigation.IsKeyboardFocusWithin);
        Assert.False(settings.IsSettingsDirty);

        scroll.Offset = new Vector(0, 20);
        Dispatcher.UIThread.RunJobs();
        Assert.True(scroll.Offset.Y > 0);
        settings.SelectedCategory = SettingsCategoryCodes.Appearance;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, scroll.Offset.Y);
        Assert.True(settings.Appearance.IsAdvancedColorExpanded);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SettingsFeedback_WhenSaving_ShowsBusyUntilCompletionAndKeepsFailedDraftForRetry(bool failPersistence)
    {
        using var context = CreateContext();
        PrepareGoldenWindow(context);
        OpenSettings(context);
        var settings = context.ViewModel.Settings;
        var status = context.Window.GetVisualDescendants().OfType<TextBlock>()
            .Single(control => control.Name == "SettingsSaveStatus");
        var save = context.Window.GetVisualDescendants().OfType<Button>()
            .Single(control => ReferenceEquals(control.Command, settings.SaveSettingsCommand));
        var progress = context.Window.GetVisualDescendants().OfType<ProgressBar>()
            .Single(control => control.Classes.Contains("settings-save-progress"));
        Assert.False(status.IsEffectivelyVisible);
        var preview = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        settings.PreviewAppearanceAsync = (_, _, _) => preview.Task;
        settings.Editor.Current.ThemeMode = ThemeModes.Dark;
        settings.SelectedCategory = SettingsCategoryCodes.About;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(context.ViewModel.Shell.I18n[LocalizationKeys.UnsavedChanges], status.Text);
        Assert.True(status.IsEffectivelyVisible);
        Assert.False(progress.IsEffectivelyVisible);
        Assert.True(save.IsEffectivelyEnabled);
        var path = context.Directory.DataRoot.SettingsPath;
        if (failPersistence)
            Directory.CreateDirectory(path);

        var saving = settings.SaveSettingsCommand.ExecuteAsync(null);
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.False(saving.IsCompleted);
            Assert.True(settings.IsSaving);
            Assert.Equal(context.ViewModel.Shell.I18n[LocalizationKeys.SettingsSaving], status.Text);
            Assert.Equal(status.Text, AutomationProperties.GetName(save));
            Assert.True(progress.IsEffectivelyVisible);
            Assert.True(progress.IsIndeterminate);
            Assert.True(progress.Bounds.Width >= 100);
            Assert.False(save.IsEffectivelyEnabled);
            Assert.False(GetSettingsNavigation(context.Window).IsEffectivelyEnabled);
        }
        finally
        {
            preview.TrySetResult();
            await saving;
        }

        Dispatcher.UIThread.RunJobs();
        Assert.False(settings.IsSaving);
        Assert.False(progress.IsEffectivelyVisible);
        Assert.Equal(context.ViewModel.Shell.I18n[LocalizationKeys.Save], AutomationProperties.GetName(save));
        Assert.Equal(failPersistence, settings.IsSettingsDirty);
        Assert.Equal(failPersistence, save.IsEffectivelyEnabled);
        Assert.Equal(failPersistence, status.IsEffectivelyVisible);
        if (failPersistence)
        {
            Assert.Equal(context.ViewModel.Shell.I18n[LocalizationKeys.UnsavedChanges], status.Text);
            Directory.Delete(path);
            await settings.SaveSettingsCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(settings.IsSettingsDirty);
            Assert.False(status.IsEffectivelyVisible);
        }
    }

    [AvaloniaTheory]
    [InlineData(LauncherLanguages.English)]
    [InlineData(LauncherLanguages.SimplifiedChinese)]
    [InlineData(LauncherLanguages.TraditionalChinese)]
    [InlineData(LauncherLanguages.Japanese)]
    public async Task SettingsFeedback_WhenLocalized_FitsFooterAcrossCategoriesAndTextScales(string language)
    {
        using var context = CreateContext();
        PrepareGoldenWindow(context);
        context.Window.Width = 1024;
        context.Window.Height = 640;
        OpenSettings(context);
        var settings = context.ViewModel.Settings;
        settings.Editor.Current.RememberWindowPositionAndSize = !settings.Editor.Current.RememberWindowPositionAndSize;
        context.ViewModel.Shell.ApplyLanguage(language, hasSnapshot: false);
        var status = context.Window.GetVisualDescendants().OfType<TextBlock>()
            .Single(control => control.Name == "SettingsSaveStatus");
        var save = context.Window.GetVisualDescendants().OfType<Button>()
            .Single(control => ReferenceEquals(control.Command, settings.SaveSettingsCommand));
        foreach (var scale in new[] { 1.0, 1.5, 2.0 })
        {
            using var typography = new TypographyScaleSnapshot(context.Window, scale);
            foreach (var category in settings.Options.SettingsCategories)
            {
                settings.SelectedCategory = category.Code;
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(context.ViewModel.Shell.I18n[LocalizationKeys.UnsavedChanges], status.Text);
                Assert.True(status.IsEffectivelyVisible);
                AssertControlInsideWindow(status, context.Window);
                AssertControlInsideWindow(save, context.Window);
                var statusOrigin = status.TranslatePoint(default, context.Window)!.Value;
                var saveOrigin = save.TranslatePoint(default, context.Window)!.Value;
                Assert.True(statusOrigin.X + status.Bounds.Width <= saveOrigin.X);
            }
            settings.IsSaving = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(context.ViewModel.Shell.I18n[LocalizationKeys.SettingsSaving], status.Text);
            AssertControlInsideWindow(status, context.Window);
            AssertControlInsideWindow(save, context.Window);
            settings.IsSaving = false;
        }
        await settings.DiscardChangesAsync();
        Dispatcher.UIThread.RunJobs();
        Assert.False(status.IsEffectivelyVisible);
        Assert.False(save.IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public async Task SettingsFeedback_WhenFollowThroughStillRunsAfterPersist_RestoresFooterAndNavigation()
    {
        using var context = CreateContext();
        PrepareGoldenWindow(context);
        OpenSettings(context);
        var settings = context.ViewModel.Settings;
        var status = context.Window.GetVisualDescendants().OfType<TextBlock>()
            .Single(control => control.Name == "SettingsSaveStatus");
        var save = context.Window.GetVisualDescendants().OfType<Button>()
            .Single(control => ReferenceEquals(control.Command, settings.SaveSettingsCommand));
        var navigation = GetSettingsNavigation(context.Window);
        var original = settings.SavedSettingsFollowThrough;
        var completionStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completionRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        settings.SavedSettingsFollowThrough = new GatedFollowThrough(completionStarted, completionRelease);
        try
        {
            settings.Editor.Current.RememberWindowPositionAndSize =
                !settings.Editor.Current.RememberWindowPositionAndSize;
            Dispatcher.UIThread.RunJobs();
            Assert.True(settings.IsSettingsDirty);

            var saving = settings.SaveSettingsCommand.ExecuteAsync(null);
            await completionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Dispatcher.UIThread.RunJobs();

            // 落盘已完成、跟随刷新仍在进行：保存态以落盘为界，页脚与页内交互必须恢复，
            // 否则整段壳刷新会把「正在保存」和禁用态拖到数十秒之后（AUD-SET-003）。
            Assert.False(settings.IsSaving);
            Assert.False(saving.IsCompleted);
            Assert.False(status.IsEffectivelyVisible);
            Assert.True(navigation.IsEffectivelyEnabled);
            Assert.Equal(context.ViewModel.Shell.I18n[LocalizationKeys.Save], AutomationProperties.GetName(save));

            completionRelease.TrySetResult();
            await saving.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            completionRelease.TrySetResult();
            settings.SavedSettingsFollowThrough = original;
        }
    }

    private sealed class GatedFollowThrough(
        TaskCompletionSource started,
        TaskCompletionSource release) : ISavedSettingsFollowThrough
    {
        public SavedSettingsHandoff Apply(LauncherSettings savedSettings) => new(savedSettings, null);

        public async Task CompleteAsync(SavedSettingsHandoff handoff)
        {
            started.TrySetResult();
            await release.Task;
        }
    }
}
