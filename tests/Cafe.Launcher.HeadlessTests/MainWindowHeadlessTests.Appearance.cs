using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.UI.Constants;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.Views;
using Cafe.Launcher.UI.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Cafe.Launcher.HeadlessTests;

public sealed partial class MainWindowHeadlessTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void AppearanceExpanders_PointerStates_UseSharedBrushesOnTheRenderedSurface(bool dark)
    {
        using var variant = ThemeVariantSnapshot.Capture(dark ? ThemeVariant.Dark : ThemeVariant.Light);
        using var context = CreateContext();
        OpenSettings(context);
        context.ViewModel.Settings.SelectedCategory = SettingsCategoryCodes.Appearance;
        context.ViewModel.Settings.Editor.Current.ThemeMode = dark ? ThemeModes.Dark : ThemeModes.Light;
        Dispatcher.UIThread.RunJobs();
        var section = context.Window.GetVisualDescendants().OfType<SettingsAppearanceSection>().Single();
        foreach (var expander in section.GetVisualDescendants().OfType<Expander>())
        {
            expander.IsExpanded = true;
            expander.BringIntoView();
            Dispatcher.UIThread.RunJobs();
            var header = expander.GetVisualDescendants().OfType<ToggleButton>().First();
            var surface = header.GetVisualDescendants().OfType<Border>().First();
            var bounds = header.Bounds;
            var point = header.TranslatePoint(new Point(header.Bounds.Width / 2, header.Bounds.Height / 2), context.Window)!.Value;
            context.Window.MouseMove(point);
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(":pointerover", header.Classes);
            Assert.Equal(context.Window.FindResource(context.Window.ActualThemeVariant, "Launcher.Color.Button.Secondary.Hover"), surface.Background);
            Assert.Equal((byte)0, Assert.IsAssignableFrom<ISolidColorBrush>(surface.BorderBrush).Color.A);
            Assert.All(header.GetVisualDescendants().OfType<Border>().Where(border => !ReferenceEquals(border, surface)),
                border => Assert.True(border.Background is null or ISolidColorBrush { Color.A: 0 }));
            context.Window.MouseDown(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(context.Window.FindResource(context.Window.ActualThemeVariant, "Launcher.Color.Button.Secondary.Pressed"), surface.Background);
            Assert.Equal(bounds, header.Bounds);
            context.Window.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.False(expander.IsExpanded);
            Assert.Equal(bounds, header.Bounds);
            header.IsEnabled = false;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal((byte)0, Assert.IsAssignableFrom<ISolidColorBrush>(surface.Background).Color.A);
            Assert.Equal(bounds, header.Bounds);
            Assert.All(header.GetVisualDescendants().OfType<Border>().Where(border => !ReferenceEquals(border, surface)),
                border => Assert.True(border.Background is null or ISolidColorBrush { Color.A: 0 }));
        }
    }

    [AvaloniaFact]
    public void AppearanceSections_WhenExpandedAndNavigated_PreserveDraftAndResetOnReopen()
    {
        using var context = CreateContext();
        OpenSettings(context);
        var settings = context.ViewModel.Settings;
        settings.SelectedCategory = SettingsCategoryCodes.Appearance;
        Dispatcher.UIThread.RunJobs();
        var section = context.Window.GetVisualDescendants().OfType<SettingsAppearanceSection>().Single();
        Assert.Single(section.GetVisualDescendants().OfType<Expander>());
        var advanced = section.FindControl<Expander>("AdvancedColorsExpander")!;
        Assert.False(advanced.IsExpanded);
        Assert.False(settings.IsSettingsDirty);

        advanced.IsExpanded = true;
        settings.SelectedCategory = SettingsCategoryCodes.General;
        settings.SelectedCategory = SettingsCategoryCodes.Appearance;
        Dispatcher.UIThread.RunJobs();
        Assert.True(advanced.IsExpanded);
        Assert.False(settings.IsSettingsDirty);
        Assert.False(settings.CanSaveSettings);
        Assert.All(section.GetVisualDescendants().OfType<global::Cafe.Launcher.UI.Controls.SettingRow>()
            .Where(row => row.IsEffectivelyVisible), row =>
                Assert.Equal(FontWeight.Normal, row.FindControl<TextBlock>("RowTitle")!.FontWeight));

        context.ViewModel.WindowChrome.ShowSettingsCommand.Execute(null);
        context.ViewModel.WindowChrome.ShowSettingsCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(advanced.IsExpanded);
        Assert.False(settings.IsSettingsDirty);
    }

    [AvaloniaFact]
    public void AppearanceAdvanced_WhenCollapsed_PreservesValueAndReturnsKeyboardFocus()
    {
        using var context = CreateContext();
        OpenSettings(context);
        var settings = context.ViewModel.Settings;
        settings.SelectedCategory = SettingsCategoryCodes.Appearance;
        settings.Appearance.IsAdvancedColorExpanded = true;
        Dispatcher.UIThread.RunJobs();
        var section = context.Window.GetVisualDescendants().OfType<SettingsAppearanceSection>().Single();
        var advanced = section.FindControl<Expander>("AdvancedColorsExpander")!;
        var option = advanced.GetVisualDescendants().OfType<ComboBox>().First(control => control.IsEffectivelyVisible);
        option.Focus(NavigationMethod.Tab);
        settings.Editor.Current.NeutralColorStrategy = NeutralColorStrategies.SeedFollowing;
        advanced.IsExpanded = false;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(NeutralColorStrategies.SeedFollowing, settings.Editor.Current.NeutralColorStrategy);
        Assert.True(settings.IsSettingsDirty);
        var header = advanced.GetVisualDescendants().OfType<ToggleButton>().First();
        Assert.True(header.IsFocused);
        Assert.Equal(context.ViewModel.Shell.I18n[LocalizationKeys.SettingsAdvancedColors], AutomationProperties.GetName(header));
        Assert.Equal(new Thickness(2), header.BorderThickness);
        Assert.Equal(context.Window.FindResource("Launcher.Color.FocusRing"), header.BorderBrush);
        Assert.Contains(header.GetVisualDescendants().OfType<Border>(), border =>
            Equals(header.BorderBrush, border.BorderBrush) && border.BorderThickness == header.BorderThickness);
        advanced.IsExpanded = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(NeutralColorStrategies.SeedFollowing, settings.Editor.Current.NeutralColorStrategy);
    }

    [AvaloniaFact]
    public async Task AppearanceBackground_WhenFileFails_ShowsRecoveryOutsideCollapsedDetails()
    {
        using var context = CreateContext();
        OpenSettings(context);
        context.ViewModel.Settings.SelectedCategory = SettingsCategoryCodes.Appearance;
        Dispatcher.UIThread.RunJobs();
        context.ViewModel.Settings.Editor.Current.BackgroundSource = BackgroundSources.Custom;
        context.ViewModel.Settings.Editor.Current.CustomBackgroundPath = "missing-appearance-background.png";
        await context.ViewModel.Settings.PendingAppearancePreview.WaitAsync(TimeSpan.FromSeconds(5));
        Dispatcher.UIThread.RunJobs();
        Assert.True(context.ViewModel.Background.HasBackgroundLoadFailure);
        var section = context.Window.GetVisualDescendants().OfType<SettingsAppearanceSection>().Single();
        var warning = section.GetVisualDescendants().OfType<Grid>()
            .Single(control => control.Classes.Contains("settings-appearance-warning"));
        Assert.True(warning.IsEffectivelyVisible);
        var selectImage = section.GetVisualDescendants().OfType<Button>().Single(control =>
            ReferenceEquals(control.Command, context.ViewModel.Settings.ChooseBackgroundImageCommand));
        Assert.True(selectImage.IsEffectivelyVisible);
        context.ViewModel.Settings.Editor.Current.BackgroundSource = BackgroundSources.Bundled;
        await context.ViewModel.Settings.PendingAppearancePreview.WaitAsync(TimeSpan.FromSeconds(5));
        Dispatcher.UIThread.RunJobs();
        Assert.False(warning.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void Golden_SettingsAppearance_MatchesBaseline()
    {
        using var context = CreateContext();
        PrepareGoldenWindow(context);
        OpenSettings(context);
        try
        {
            context.ViewModel.Shell.ApplyLanguage(LauncherLanguages.SimplifiedChinese, hasSnapshot: false);
            context.ViewModel.Settings.SelectedCategory = SettingsCategoryCodes.Appearance;
            Dispatcher.UIThread.RunJobs();
            GoldenScreenshot.Compare(context.Window, "settings-appearance");
        }
        finally
        {
            context.ViewModel.Shell.ApplyLanguage(LauncherLanguages.English, hasSnapshot: false);
        }
    }

    [AvaloniaFact]
    public void Golden_SettingsAppearanceAdvancedDark_MatchesBaseline()
    {
        using var variant = ThemeVariantSnapshot.Capture(ThemeVariant.Dark);
        using var context = CreateContext();
        PrepareGoldenWindow(context, ThemeVariant.Dark);
        OpenSettings(context);
        context.ViewModel.Settings.SelectedCategory = SettingsCategoryCodes.Appearance;
        context.ViewModel.Settings.Editor.Current.ThemeMode = ThemeModes.Dark;
        context.Provider.GetRequiredService<ThemeApplier>().ApplyScheme(Color.Parse("#2E7DF6"), isDark: true);
        context.ViewModel.Settings.Appearance.IsAdvancedColorExpanded = true;
        Dispatcher.UIThread.RunJobs();
        var advanced = context.Window.GetVisualDescendants().OfType<SettingsAppearanceSection>().Single()
            .FindControl<Expander>("AdvancedColorsExpander")!;
        advanced.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        var header = advanced.GetVisualDescendants().OfType<ToggleButton>().First();
        var point = header.TranslatePoint(new Point(header.Bounds.Width / 2, header.Bounds.Height / 2), context.Window)!.Value;
        context.Window.MouseMove(point);
        Dispatcher.UIThread.RunJobs();
        GoldenScreenshot.Compare(context.Window, "settings-appearance-advanced-dark");
    }
    [AvaloniaFact]
    public async Task Golden_SettingsVideoAudio_MatchesBaseline()
    {
        using var context = CreateContext();
        PrepareGoldenWindow(context);
        OpenSettings(context);
        try
        {
            context.ViewModel.Shell.ApplyLanguage(LauncherLanguages.SimplifiedChinese, hasSnapshot: false);
            context.ViewModel.Settings.SelectedCategory = SettingsCategoryCodes.Appearance;
            Dispatcher.UIThread.RunJobs();
            var draft = context.ViewModel.Settings.Editor.Current;
            draft.BackgroundSource = BackgroundSources.Custom;
            draft.CustomBackgroundPath = "background.mp4";
            draft.BackgroundVideoVolume = 35;
            draft.BackgroundVideoMuted = true;
            await context.ViewModel.Settings.PendingAppearancePreview.WaitAsync(TimeSpan.FromSeconds(5));
            Dispatcher.UIThread.RunJobs();
            var slider = context.Window.GetVisualDescendants().OfType<Slider>()
                .Single(control => AutomationProperties.GetName(control) == context.ViewModel.Shell.I18n[LocalizationKeys.VideoBackgroundVolume]);
            slider.GetVisualAncestors().OfType<global::Cafe.Launcher.UI.Controls.SettingRow>().First().BringIntoView();
            Dispatcher.UIThread.RunJobs();
            GoldenScreenshot.Compare(context.Window, "settings-video-audio");
        }
        finally
        {
            context.ViewModel.Shell.ApplyLanguage(LauncherLanguages.English, hasSnapshot: false);
        }
    }

}
