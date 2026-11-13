using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using System.Runtime.InteropServices;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cafe.Launcher.UI.Constants;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.Views;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Services.Diagnostics;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Cafe.Launcher.HeadlessTests;

public sealed partial class MainWindowHeadlessTests
{
    [AvaloniaTheory]
    [InlineData(LauncherLanguages.English)]
    [InlineData(LauncherLanguages.SimplifiedChinese)]
    [InlineData(LauncherLanguages.TraditionalChinese)]
    [InlineData(LauncherLanguages.Japanese)]
    public async Task VideoBackgroundSettings_WhenDraftChanges_UpdatesVolumeAndVisibility(string language)
    {
        using var context = CreateContext();
        context.Window.Width = 1024;
        context.Window.Height = 640;
        OpenSettings(context);
        context.ViewModel.Shell.ApplyLanguage(language, hasSnapshot: false);
        context.ViewModel.Settings.SelectedCategory = SettingsCategoryCodes.Appearance;
        var settings = context.ViewModel.Settings.Editor.Current;
        // A missing media file keeps this UI test independent of a GPU and decoder.
        settings.CustomBackgroundPath = "missing-background.MP4";
        settings.BackgroundSource = BackgroundSources.Custom;
        await WaitForBackgroundSelectionAsync(context, settings.CustomBackgroundPath);
        Dispatcher.UIThread.RunJobs();
        var name = context.ViewModel.Shell.I18n[LocalizationKeys.VideoBackgroundVolume];
        var slider = context.Window.GetVisualDescendants().OfType<Slider>()
            .Single(control => AutomationProperties.GetName(control) == name);
        Assert.True(slider.IsEffectivelyVisible);
        Assert.Equal(0, slider.Value);
        var mute = context.Window.GetVisualDescendants().OfType<CheckBox>()
            .Single(control => AutomationProperties.GetName(control) == context.ViewModel.Shell.I18n[LocalizationKeys.VideoBackgroundMute]);
        Assert.True(mute.IsEffectivelyVisible);
        Assert.False(mute.IsChecked);
        using var typography = new TypographyScaleSnapshot(context.Window, 1.5);
        Dispatcher.UIThread.RunJobs();
        var row = slider.GetVisualAncestors().OfType<global::Cafe.Launcher.UI.Controls.SettingRow>().First();
        var action = row.FindControl<ContentPresenter>("ActionPresenter")!;
        Assert.True(action.Bounds.Right <= row.Bounds.Width + 1);
        foreach (var control in new Control[] { slider, mute })
        {
            var point = control.TranslatePoint(default, row)!.Value;
            Assert.True(point.X >= -1 && point.X + control.Bounds.Width <= row.Bounds.Width + 1);
        }

        var percentage = row.GetVisualDescendants().OfType<TextBlock>()
            .Single(control => control.Text == "0%");
        var percentageWidth = percentage.Bounds.Width;
        var mutePosition = mute.TranslatePoint(default, row)!.Value;
        var sliderPosition = slider.TranslatePoint(default, row)!.Value;
        foreach (var volume in new[] { 35, 100, 0 })
        {
            slider.Value = volume;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(percentageWidth, percentage.Bounds.Width);
            Assert.Equal(mutePosition, mute.TranslatePoint(default, row)!.Value);
            Assert.Equal(sliderPosition, slider.TranslatePoint(default, row)!.Value);
            Assert.True(percentage.TextLayout.Width <= percentage.Bounds.Width + 1);
        }

        slider.Value = 35;
        await context.ViewModel.Settings.PendingAppearancePreview.WaitAsync(TimeSpan.FromSeconds(5));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(35, settings.BackgroundVideoVolume);
        Assert.Equal(35, context.ViewModel.Background.BackgroundVideoVolume);

        mute.IsChecked = true;
        await context.ViewModel.Settings.PendingAppearancePreview.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(settings.BackgroundVideoMuted);
        Assert.Equal(35, settings.BackgroundVideoVolume);
        Assert.Equal(0, context.ViewModel.Background.BackgroundVideoVolume);
        slider.Value = 60;
        await context.ViewModel.Settings.PendingAppearancePreview.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(60, settings.BackgroundVideoVolume);
        Assert.Equal(0, context.ViewModel.Background.BackgroundVideoVolume);
        mute.IsChecked = false;
        await context.ViewModel.Settings.PendingAppearancePreview.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(60, context.ViewModel.Background.BackgroundVideoVolume);

        settings.BackgroundSource = BackgroundSources.Bundled;
        await WaitForBackgroundSelectionAsync(context, null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(slider.IsEffectivelyVisible);
        Assert.False(mute.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void Settings_WhenLanguageChanges_RefreshesVisibleCategoryTitle()
    {
        using var context = CreateContext();
        OpenSettings(context);

        var categoryTitle = context.Window.GetVisualDescendants().OfType<TextBlock>()
            .Single(control => control.Classes.Contains("category-title"));
        var description = context.Window.GetVisualDescendants().OfType<TextBlock>()
            .Single(control => control.Name == "SettingsCategoryDescription");
        var english = categoryTitle.Text;
        var englishDescription = description.Text;

        context.ViewModel.Shell.ApplyLanguage(
            LauncherLanguages.SimplifiedChinese,
            hasSnapshot: false);
        Dispatcher.UIThread.RunJobs();

        Assert.NotEqual(english, categoryTitle.Text);
        Assert.Equal(
            context.ViewModel.Settings.Options.SettingsCategories.First(option =>
                option.Code == context.ViewModel.Settings.SelectedCategory).DisplayName,
            categoryTitle.Text);
        Assert.NotEqual(englishDescription, description.Text);
        foreach (var option in context.ViewModel.Settings.Options.SettingsCategories)
        {
            context.ViewModel.Settings.SelectedCategory = option.Code;
            Dispatcher.UIThread.RunJobs();
            Assert.False(string.IsNullOrWhiteSpace(description.Text));
            Assert.Equal(option.Description, description.Text);
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SettingsAbout_IdentityActions_ShareGeometryAndPreservePrimaryEmphasis(bool dark)
    {
        using var theme = ThemeVariantSnapshot.Capture(dark ? ThemeVariant.Dark : ThemeVariant.Light);
        using var context = CreateContext();
        PrepareGoldenWindow(context, dark ? ThemeVariant.Dark : ThemeVariant.Light);
        OpenSettings(context);
        context.ViewModel.Settings.SelectedCategory = SettingsCategoryCodes.About;
        context.ViewModel.Settings.Editor.Current.ThemeMode = dark ? ThemeModes.Dark : ThemeModes.Light;
        await context.ViewModel.Settings.PendingAppearancePreview.WaitAsync(TimeSpan.FromSeconds(5));
        context.Provider.GetRequiredService<ThemeApplier>().ApplyScheme(Color.Parse("#2E7DF6"), isDark: dark);
        Dispatcher.UIThread.RunJobs();
        var update = context.Window.GetVisualDescendants().OfType<Button>()
            .Single(button => ReferenceEquals(button.Command, context.ViewModel.Settings.CheckForUpdatesCommand));
        var row = Assert.IsType<WrapPanel>(update.Parent);
        var buttons = row.Children.OfType<Button>().ToArray();
        Assert.Equal(3, buttons.Length);
        foreach (var button in buttons)
        {
            Assert.Equal(40, button.Bounds.Height);
            Assert.Equal(new CornerRadius(8), button.CornerRadius);
            Assert.Equal(buttons[1].Padding, button.Padding);
        }

        Assert.Equal(context.Window.FindResource(context.Window.ActualThemeVariant, "Launcher.Color.Primary"), update.Background);
        Assert.Equal(context.Window.FindResource(context.Window.ActualThemeVariant, "Launcher.Color.OnPrimary"), update.Foreground);
        Assert.Equal(context.Window.FindResource(context.Window.ActualThemeVariant, "Launcher.Color.Button.Secondary.Background"), buttons[1].Background);
        var bounds = buttons.Select(button => button.Bounds).ToArray();
        var surface = Assert.Single(update.GetVisualChildren().OfType<Border>());
        var point = update.TranslatePoint(new Point(10, 10), context.Window)!.Value;
        context.Window.MouseMove(point);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(context.Window.FindResource(context.Window.ActualThemeVariant, "Launcher.Color.Primary.Hover"), surface.Background);
        context.Window.MouseDown(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(context.Window.FindResource(context.Window.ActualThemeVariant, "Launcher.Color.Primary.Pressed"), surface.Background);
        // 在按钮外释放指针，不触发真实网络检查。
        context.Window.MouseMove(new Point(20, 20));
        context.Window.MouseUp(new Point(20, 20), MouseButton.Left);
        buttons[1].Focus(NavigationMethod.Unspecified);
        update.Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(context.Window.FindResource(context.Window.ActualThemeVariant, "Launcher.Color.OnPrimary"), surface.BorderBrush);
        Assert.Equal(bounds, buttons.Select(button => button.Bounds).ToArray());
        update.IsEnabled = false;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(context.Window.FindResource(context.Window.ActualThemeVariant, "Launcher.Color.Content.Row"), surface.Background);
        Assert.Equal(bounds, buttons.Select(button => button.Bounds).ToArray());
    }

    [AvaloniaTheory]
    [InlineData(1300, false)]
    [InlineData(1300, true)]
    [InlineData(1301, false)]
    [InlineData(1301, true)]
    [InlineData(1024, false)]
    [InlineData(1024, true)]
    public async Task SettingsAbout_IdentityActions_AlignPaintedEdgesToDevicePixels(double width, bool dark)
    {
        var variant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        using var theme = ThemeVariantSnapshot.Capture(variant);
        using var context = CreateContext();
        PrepareGoldenWindow(context, variant);
        context.Window.Width = width;
        OpenSettings(context);
        context.ViewModel.Settings.SelectedCategory = SettingsCategoryCodes.About;
        context.ViewModel.Settings.Editor.Current.ThemeMode = dark ? ThemeModes.Dark : ThemeModes.Light;
        await context.ViewModel.Settings.PendingAppearancePreview.WaitAsync(TimeSpan.FromSeconds(5));
        context.Provider.GetRequiredService<ThemeApplier>().ApplyScheme(Color.Parse("#2E7DF6"), isDark: dark);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(variant, context.Window.ActualThemeVariant);
        var update = context.Window.GetVisualDescendants().OfType<Button>()
            .Single(button => ReferenceEquals(button.Command, context.ViewModel.Settings.CheckForUpdatesCommand));
        var row = Assert.IsType<WrapPanel>(update.Parent);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)context.Window.Bounds.Width, (int)context.Window.Bounds.Height));
        bitmap.Render(context.Window);
        foreach (var button in row.Children.OfType<Button>())
        {
            var border = Assert.Single(button.GetVisualChildren().OfType<Border>());
            var origin = border.TranslatePoint(default, context.Window)!.Value;
            foreach (var edge in new[] { origin.X, origin.Y, origin.X + border.Bounds.Width, origin.Y + border.Bounds.Height })
            {
                var physicalEdge = edge * context.Window.RenderScaling;
                Assert.Equal(Math.Round(physicalEdge), physicalEdge, precision: 6);
            }

            // 取圆角之外的左侧直边；外侧卡片与内侧填充之间不能出现半覆盖的混色像素。
            var strip = new byte[8 * 4];
            var handle = GCHandle.Alloc(strip, GCHandleType.Pinned);
            try
            {
                bitmap.CopyPixels(new PixelRect((int)origin.X - 1, (int)(origin.Y + border.Bounds.Height / 2), 8, 1),
                    handle.AddrOfPinnedObject(), strip.Length, strip.Length);
            }
            finally
            {
                handle.Free();
            }

            var outside = strip.AsSpan(0, 4).ToArray();
            var inside = strip.AsSpan(strip.Length - 4, 4).ToArray();
            Assert.False(outside.AsSpan().SequenceEqual(inside));
            for (var offset = 0; offset < strip.Length; offset += 4)
            {
                var pixel = strip.AsSpan(offset, 4);
                Assert.True(pixel.SequenceEqual(outside) || pixel.SequenceEqual(inside), "Straight button edges must not have partially covered pixels.");
            }
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void SettingsAbout_WhenLanguageChanges_UpdatesCreditRowsAndKeepsLinksClickable(bool dark)
    {
        var variant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        using var theme = ThemeVariantSnapshot.Capture(variant);
        var openedUrls = new List<string?>();
        using var context = CreateContext(configure: services =>
            services.AddSingleton(new PresentationEnvironment { OpenExternalUrl = openedUrls.Add }));
        context.ViewModel.IsMotionReduced = true;
        context.Provider.GetRequiredService<ThemeApplier>().ApplyScheme(Color.Parse("#2E7DF6"), isDark: dark);
        OpenSettings(context);
        context.ViewModel.Settings.SelectedCategory = SettingsCategoryCodes.About;
        Dispatcher.UIThread.RunJobs();

        var section = context.Window.GetVisualDescendants().OfType<SettingsAboutSection>().Single();
        var initialRows = CreditRows();
        Assert.Equal(7, initialRows.Length);
        var englishDescriptions = initialRows.Select(AutomationProperties.GetHelpText).ToArray();
        Assert.All(englishDescriptions, description => Assert.False(string.IsNullOrWhiteSpace(description)));

        context.ViewModel.Shell.ApplyLanguage(LauncherLanguages.SimplifiedChinese, hasSnapshot: false);
        Dispatcher.UIThread.RunJobs();

        var rows = CreditRows();
        Assert.Equal(["gytxtx", "MF-Dust", "Avalonia", "CommunityToolkit.Mvvm", "Material.Icons.Avalonia",
                context.ViewModel.Shell.I18n[LocalizationKeys.AboutCreditBlueArchiveCafe],
                context.ViewModel.Shell.I18n[LocalizationKeys.AboutCreditAppIcon]],
            rows.Select(AutomationProperties.GetName));
        var expectedDescriptions = new[]
        {
            LocalizationKeys.AboutCreditGytxtx, LocalizationKeys.AboutCreditMfDust,
            LocalizationKeys.AboutCreditAvalonia, LocalizationKeys.AboutCreditCommunityToolkit,
            LocalizationKeys.AboutCreditMaterialIcons, LocalizationKeys.AboutCreditBlueArchiveCafeDescription,
            LocalizationKeys.AboutCreditAppIconDescription
        }.Select(key => context.ViewModel.Shell.I18n[key]).ToArray();
        Assert.Equal(expectedDescriptions, rows.Select(AutomationProperties.GetHelpText));
        Assert.DoesNotContain(englishDescriptions, description => expectedDescriptions.Contains(description));
        var avatarBrush = context.Window.FindResource(variant, "Launcher.Color.SecondaryContainer");
        Assert.All(rows, row =>
        {
            var avatar = Assert.Single(row.GetVisualDescendants().OfType<Border>(),
                border => border.Classes.Contains("about-credit-avatar"));
            Assert.Equal(avatarBrush, avatar.Background);
        });
        Assert.Equal(5, section.GetVisualDescendants().OfType<Border>()
            .Count(border => border.Classes.Contains("about-credit-divider") && border.IsVisible));

        var secondRow = rows[1];
        secondRow.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        var divider = Assert.Single(Assert.IsType<Grid>(secondRow.Parent).Children.OfType<Border>());
        var surface = Assert.Single(secondRow.GetVisualChildren().OfType<Border>());
        Assert.Equal(surface.TranslatePoint(default, context.Window), divider.TranslatePoint(default, context.Window));
        Assert.Equal(divider.Bounds.Width, surface.Bounds.Width);

        var hoverPoint = secondRow.TranslatePoint(secondRow.Bounds.Center - secondRow.Bounds.Position, context.Window)!.Value;
        context.Window.MouseMove(hoverPoint);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(context.Window.FindResource(variant, "Launcher.Color.Button.Flat.Hover"), surface.Background);
        Assert.Equal(BackgroundSizing.OuterBorderEdge, surface.BackgroundSizing);
        Assert.Equal(default, surface.CornerRadius);
        context.Window.MouseDown(hoverPoint, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(context.Window.FindResource(variant, "Launcher.Color.Button.Flat.Pressed"), surface.Background);
        context.Window.MouseMove(new Point(20, 20));
        context.Window.MouseUp(new Point(20, 20), MouseButton.Left);

        var firstRow = rows[0];
        firstRow.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        var borderThickness = firstRow.BorderThickness;
        Assert.True(firstRow.Focus(NavigationMethod.Tab));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(borderThickness, firstRow.BorderThickness);
        Assert.Equal(context.Window.FindResource(variant, "Launcher.Color.FocusRing"), firstRow.BorderBrush);

        foreach (var row in rows)
        {
            row.BringIntoView();
            Dispatcher.UIThread.RunJobs();
            var point = row.TranslatePoint(new Point(row.Bounds.Width * 0.75, row.Bounds.Height / 2), context.Window);
            Assert.NotNull(point);
            context.Window.MouseDown(point.Value, MouseButton.Left);
            context.Window.MouseUp(point.Value, MouseButton.Left);
        }

        Assert.Equal(
            [
                "https://github.com/gytxtx",
                "https://github.com/MF-Dust",
                "https://github.com/AvaloniaUI/Avalonia",
                "https://github.com/CommunityToolkit/dotnet",
                "https://github.com/AvaloniaUtils/Material.Icons.Avalonia/",
                "https://bluearchive.cafe/",
                "https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia/commit/3401794c9b06956a09a6308dfaae21341b3814f5"
            ],
            openedUrls);

        Button[] CreditRows() => section.GetVisualDescendants().OfType<Button>()
            .Where(button => button.Classes.Contains("about-credit-row")).ToArray();
    }

    [AvaloniaFact]
    public void SettingsAbout_LegalLinks_DoNotExpandCaptionLineHeight()
    {
        using var context = CreateContext();
        OpenSettings(context);
        context.ViewModel.Settings.SelectedCategory = SettingsCategoryCodes.About;
        Dispatcher.UIThread.RunJobs();

        var links = context.Window
            .GetVisualDescendants()
            .OfType<HyperlinkButton>()
            .Where(control => control.Classes.Contains("inline-legal-link"))
            .ToArray();

        Assert.Equal(2, links.Length);
        Assert.All(links, link =>
        {
            Assert.InRange(link.Bounds.Height, 1, 20);
            Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(link)));

            var row = link.GetVisualAncestors().OfType<Grid>().First();
            var caption = row.Children.OfType<TextBlock>().Single();

            Assert.Equal(caption.FontSize, link.FontSize);
        });
    }

    [AvaloniaFact]
    public async Task SettingsAbout_WhenLanguageChanges_ShowsLegalCopy()
    {
        using var themeVariant = ThemeVariantSnapshot.Capture(ThemeVariant.Dark);
        using var context = CreateContext();
        OpenSettings(context);
        context.ViewModel.Settings.Editor.Current.ThemeMode = ThemeModes.Dark;
        await context.ViewModel.Settings.SaveSettingsCommand.ExecuteAsync(null);

        foreach (var language in new[]
                 {
                     LauncherLanguages.English,
                     LauncherLanguages.SimplifiedChinese,
                     LauncherLanguages.TraditionalChinese,
                     LauncherLanguages.Japanese
                 })
        {
            context.ViewModel.Settings.SelectedCategory = SettingsCategoryCodes.General;
            Dispatcher.UIThread.RunJobs();
            context.ViewModel.Settings.Editor.Current.Language = language;
            await context.ViewModel.Settings.SaveSettingsCommand.ExecuteAsync(null);
            context.ViewModel.Settings.SelectedCategory = SettingsCategoryCodes.About;
            Dispatcher.UIThread.RunJobs();

            var links = context.Window.GetVisualDescendants().OfType<HyperlinkButton>()
                .Where(control => control.Classes.Contains("inline-legal-link"))
                .ToArray();

            Assert.Equal(2, links.Length);
            var expectedKeys = new[]
            {
                LocalizationKeys.AboutCopyrightText,
                LocalizationKeys.DefaultBackgroundCopyrightText
            };
            for (var index = 0; index < links.Length; index++)
            {
                var row = links[index].GetVisualAncestors().OfType<Grid>().First();
                var caption = row.Children.OfType<TextBlock>().Single();
                Assert.Equal(context.ViewModel.Shell.I18n[expectedKeys[index]], caption.Text);
                Assert.True(caption.Bounds.Height > 0, $"Missing legal caption layout for {language}.");
                Assert.True(links[index].Bounds.Width > 0, $"Missing legal link for {language}.");
            }
        }
    }

    [AvaloniaFact]
    public void SettingsWorkspace_WhenShown_AppliesWorkspaceStyle()
    {
        using var context = CreateContext();
        context.ViewModel.WindowChrome.IsSettingsVisible = true;

        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var workspace = context.Window
            .GetVisualDescendants()
            .OfType<Grid>()
            .Single(control => control.Classes.Contains("settings-workspace"));
        Assert.Equal(new Thickness(0), workspace.Margin);
    }

    [AvaloniaFact]
    public void SettingsWorkspace_HorizontalInsetsStayAligned()
    {
        using var context = CreateContext();
        OpenSettings(context);

        var settingsOverlay = context.Window.GetVisualDescendants()
            .OfType<MainWindowSettingsOverlay>()
            .Single();
        var dialog = settingsOverlay.GetVisualDescendants()
            .OfType<global::Cafe.Launcher.UI.Controls.DialogSurface>()
            .Single();
        var navigation = GetSettingsNavigation(context.Window);
        var selectedItem = navigation.ContainerFromIndex(navigation.SelectedIndex)
            ?? throw new InvalidOperationException("Selected settings item was not realized.");
        var content = settingsOverlay.GetVisualDescendants()
            .OfType<Grid>()
            .Single(control => control.Classes.Contains("settings-content"));
        var contentViewport = settingsOverlay.GetVisualDescendants()
            .OfType<ScrollViewer>()
            .Single(control => control.Classes.Contains("dialog-scroll"));

        var dialogTopLeft = dialog.TranslatePoint(default, context.Window);
        var selectedItemTopLeft = selectedItem.TranslatePoint(default, context.Window);
        var contentTopLeft = content.TranslatePoint(default, context.Window);
        var contentViewportTopLeft = contentViewport.TranslatePoint(default, context.Window);

        Assert.NotNull(dialogTopLeft);
        Assert.NotNull(selectedItemTopLeft);
        Assert.NotNull(contentTopLeft);
        Assert.NotNull(contentViewportTopLeft);

        var dialogLeftInset = selectedItemTopLeft!.Value.X - dialogTopLeft!.Value.X;
        var contentLeftInset = contentViewportTopLeft!.Value.X - contentTopLeft!.Value.X;
        var contentRightInset = contentViewport.Padding.Right;

        Assert.InRange(Math.Abs(dialogLeftInset - 16), 0, 1);
        Assert.InRange(Math.Abs(contentLeftInset - dialogLeftInset), 0, 1);
        Assert.InRange(Math.Abs(contentRightInset - dialogLeftInset), 0, 1);
    }

    [AvaloniaFact]
    public void SettingsTypography_WhenShown_AppliesNormalAndStrongWeights()
    {
        using var context = CreateContext();
        OpenSettings(context);

        var textBlocks = context.Window.GetVisualDescendants().OfType<TextBlock>().ToArray();
        Assert.Equal(
            FontWeight.SemiBold,
            textBlocks.Single(control =>
                control.Classes.Contains("dialog-title")
                && control.IsEffectivelyVisible).FontWeight);
        Assert.Equal(
            FontWeight.SemiBold,
            textBlocks.Single(control => control.Classes.Contains("category-title")).FontWeight);
        Assert.All(
            textBlocks.Where(control => control.Classes.Contains("group-title")),
            control => Assert.Equal(FontWeight.SemiBold, control.FontWeight));
        Assert.Equal(
            FontWeight.Normal,
            textBlocks.First(control => control.Classes.Contains("caption")).FontWeight);

        Assert.All(textBlocks.Where(control => control.IsEffectivelyVisible
            && control.Classes.Contains("section-title")
            && control.GetVisualAncestors().OfType<global::Cafe.Launcher.UI.Controls.SettingRow>().Any()),
            control => Assert.Equal(FontWeight.Normal, control.FontWeight));

        var navigation = GetSettingsNavigation(context.Window);
        var selectedItem = navigation.ContainerFromIndex(navigation.SelectedIndex)
            ?? throw new InvalidOperationException("Selected settings item was not realized.");
        var selectedText = selectedItem.GetVisualDescendants()
            .OfType<TextBlock>()
            .Single(control => control.Classes.Contains("settings-navigation-item"));
        Assert.Equal(FontWeight.SemiBold, selectedText.FontWeight);
    }

    [AvaloniaFact]
    public void LanguageFont_WhenLanguageChanges_UpdatesWindowAndInheritedText()
    {
        using var context = CreateContext();
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var visibleText = context.Window
            .GetVisualDescendants()
            .OfType<TextBlock>()
            .First(control => control.IsEffectivelyVisible);

        Assert.Equal("Segoe UI", context.Window.FontFamily.Name);
        Assert.Equal("Segoe UI", visibleText.FontFamily.Name);

        context.ViewModel.Shell.ApplyLanguage(
            LauncherLanguages.TraditionalChinese,
            hasSnapshot: false);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Microsoft JhengHei UI", context.Window.FontFamily.Name);
        Assert.Equal("Microsoft JhengHei UI", visibleText.FontFamily.Name);

        context.ViewModel.Shell.ApplyLanguage(
            LauncherLanguages.Japanese,
            hasSnapshot: false);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Yu Gothic UI", context.Window.FontFamily.Name);
        Assert.Equal("Yu Gothic UI", visibleText.FontFamily.Name);
    }

    [AvaloniaFact]
    public void SettingsWorkspace_WhenOpened_ShowsOnlyGeneralSection()
    {
        using var context = CreateContext();
        OpenSettings(context);

        Assert.Equal(SettingsCategoryCodes.General, context.ViewModel.Settings.SelectedCategory);
        AssertVisibleSettingsSection(context.Window, typeof(SettingsGeneralSection));
    }

    [AvaloniaFact]
    public void SettingsNavigation_WhenOpened_FocusesNavigationAndShowsSelectedState()
    {
        using var context = CreateContext();
        OpenSettings(context);

        var navigation = GetSettingsNavigation(context.Window);
        Assert.Equal(SettingsCategoryCodes.General, context.ViewModel.Settings.SelectedCategory);
        Assert.True(navigation.IsKeyboardFocusWithin);
        AssertNavigationSelectionVisual(navigation, SettingsCategoryCodes.General);
    }

    [AvaloniaFact]
    public async Task SettingsNavigation_AfterSave_KeepsSelectedItemVisuallySelected()
    {
        using var context = CreateContext();
        OpenSettings(context);
        var navigation = GetSettingsNavigation(context.Window);

        context.ViewModel.Settings.Editor.Current.Language = LauncherLanguages.Japanese;
        await context.ViewModel.Settings.SaveSettingsCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        Assert.False(navigation.IsKeyboardFocusWithin);
        AssertNavigationSelectionVisual(navigation, SettingsCategoryCodes.General);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveSettings_FromSettingsPage_ReportsSuccessInsteadOfThreadFailure(bool expireToastImmediately)
    {
        using var context = CreateContext(configure: services => services.AddSingleton(sp => new ToastHostViewModel(
            sp.GetRequiredService<ToastService>(),
            sp.GetRequiredService<LocalizationService>(),
            sp.GetRequiredService<ILauncherDiagnostics>(),
            async action => await Dispatcher.UIThread.InvokeAsync(action),
            (_, token) => expireToastImmediately ? Task.CompletedTask : Task.Delay(Timeout.InfiniteTimeSpan, token))));
        OpenSettings(context);
        var notifications = new List<ToastNotification>();
        context.Provider.GetRequiredService<ToastService>().ToastRaised += notifications.Add;

        context.ViewModel.Settings.Editor.Current.Language = LauncherLanguages.Japanese;
        await context.ViewModel.Settings.SaveSettingsCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        // 断言保存流程发出的结果。命令还会等待保存后的派生效果收尾（含刷新），期间 Toast
        // 可能已自行退出；显示列表的瞬时存量不能证明是否发出过成功或错误提示。
        var toast = Assert.Single(notifications);
        Assert.Equal(ToastSeverity.Success, toast.Severity);
        Assert.Equal(expireToastImmediately, context.ViewModel.Toasts.ActiveToasts.Count == 0);
    }

    [AvaloniaFact]
    public void SettingsWorkspace_WhenEachExactCodeIsSelected_ShowsOnlyItsSection()
    {
        using var context = CreateContext();
        OpenSettings(context);

        foreach (var (code, sectionType) in SettingsSections)
        {
            context.ViewModel.Settings.SelectedCategory = code;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(code, context.ViewModel.Settings.SelectedCategory);
            AssertVisibleSettingsSection(context.Window, sectionType);
        }
    }

    [AvaloniaFact]
    public void SettingsNavigation_PreservesDraftDirtyStateWithoutSaving()
    {
        using var context = CreateContext();
        OpenSettings(context);
        context.ViewModel.Settings.Editor.Current.Language = LauncherLanguages.Japanese;
        Assert.True(context.ViewModel.Settings.IsSettingsDirty);

        context.ViewModel.Settings.SelectedCategory = SettingsCategoryCodes.Appearance;
        context.ViewModel.Settings.SelectedCategory = SettingsCategoryCodes.General;

        // 导航不触发保存：草稿保持未保存的语言改动，脏标记仍在。
        Assert.Equal(LauncherLanguages.Japanese, context.ViewModel.Settings.Editor.Current.Language);
        Assert.True(context.ViewModel.Settings.IsSettingsDirty);
    }

    [AvaloniaFact]
    public void SettingsCategory_IsSessionScopedAndNewViewModelDefaultsToGeneral()
    {
        using var context = CreateContext();
        OpenSettings(context);
        context.ViewModel.Settings.SelectedCategory = SettingsCategoryCodes.DownloadNetwork;
        context.ViewModel.WindowChrome.ToggleSettingsCommand.Execute(null);
        context.ViewModel.WindowChrome.ToggleSettingsCommand.Execute(null);

        Assert.Equal(SettingsCategoryCodes.DownloadNetwork, context.ViewModel.Settings.SelectedCategory);

        using var newContext = CreateContext();
        Assert.Equal(SettingsCategoryCodes.General, newContext.ViewModel.Settings.SelectedCategory);
    }

    [AvaloniaFact]
    public void SettingRow_RendersAllFields_WithExplicitActionProperty()
    {
        using var context = CreateContext();
        OpenSettings(context);
        context.ViewModel.Settings.SelectedCategory = SettingsCategoryCodes.General;
        Dispatcher.UIThread.RunJobs();

        var rows = context.Window.GetVisualDescendants().OfType<global::Cafe.Launcher.UI.Controls.SettingRow>().ToArray();
        Assert.NotEmpty(rows);

        foreach (var row in rows)
        {
            var titleText = row.FindControl<TextBlock>("RowTitle");
            Assert.NotNull(titleText);
            Assert.Equal(row.Title, titleText!.Text);

            var descText = row.FindControl<TextBlock>("RowDescription");
            Assert.NotNull(descText);
            Assert.Equal(row.Description, descText!.Text);

            var actionPresenter = row.FindControl<ContentPresenter>("ActionPresenter");
            Assert.NotNull(actionPresenter);
            Assert.Equal(row.Action, actionPresenter!.Content);
        }
    }

    [AvaloniaTheory]
    [InlineData(1300, 754)]
    [InlineData(1024, 640)]
    public void SettingsAdvanced_AtSupportedWindowSizes_AlignsDedicatedLogActionRow(
        double width,
        double height)
    {
        using var context = CreateContext();
        context.Window.Width = width;
        context.Window.Height = height;
        OpenSettings(context);
        context.ViewModel.Settings.SelectedCategory = SettingsCategoryCodes.Advanced;
        Dispatcher.UIThread.RunJobs();

        var section = context.Window
            .GetVisualDescendants()
            .OfType<SettingsAdvancedSection>()
            .Single();
        var rows = section
            .GetVisualDescendants()
            .OfType<global::Cafe.Launcher.UI.Controls.SettingRow>()
            .Where(row => row.IsEffectivelyVisible)
            .ToArray();

        // 诊断（日志级别/日志操作）、网络兼容（本机与内网地址）与数据恢复（目录/重置）。
        Assert.Equal(5, rows.Length);
        var levelRow = Assert.Single(
            rows,
            row => row.GetVisualDescendants().OfType<ComboBox>().Any()
                && row.Title == context.ViewModel.Shell.I18n["logLevel"]);
        var levelControl = levelRow.GetVisualDescendants().OfType<ComboBox>().Single();
        var logRow = Assert.Single(rows, row => row.Title == context.ViewModel.Shell.I18n["logFiles"]);
        var logButtons = logRow
            .GetVisualDescendants()
            .OfType<Button>()
            .ToArray();
        Assert.Equal(2, logButtons.Length);
        var resetButton = rows
            .SelectMany(row => row.GetVisualDescendants().OfType<Button>())
            .Single(button => button.Classes.Contains("danger-action"));
        Assert.Contains("flat-action", resetButton.Classes);
        Assert.Equal(logButtons[0].Bounds.Height, resetButton.Bounds.Height);

        var levelTopLeft = levelControl.TranslatePoint(default, context.Window);
        Assert.NotNull(levelTopLeft);
        var levelRight = levelTopLeft.Value.X + levelControl.Bounds.Width;
        var logPresenter = logRow.FindControl<ContentPresenter>("ActionPresenter");
        Assert.NotNull(logPresenter);
        var logPresenterTopLeft = logPresenter!.TranslatePoint(default, context.Window);
        Assert.NotNull(logPresenterTopLeft);
        var logPresenterRight = logPresenterTopLeft.Value.X + logPresenter.Bounds.Width;
        Assert.InRange(Math.Abs(levelRight - logPresenterRight), 0, 1);

        var description = logRow.FindControl<TextBlock>("RowDescription");
        Assert.NotNull(description);
        var descriptionTopLeft = description!.TranslatePoint(default, context.Window);
        var firstButtonTopLeft = logButtons[0].TranslatePoint(default, context.Window);
        Assert.NotNull(descriptionTopLeft);
        Assert.NotNull(firstButtonTopLeft);
        Assert.True(
            descriptionTopLeft.Value.X + description.Bounds.Width
            <= firstButtonTopLeft.Value.X);

        AssertControlInsideWindow(levelControl, context.Window);
        Assert.All(logButtons, button => AssertControlInsideWindow(button, context.Window));
    }

    [AvaloniaFact]
    public async Task SettingsGame_InstalledProtonSelectionAndManualPathStaySynchronized()
    {
        // Linux-only rows are hidden on Windows CI unless --show-hidden-settings is
        // passed; inject the same presentation option the startup switch resolves to.
        using var context = CreateContext(configure: services =>
            services.AddSingleton(new PresentationOptions(ShowHiddenSettings: true)));
        Assert.True(context.ViewModel.IsPlatformSpecificSettingsVisible);
        OpenSettings(context);
        context.ViewModel.Settings.SelectedCategory = SettingsCategoryCodes.Game;
        await context.ViewModel.Settings.GameRuntime.PendingStatusRefresh.WaitAsync(TimeSpan.FromSeconds(5));
        var settings = context.ViewModel.Settings;
        var build = new SettingOption { DisplayName = "GE-Proton", Code = "/tools/GE-Proton" };
        settings.GameRuntime.Builds = [build];
        Dispatcher.UIThread.RunJobs();
        var section = context.Window.GetVisualDescendants().OfType<SettingsGameSection>().Single();
        var combo = Assert.Single(section.GetVisualDescendants().OfType<ComboBox>(),
            control => ReferenceEquals(control.ItemsSource, settings.GameRuntime.Builds));
        Assert.True(combo.IsEffectivelyVisible);
        var pathInput = Assert.Single(section.GetVisualDescendants().OfType<TextBox>(),
            control => AutomationProperties.GetName(control) == context.ViewModel.Shell.I18n["gameRuntimeProtonPath"]);

        Assert.Null(combo.SelectedItem);
        combo.SelectedItem = build;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(build.Code, pathInput.Text);
        Assert.Equal(build.Code, settings.Editor.Current.GameRuntime.ProtonPath);

        pathInput.Text = "/custom/proton";
        Dispatcher.UIThread.RunJobs();
        Assert.Null(combo.SelectedItem);
        Assert.Equal("/custom/proton", settings.Editor.Current.GameRuntime.ProtonPath);

        pathInput.Text = string.Empty;
        Dispatcher.UIThread.RunJobs();
        Assert.Null(combo.SelectedItem);
        Assert.True(string.IsNullOrEmpty(settings.Editor.Current.GameRuntime.ProtonPath));
    }

    [AvaloniaFact]
    public void SettingsGame_DangerAction_MatchesSiblingFlatActionHeight()
    {
        using var context = CreateContext();
        OpenSettings(context);
        context.ViewModel.Settings.SelectedCategory = SettingsCategoryCodes.Game;
        Dispatcher.UIThread.RunJobs();

        var section = context.Window
            .GetVisualDescendants()
            .OfType<SettingsGameSection>()
            .Single();
        var managementRow = section
            .GetVisualDescendants()
            .OfType<global::Cafe.Launcher.UI.Controls.SettingRow>()
            .Single(row => row.GetVisualDescendants().OfType<Button>()
                .Any(button => button.Command == context.ViewModel.Operations.RequestUninstallCommand));
        var buttons = managementRow.GetVisualDescendants().OfType<Button>().ToArray();
        var repairButton = Assert.Single(
            buttons,
            button => ReferenceEquals(button.Command, context.ViewModel.Operations.RequestRepairCommand));
        var uninstallButton = Assert.Single(buttons, button => button.Classes.Contains("danger-action"));

        Assert.Contains("flat-action", uninstallButton.Classes);
        Assert.Equal(repairButton.Bounds.Height, uninstallButton.Bounds.Height);
    }

    [AvaloniaFact]
    public void SettingsSaving_DisablesNavigationButKeepsFooterVisible()
    {
        using var context = CreateContext();
        OpenSettings(context);
        var navigation = GetSettingsNavigation(context.Window);
        var settingsOverlay = context.Window.GetVisualDescendants()
            .OfType<MainWindowSettingsOverlay>().Single();
        var footer = settingsOverlay.GetVisualDescendants().OfType<Border>()
            .Single(control => control.Classes.Contains("settings-content-actions"));

        context.ViewModel.Settings.IsSaving = true;
        Dispatcher.UIThread.RunJobs();

        Assert.False(navigation.IsEnabled);
        Assert.True(footer.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void SettingsAppearance_WhenCustomBackgroundOptionsOverflow_ShowsScrollableContent()
    {
        using var context = CreateContext();
        OpenSettings(context);
        context.ViewModel.Settings.SelectedCategory = SettingsCategoryCodes.Appearance;
        context.ViewModel.Settings.Editor.Current.ThemeColorMode = ThemeColorModes.Wallpaper;
        context.ViewModel.Settings.Editor.Current.BackgroundSource = BackgroundSources.Custom;
        context.ViewModel.Settings.Editor.Current.BackgroundFit = BackgroundFits.Uniform;
        Dispatcher.UIThread.RunJobs();

        var scrollViewer = context.Window
            .GetVisualDescendants()
            .OfType<ScrollViewer>()
            .Single(control => control.Classes.Contains("dialog-scroll"));

        Assert.True(scrollViewer.Extent.Height > scrollViewer.Viewport.Height);

        var verticalScrollBar = scrollViewer.GetVisualDescendants()
            .OfType<ScrollBar>()
            .Single(control => control.Orientation == Orientation.Vertical);
        Assert.True(verticalScrollBar.IsEffectivelyVisible);

        var settingsOverlay = context.Window.GetVisualDescendants()
            .OfType<MainWindowSettingsOverlay>()
            .Single();
        var dialog = settingsOverlay.GetVisualDescendants()
            .OfType<global::Cafe.Launcher.UI.Controls.DialogSurface>()
            .Single();
        var dialogTopLeft = dialog.TranslatePoint(default, context.Window);
        var scrollBarTopLeft = verticalScrollBar.TranslatePoint(default, context.Window);
        Assert.NotNull(dialogTopLeft);
        Assert.NotNull(scrollBarTopLeft);

        var dialogRight = dialogTopLeft!.Value.X + dialog.Bounds.Width;
        var scrollBarRight = scrollBarTopLeft!.Value.X + verticalScrollBar.Bounds.Width;
        Assert.InRange(Math.Abs(dialogRight - scrollBarRight), 0, 1);
    }

    [AvaloniaFact]
    public void SettingsOverlay_AtMinimumWindowSize_KeepsDialogAndFooterVisible()
    {
        using var context = CreateContext();
        context.Window.Width = 1024;
        context.Window.Height = 640;
        OpenSettings(context);

        var settingsOverlay = context.Window
            .GetVisualDescendants()
            .OfType<MainWindowSettingsOverlay>()
            .Single();
        var dialog = settingsOverlay
            .GetVisualDescendants()
            .OfType<global::Cafe.Launcher.UI.Controls.DialogSurface>()
            .Single();
        var footer = dialog
            .GetVisualDescendants()
            .OfType<Border>()
            .Single(control => control.Classes.Contains("settings-content-actions"));
        var footerTopLeft = footer.TranslatePoint(default, dialog);

        Assert.True(dialog.Bounds.Width <= context.Window.ClientSize.Width - 48);
        Assert.True(dialog.Bounds.Height <= context.Window.ClientSize.Height - 48);
        Assert.True(dialog.Bounds.Height > 0);
        Assert.True(footer.IsEffectivelyVisible);
        Assert.NotNull(footerTopLeft);
        Assert.True(footerTopLeft.Value.Y + footer.Bounds.Height <= dialog.Bounds.Height);
    }

    [AvaloniaFact]
    public void SettingsFooterActions_AlignWithContentHeaderRightEdge()
    {
        using var context = CreateContext();
        OpenSettings(context);

        var headerCloseButton = context.Window
            .GetVisualDescendants()
            .OfType<Button>()
            .Single(control => control.Classes.Contains("content-header-action"));
        var saveButton = context.Window
            .GetVisualDescendants()
            .OfType<Button>()
            .Single(control => ReferenceEquals(
                control.Command,
                context.ViewModel.Settings.SaveSettingsCommand));
        var headerTopLeft = headerCloseButton.TranslatePoint(default, context.Window);
        var saveTopLeft = saveButton.TranslatePoint(default, context.Window);

        Assert.NotNull(headerTopLeft);
        Assert.NotNull(saveTopLeft);
        var headerRight = headerTopLeft.Value.X + headerCloseButton.Bounds.Width;
        var saveRight = saveTopLeft.Value.X + saveButton.Bounds.Width;
        Assert.InRange(Math.Abs(headerRight - saveRight), 0, 1);
    }

    [AvaloniaFact]
    public void SettingsControls_UseMinimumAccessibleInteractionHeight()
    {
        using var context = CreateContext();
        OpenSettings(context);

        var settingControls = context.Window
            .GetVisualDescendants()
            .OfType<ComboBox>()
            .Where(control => control.Classes.Contains("setting-control"))
            .ToArray();

        Assert.NotEmpty(settingControls);
        Assert.All(settingControls, control => Assert.True(control.Bounds.Height >= 36));
    }

    [AvaloniaFact]
    public void SettingsNavigation_WhenFocused_UsesDownAndUpToChangeSelection()
    {
        using var context = CreateContext();
        OpenSettings(context);
        var navigation = GetSettingsNavigation(context.Window);
        context.Window.Activate();
        navigation.Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();
        Assert.True(navigation.IsKeyboardFocusWithin);

        context.Window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, "");
        context.Window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, "");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(SettingsCategoryCodes.Game, context.ViewModel.Settings.SelectedCategory);
        AssertVisibleSettingsSection(context.Window, typeof(SettingsGameSection));

        context.Window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, "");
        context.Window.KeyRelease(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, "");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(SettingsCategoryCodes.General, context.ViewModel.Settings.SelectedCategory);
        AssertVisibleSettingsSection(context.Window, typeof(SettingsGeneralSection));
    }

    [AvaloniaFact]
    public void SettingsContent_ShowsLocalizedTitleWithoutRedundantSubtitle()
    {
        using var context = CreateContext();
        OpenSettings(context);
        var title = context.Window.GetVisualDescendants().OfType<TextBlock>()
            .Single(control => control.Classes.Contains("category-title"));
        Assert.DoesNotContain(context.Window.GetVisualDescendants().OfType<TextBlock>(),
            control => control.Classes.Contains("category-subtitle"));

        Assert.Equal(
            context.ViewModel.Settings.Options.SettingsCategories.Single(
                option => option.Code == SettingsCategoryCodes.General).DisplayName,
            title.Text);
        Assert.Equal(title.Text, AutomationProperties.GetName(title));

        context.ViewModel.Settings.SelectedCategory = SettingsCategoryCodes.Appearance;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(
            context.ViewModel.Settings.Options.SettingsCategories.Single(
                option => option.Code == SettingsCategoryCodes.Appearance).DisplayName,
            title.Text);
        Assert.Equal(title.Text, AutomationProperties.GetName(title));
    }

    [AvaloniaFact]
    public void SettingsNavigation_WhenTabIsPressed_FocusesCurrentContent()
    {
        using var context = CreateContext();
        OpenSettings(context);
        var navigation = GetSettingsNavigation(context.Window);
        var generalSection = context.Window.GetVisualDescendants()
            .OfType<SettingsGeneralSection>().Single();
        context.Window.Activate();
        navigation.Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();
        Assert.True(navigation.IsKeyboardFocusWithin);

        context.Window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, "");
        context.Window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, "");
        Dispatcher.UIThread.RunJobs();

        var contentHeaderClose = context.Window.GetVisualDescendants()
            .OfType<Button>()
            .Single(control => control.Classes.Contains("content-header-action"));
        Assert.True(contentHeaderClose.IsKeyboardFocusWithin);
    }

    [AvaloniaFact]
    public void SettingsContent_WhenScrolledToEnd_KeepsCloseButtonInsideDialog()
    {
        using var context = CreateContext();
        OpenSettings(context);
        var closeButton = context.Window.GetVisualDescendants()
            .OfType<Button>()
            .Single(control => control.Classes.Contains("content-header-action"));
        var contentScroll = context.Window.GetVisualDescendants()
            .OfType<ScrollViewer>()
            .Single(control => control.Classes.Contains("settings-content-scroll"));
        var maxScrollOffset = 0d;
        foreach (var option in context.ViewModel.Settings.Options.SettingsCategories)
        {
            context.ViewModel.Settings.SelectedCategory = option.Code;
            Dispatcher.UIThread.RunJobs();
            maxScrollOffset = Math.Max(
                maxScrollOffset,
                Math.Max(0, contentScroll.Extent.Height - contentScroll.Viewport.Height));
        }

        Assert.True(maxScrollOffset > 0);
        contentScroll.Offset = new Vector(contentScroll.Offset.X, maxScrollOffset);
        Dispatcher.UIThread.RunJobs();

        var closeTransform = closeButton.TransformToVisual(context.Window);
        Assert.NotNull(closeTransform);
        var closeTopLeft = closeTransform.Value.Transform(closeButton.Bounds.TopLeft);
        var closeBottomRight = closeTransform.Value.Transform(closeButton.Bounds.BottomRight);
        Assert.True(closeButton.IsEffectivelyVisible);
        Assert.True(closeTopLeft.Y >= 0);
        Assert.True(closeBottomRight.Y <= context.Window.Bounds.Height);
    }

    [AvaloniaFact]
    public void SettingsFooter_BindsTransactionalSaveAndCancelCommands()
    {
        using var context = CreateContext();
        context.Window.Show();
        context.ViewModel.WindowChrome.IsSettingsVisible = true;
        context.ViewModel.Settings.Editor.Current.Language = LauncherLanguages.Japanese;
        Dispatcher.UIThread.RunJobs();
        var footerButtons = context.Window
            .GetVisualDescendants()
            .OfType<Button>()
            .Where(control =>
                control.Classes.Contains("dialog-action")
                && (ReferenceEquals(
                        control.Command,
                        context.ViewModel.Settings.SaveSettingsCommand)
                    || ReferenceEquals(
                        control.Command,
                        context.ViewModel.WindowChrome.ToggleSettingsCommand)))
            .ToArray();
        Assert.Equal(2, footerButtons.Length);
        var save = footerButtons.Single(button =>
            ReferenceEquals(button.Command, context.ViewModel.Settings.SaveSettingsCommand));
        var cancel = footerButtons.Single(button =>
            ReferenceEquals(button.Command, context.ViewModel.WindowChrome.ToggleSettingsCommand));

        Assert.True(save.IsEnabled);
        cancel.Command!.Execute(cancel.CommandParameter);
        Dispatcher.UIThread.RunJobs();

        Assert.True(context.ViewModel.WindowChrome.IsSettingsVisible);
        Assert.True(context.ViewModel.Settings.IsUnsavedChangesVisible);
    }

    [AvaloniaFact]
    public void SettingsOverlay_WhenOpenedWithoutChanges_RemainsClean()
    {
        using var context = CreateContext();
        context.Window.Show();
        var changedProperties = new List<string>();
        context.ViewModel.Settings.Editor.CurrentPropertyChanged += (_, args) =>
            changedProperties.Add(args.PropertyName ?? "<null>");

        context.ViewModel.WindowChrome.ToggleSettingsCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.True(context.ViewModel.WindowChrome.IsSettingsVisible);
        Assert.False(
            context.ViewModel.Settings.IsSettingsDirty,
            string.Join(", ", changedProperties));
    }

    [AvaloniaFact]
    public void GeneralSegments_SelectingAValueUpdatesTheDraftAndMarksSettingsDirty()
    {
        using var context = CreateContext();
        OpenSettings(context);
        var settings = context.ViewModel.Settings;
        settings.SelectedCategory = SettingsCategoryCodes.General;
        // 预发布构建默认使用 Beta 通道；以显式基线开始，避免依赖宿主构建身份。
        var baseline = settings.Editor.GetSavedSnapshot();
        baseline.CloseBehavior = CloseBehaviors.Minimize;
        baseline.StatusDetailMode = StatusDetailModes.Compact;
        baseline.UpdateChannel = UpdateChannels.Stable;
        settings.Editor.ApplySnapshot(baseline);
        Dispatcher.UIThread.RunJobs();
        var section = context.Window.GetVisualDescendants().OfType<SettingsGeneralSection>().Single();

        var closeSegments = SegmentOptions(section, "SettingsCloseBehavior");
        Assert.Equal(2, closeSegments.Length);
        Assert.True(Assert.Single(closeSegments, segment =>
            AutomationProperties.GetName(segment) == context.ViewModel.Shell.I18n[LocalizationKeys.CloseBehaviorMinimize]).IsChecked);
        Assert.False(settings.IsSettingsDirty);

        Assert.Single(closeSegments, segment =>
            AutomationProperties.GetName(segment) == context.ViewModel.Shell.I18n[LocalizationKeys.CloseBehaviorExit]).IsChecked = true;
        var statusSegments = SegmentOptions(section, "SettingsStatusDetailMode");
        Assert.Equal(2, statusSegments.Length);
        Assert.Single(statusSegments, segment =>
            AutomationProperties.GetName(segment) == context.ViewModel.Shell.I18n[LocalizationKeys.StatusDetailModeHidden]).IsChecked = true;
        var channelSegments = SegmentOptions(section, "SettingsUpdateChannel");
        Assert.Equal(2, channelSegments.Length);
        Assert.Single(channelSegments, segment =>
            AutomationProperties.GetName(segment) == context.ViewModel.Shell.I18n[LocalizationKeys.LauncherUpdateChannelBeta]).IsChecked = true;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(CloseBehaviors.Exit, settings.Editor.Current.CloseBehavior);
        Assert.Equal(StatusDetailModes.Hidden, settings.Editor.Current.StatusDetailMode);
        Assert.Equal(UpdateChannels.Beta, settings.Editor.Current.UpdateChannel);
        Assert.True(settings.IsSettingsDirty);
    }

    [AvaloniaFact]
    public void DownloadSegments_SelectingAValueUpdatesTheDraftAndMarksSettingsDirty()
    {
        using var context = CreateContext();
        OpenSettings(context);
        var settings = context.ViewModel.Settings;
        settings.SelectedCategory = SettingsCategoryCodes.DownloadNetwork;
        Dispatcher.UIThread.RunJobs();
        var section = context.Window.GetVisualDescendants().OfType<SettingsDownloadNetworkSection>().Single();

        // 初始下载源由运行配置决定，用例只要求从当前草稿切到另一项。
        var initial = settings.Editor.Current.PatchUrlGroup;
        var target = initial == PatchUrlGroups.Cafe ? PatchUrlGroups.Official : PatchUrlGroups.Cafe;
        var initialName = initial == PatchUrlGroups.Cafe
            ? LocalizationKeys.DownloadSourceCafe
            : LocalizationKeys.DownloadSourceOfficial;
        var targetName = target == PatchUrlGroups.Cafe
            ? LocalizationKeys.DownloadSourceCafe
            : LocalizationKeys.DownloadSourceOfficial;

        var segments = SegmentOptions(section, "SettingsDownloadSource");
        Assert.Equal(2, segments.Length);
        Assert.True(Assert.Single(segments, segment =>
            AutomationProperties.GetName(segment) == context.ViewModel.Shell.I18n[initialName]).IsChecked);
        Assert.False(settings.IsSettingsDirty);

        Assert.Single(segments, segment =>
            AutomationProperties.GetName(segment) == context.ViewModel.Shell.I18n[targetName]).IsChecked = true;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(target, settings.Editor.Current.PatchUrlGroup);
        Assert.True(settings.IsSettingsDirty);
    }

    private static ListBox GetSettingsNavigation(MainWindow window) =>
        window.GetVisualDescendants().OfType<ListBox>().Single(control =>
            AutomationProperties.GetAutomationId(control) == "SettingsNavigation");

    private static IReadOnlyList<ListBoxItem> GetNavigationItems(ListBox navigation) =>
        navigation.GetVisualDescendants().OfType<ListBoxItem>().ToArray();

    private static void AssertNavigationSelectionVisual(ListBox navigation, string expectedCode)
    {
        var selectedItem = GetNavigationItems(navigation).Single(item => item.IsSelected);
        var presenter = selectedItem.GetVisualDescendants().OfType<ContentPresenter>().Single();

        Assert.Equal(expectedCode, ((SettingOption)selectedItem.DataContext!).Code);
        Assert.Equal(new Thickness(0), selectedItem.BorderThickness);
        var secondaryContainer = Assert.IsType<SolidColorBrush>(
            Application.Current!.Resources["Launcher.Color.SecondaryContainer"]).Color;
        var onSecondaryContainer = Assert.IsType<SolidColorBrush>(
            Application.Current!.Resources["Launcher.Color.OnSecondaryContainer"]).Color;
        Assert.Equal(secondaryContainer, Assert.IsType<SolidColorBrush>(selectedItem.Background).Color);
        Assert.Equal(secondaryContainer, Assert.IsType<SolidColorBrush>(presenter.Background).Color);
        Assert.Equal(onSecondaryContainer, Assert.IsType<SolidColorBrush>(selectedItem.Foreground).Color);
    }

    private static void AssertVisibleSettingsSection(MainWindow window, Type expectedType)
    {
        var sections = window.GetVisualDescendants()
            .Where(control => SettingsSections.Any(section => section.SectionType == control.GetType()))
            .ToArray();
        Assert.Equal(SettingsSections.Length, sections.Length);
        Assert.Single(sections, control => control.GetType() == expectedType && control.IsEffectivelyVisible);
        Assert.All(
            sections.Where(control => control.GetType() != expectedType),
            control => Assert.False(control.IsEffectivelyVisible));
    }
}
