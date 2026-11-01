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
using Cafe.Launcher.Core.Services;
using Cafe.Launcher.Testing;
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
    public void AppearanceBackground_OffersSingleFilePickerAlongsideFolderAndClear()
    {
        using var context = CreateContext();
        OpenSettings(context);
        context.ViewModel.Settings.SelectedCategory = SettingsCategoryCodes.Appearance;
        context.ViewModel.Settings.Editor.Current.BackgroundSource = BackgroundSources.Custom;
        context.ViewModel.Settings.Editor.Current.CustomBackgroundPath = "background.png";
        Dispatcher.UIThread.RunJobs();
        var section = context.Window.GetVisualDescendants().OfType<SettingsAppearanceSection>().Single();
        var row = section.GetVisualDescendants().OfType<global::Cafe.Launcher.UI.Controls.SettingRow>()
            .Single(control => control.Title == context.ViewModel.Shell.I18n[LocalizationKeys.CustomBackground]);
        var buttons = row.GetVisualDescendants().OfType<Button>().ToArray();

        Assert.Equal(3, buttons.Length);
        Assert.All(buttons, button => Assert.True(button.IsEffectivelyVisible));
        Assert.Same(context.ViewModel.Settings.ChooseBackgroundFileCommand, buttons[0].Command);
        Assert.Equal(context.ViewModel.Shell.I18n[LocalizationKeys.ChooseImageOrVideo], AutomationProperties.GetName(buttons[0]));
        Assert.Same(context.ViewModel.Settings.ChooseBackgroundFolderCommand, buttons[1].Command);
        Assert.Same(context.ViewModel.Settings.ClearBackgroundCommand, buttons[2].Command);
    }

    [AvaloniaTheory]
    [InlineData("png", false)]
    [InlineData("MP4", true)]
    [InlineData("mkv", true)]
    [InlineData("webm", true)]
    public async Task AppearanceBackground_WhenFolderSelected_ShowsSelectedFileLocation(string extension, bool isVideo)
    {
        using var context = CreateContext();
        var folder = context.Directory.Sub("wallpapers");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"selected-wallpaper.{extension}");
        if (isVideo)
            await File.WriteAllBytesAsync(path, []);
        else
            HeadlessTestHost.WriteSolidPng(path, Brushes.DarkSlateBlue, 160, 90);
        OpenSettings(context);
        var settings = context.ViewModel.Settings;
        settings.SelectedCategory = SettingsCategoryCodes.Appearance;
        settings.Editor.Current.BackgroundSource = BackgroundSources.Custom;
        settings.Editor.Current.CustomBackgroundPath = folder;
        await WaitForBackgroundSelectionAsync(context, path);
        Dispatcher.UIThread.RunJobs();
        var section = context.Window.GetVisualDescendants().OfType<SettingsAppearanceSection>().Single();
        var media = section.GetVisualDescendants().OfType<Grid>()
            .Single(control => control.Classes.Contains("settings-appearance-media"));
        var location = media.GetVisualDescendants().OfType<TextBlock>()
            .Single(control => control.Classes.Contains("caption") && control.IsEffectivelyVisible);

        Assert.Equal(path, location.Text);
        Assert.Equal(path, ToolTip.GetTip(location));
        Assert.True(location.IsEffectivelyVisible);
        Assert.Equal(folder, settings.Editor.Current.CustomBackgroundPath);
        var volume = section.GetVisualDescendants().OfType<global::Cafe.Launcher.UI.Controls.SettingRow>()
            .Single(control => control.Title == context.ViewModel.Shell.I18n[LocalizationKeys.VideoBackgroundVolume]);
        Assert.Equal(isVideo, volume.IsEffectivelyVisible);

        var background = context.ViewModel.Background.BackgroundImageSource;
        // Leave one different candidate so an accidental reroll cannot pass by choosing the same file.
        File.Move(path, Path.Combine(folder, $"replacement.{extension}"));
        settings.Editor.Current.BackgroundVideoVolume = 35;
        await settings.PendingAppearancePreview.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(path, location.Text);
        Assert.Same(background, context.ViewModel.Background.BackgroundImageSource);
        Assert.Equal(35, context.ViewModel.Background.BackgroundVideoVolume);
    }

    [AvaloniaTheory]
    [InlineData(BackgroundSources.Bundled)]
    [InlineData(BackgroundSources.Remote)]
    public async Task AppearanceBackground_WhenNonCustomSourceSelected_ShowsLocalizedName(string source)
    {
        byte[] imageBytes = [];
        using var context = CreateContext(configure: services =>
            services.AddSingleton<IRemoteHttpTransport>(new StubRemoteHttpTransport(_ => imageBytes)));
        var path = context.Directory.Sub("remote-background.png");
        HeadlessTestHost.WriteSolidPng(path, Brushes.DarkSlateBlue, 160, 90);
        imageBytes = await File.ReadAllBytesAsync(path);
        const string RemoteUrl = "https://images.example.invalid/background.png";
        var snapshot = new LauncherStatusSnapshot
        {
            Remote = new LauncherRemoteState
            {
                BaseConfig = new BaseConfigResponse
                {
                    LauncherBackgroundImg = RemoteUrl,
                    LauncherBackgroundImgCrc64 = await new Crc64Service().ComputeFileAsync(path)
                }
            }
        };
        OpenSettings(context);
        context.ViewModel.Settings.SelectedCategory = SettingsCategoryCodes.Appearance;
        context.ViewModel.Settings.Editor.Current.BackgroundSource = source;
        await context.ViewModel.Settings.PendingAppearancePreview.WaitAsync(TimeSpan.FromSeconds(5));
        await context.ViewModel.Background.UpdateBackgroundImageAsync(
            context.ViewModel.Settings.Editor.Current, snapshot, CancellationToken.None);
        Dispatcher.UIThread.RunJobs();
        var section = context.Window.GetVisualDescendants().OfType<SettingsAppearanceSection>().Single();
        var media = section.GetVisualDescendants().OfType<Grid>()
            .Single(control => control.Classes.Contains("settings-appearance-media"));
        var expected = source == BackgroundSources.Remote
            ? context.ViewModel.Shell.I18n[LocalizationKeys.BackgroundSourceRemote]
            : context.ViewModel.Shell.I18n[LocalizationKeys.BackgroundSourceBundled];

        Assert.False(context.ViewModel.Background.HasBackgroundLoadFailure);
        Assert.Contains(media.GetVisualDescendants().OfType<TextBlock>(), control =>
            control.IsEffectivelyVisible && control.Text == expected);
        Assert.Same(context.ViewModel.Background.BackgroundPreviewImage,
            media.GetVisualDescendants().OfType<Image>().Single().Source);

        if (source == BackgroundSources.Remote)
        {
            var location = media.GetVisualDescendants().OfType<TextBlock>()
                .Single(control => control.IsEffectivelyVisible && control.Classes.Contains("caption"));
            Assert.Equal(RemoteUrl, ToolTip.GetTip(location));
            context.ViewModel.Shell.ApplyLanguage(LauncherLanguages.SimplifiedChinese, hasSnapshot: false);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(context.ViewModel.Shell.I18n[LocalizationKeys.BackgroundSourceRemote], location.Text);
            await context.ViewModel.Background.UpdateBackgroundImageAsync(
                context.ViewModel.Settings.Editor.Current, null, CancellationToken.None);
            Dispatcher.UIThread.RunJobs();
            Assert.True(context.ViewModel.Background.HasBackgroundLoadFailure);
            Assert.DoesNotContain(media.GetVisualDescendants().OfType<TextBlock>(), control =>
                control.IsEffectivelyVisible && control.Text == context.ViewModel.Shell.I18n[LocalizationKeys.BackgroundSourceRemote]);
        }

        await context.ViewModel.Background.UpdateBackgroundImageAsync(
            new LauncherSettings { BackgroundSource = BackgroundSources.Bundled }, null, CancellationToken.None);
        context.ViewModel.Shell.ApplyLanguage(LauncherLanguages.SimplifiedChinese, hasSnapshot: false);
        Dispatcher.UIThread.RunJobs();
        var bundled = media.GetVisualDescendants().OfType<TextBlock>()
            .Single(control => control.IsEffectivelyVisible && control.Classes.Contains("caption"));
        Assert.Equal(context.ViewModel.Shell.I18n[LocalizationKeys.BackgroundSourceBundled], bundled.Text);
        Assert.Null(context.ViewModel.Background.CurrentBackgroundFilePath);
        Assert.False(context.ViewModel.Background.HasBackgroundLoadFailure);
    }

    [AvaloniaFact]
    public async Task AppearanceBackground_WhenFolderVideoFrameCaptured_UsesItOnlyInThumbnail()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Uses the bundled Windows decoder.");
        using var context = CreateContext();
        var folder = context.Directory.Sub("video-thumbnails");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "first-frame.mp4");
        HeadlessTestHost.WriteTwoFrameVideo(path);
        OpenSettings(context);
        var settings = context.ViewModel.Settings;
        settings.SelectedCategory = SettingsCategoryCodes.Appearance;
        settings.Editor.Current.BackgroundSource = BackgroundSources.Custom;
        settings.Editor.Current.CustomBackgroundPath = folder;
        await WaitForBackgroundSelectionAsync(context, path);
        await context.ViewModel.Background.PendingVideoThumbnail.WaitAsync(TimeSpan.FromSeconds(5));
        Dispatcher.UIThread.RunJobs();
        var section = context.Window.GetVisualDescendants().OfType<SettingsAppearanceSection>().Single();
        var thumbnail = section.GetVisualDescendants().OfType<Border>()
            .Single(control => control.Classes.Contains("settings-appearance-thumbnail"));
        var image = thumbnail.GetVisualDescendants().OfType<Image>().Single();

        Assert.IsType<Avalonia.Media.Imaging.Bitmap>(image.Source);
        Assert.Same(context.ViewModel.Background.BackgroundPreviewImage, image.Source);
        Assert.NotSame(context.ViewModel.Background.BackgroundImageSource, image.Source);
        Assert.Equal(path, context.ViewModel.Background.BackgroundVideoPath);
        Assert.Equal(folder, settings.Editor.Current.CustomBackgroundPath);
    }

    [AvaloniaFact]
    public async Task AppearanceBackground_WhenVideoSelected_BlursThumbnailAndShowsVideoIcon()
    {
        using var context = CreateContext();
        OpenSettings(context);
        var settings = context.ViewModel.Settings;
        settings.SelectedCategory = SettingsCategoryCodes.Appearance;
        settings.Editor.Current.BackgroundSource = BackgroundSources.Custom;
        settings.Editor.Current.CustomBackgroundPath = "missing-thumbnail.mp4";
        await WaitForBackgroundSelectionAsync(context, settings.Editor.Current.CustomBackgroundPath);
        var section = context.Window.GetVisualDescendants().OfType<SettingsAppearanceSection>().Single();
        var thumbnail = section.GetVisualDescendants().OfType<Border>()
            .Single(control => control.Classes.Contains("settings-appearance-thumbnail"));
        var image = thumbnail.GetVisualDescendants().OfType<Image>().Single();

        Assert.IsType<BlurEffect>(image.Effect);
        var icon = Assert.Single(thumbnail.GetVisualDescendants().OfType<Material.Icons.Avalonia.MaterialIcon>());
        Assert.True(icon.IsEffectivelyVisible);

        settings.ClearBackgroundCommand.Execute(null);
        await WaitForBackgroundSelectionAsync(context, null);
        Assert.Null(image.Effect);
        Assert.False(icon.IsEffectivelyVisible);
        Assert.Same(context.ViewModel.Background.BackgroundImageSource, image.Source);
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
        var selectFile = section.GetVisualDescendants().OfType<Button>().Single(control =>
            ReferenceEquals(control.Command, context.ViewModel.Settings.ChooseBackgroundFileCommand));
        Assert.True(selectFile.IsEffectivelyVisible);
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
            await WaitForBackgroundSelectionAsync(context, draft.CustomBackgroundPath);
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

    private static Task WaitForBackgroundSelectionAsync(TestContext context, string? path) =>
        HeadlessTestHost.WaitUntilAsync(
            () => context.ViewModel.Background.CurrentBackgroundFilePath == path
                && context.ViewModel.Settings.PendingAppearancePreview.IsCompleted,
            TimeSpan.FromSeconds(5), "The latest background selection did not settle.");
}
