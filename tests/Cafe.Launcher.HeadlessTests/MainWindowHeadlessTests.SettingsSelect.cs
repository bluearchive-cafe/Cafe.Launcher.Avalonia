using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.UI.Views;
using Material.Icons.Avalonia;
using Microsoft.Extensions.DependencyInjection;

namespace Cafe.Launcher.HeadlessTests;

public sealed partial class MainWindowHeadlessTests
{
    [AvaloniaTheory]
    [InlineData(LauncherLanguages.English)]
    [InlineData(LauncherLanguages.SimplifiedChinese)]
    [InlineData(LauncherLanguages.TraditionalChinese)]
    [InlineData(LauncherLanguages.Japanese)]
    public async Task SettingsSelect_AllCategoriesLanguagesAndTextScales_KeepChecksAndReadableRows(string language)
    {
        using var variant = ThemeVariantSnapshot.Capture(ThemeVariant.Light);
        using var context = CreateContext();
        PrepareGoldenWindow(context);
        context.ViewModel.Shell.ApplyLanguage(language, hasSnapshot: false);
        OpenSettings(context);
        var settings = context.ViewModel.Settings;
        var overlay = context.Window.GetVisualDescendants().OfType<MainWindowSettingsOverlay>().Single();
        settings.Editor.Current.ThemeMode = ThemeModes.Light;
        settings.Editor.Current.ThemeColorMode = ThemeColorModes.Wallpaper;
        settings.Appearance.IsAdvancedColorExpanded = true;
        await settings.Appearance.PendingThemeRefresh.WaitAsync(TimeSpan.FromSeconds(5));

        foreach (var (mode, theme) in new[] { (ThemeModes.Light, ThemeVariant.Light), (ThemeModes.Dark, ThemeVariant.Dark) })
        {
            settings.Editor.Current.ThemeMode = mode;
            context.Provider.GetRequiredService<ThemeApplier>().ApplyThemeMode(mode);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(theme, context.Window.ActualThemeVariant);
            AssertSettingsSelectRows(context, overlay, theme);
        }
    }

    private static void AssertSettingsSelectRows(TestContext context, MainWindowSettingsOverlay overlay, ThemeVariant theme)
    {
        double[] scales = [1, 1.5, 2];
        var visitsByScale = new int[scales.Length];
        foreach (var (category, sectionType) in SettingsSections)
        {
            context.ViewModel.Settings.SelectedCategory = category;
            Dispatcher.UIThread.RunJobs();
            var section = Assert.Single(overlay.GetVisualDescendants().OfType<UserControl>(),
                control => control.GetType() == sectionType && control.IsEffectivelyVisible);
            // Keep the selected section and its templates realized for all font sizes.
            for (var scaleIndex = 0; scaleIndex < scales.Length; scaleIndex++)
            {
                var scale = scales[scaleIndex];
                using var typography = scale == 1d ? null : new TypographyScaleSnapshot(section, scale);
                foreach (var combo in section.GetVisualDescendants().OfType<ComboBox>().Where(control => control.IsEffectivelyVisible))
                {
                    visitsByScale[scaleIndex]++;
                    combo.BringIntoView();
                    Dispatcher.UIThread.RunJobs();
                    var bounds = combo.Bounds;
                    var previous = combo.SelectedValue;
                    Assert.NotNull(combo.SelectedItem);
                    var popup = PreviewPart<Popup>(combo, "PART_Popup");
                    popup.ShouldUseOverlayLayer = true;
                    combo.IsDropDownOpen = true;
                    Dispatcher.UIThread.RunJobs();
                    using (var popupTypography = scale == 1d ? null : new TypographyScaleSnapshot(popup.Child!, scale))
                    {
                        if (popupTypography is not null)
                        {
                            Dispatcher.UIThread.RunJobs();
                        }
                        var items = popup.Child!.GetVisualDescendants().OfType<ComboBoxItem>().ToArray();
                        Assert.NotEmpty(items);
                        foreach (var item in items)
                        {
                            var check = PreviewPart<MaterialIcon>(item, "SettingsChoiceCheck");
                            Assert.Equal(item.IsSelected ? 1 : 0, check.Opacity);
                            var text = Assert.Single(
                                PreviewPart<ContentPresenter>(item, "PART_ContentPresenter")
                                    .GetVisualDescendants().OfType<TextBlock>(),
                                candidate => !candidate.Classes.Contains("caption"));
                            Assert.Equal(14 * scale, TextElement.GetFontSize(text));
                            Assert.Equal(TextWrapping.Wrap, text.TextWrapping);
                            Assert.Equal(TextTrimming.None, text.TextTrimming);
                            Assert.True(item.Bounds.Height >= text.DesiredSize.Height + 16 - 1,
                                $"{theme}, {category}, scale {scale}: '{text.Text}' is clipped.");
                        }

                        var scroller = Assert.Single(popup.Child!.GetVisualDescendants().OfType<ScrollViewer>());
                        Assert.True(scroller.Bounds.Height > 0);
                        Assert.True(scroller.Extent.Height >= scroller.Viewport.Height - 1);
                    }

                    combo.IsDropDownOpen = false;
                    Dispatcher.UIThread.RunJobs();
                    Assert.Equal(previous, combo.SelectedValue);
                    Assert.Equal(bounds, combo.Bounds);
                }
            }
        }

        Assert.Equal(theme, context.Window.ActualThemeVariant);
        // The Linux-only runtime selector is hidden on Windows; all other ten choices are exercised.
        var expected = OperatingSystem.IsLinux() ? 11 : 10;
        for (var index = 0; index < scales.Length; index++)
        {
            Assert.True(visitsByScale[index] == expected,
                $"{theme}, scale {scales[index]}: visited {visitsByScale[index]} selectors, expected {expected}.");
        }
    }

    [AvaloniaTheory]
    [InlineData(false, "#FF0000", ThemeColorVariants.Fidelity)]
    [InlineData(true, "#00FF00", ThemeColorVariants.Content)]
    public void SettingsSelect_SelectedAccentStatesAndLiveThemeSwitch_PreservePaintedContrast(bool dark, string seed, string schemeVariant)
    {
        using var variant = ThemeVariantSnapshot.Capture(dark ? ThemeVariant.Dark : ThemeVariant.Light);
        using var context = CreateContext();
        var combo = new ComboBox { Width = 280, Classes = { "setting-control" }, ItemsSource = new[] { "Selected", "Other" }, SelectedIndex = 0 };
        var window = new Window { Width = 600, Height = 400, Content = new UserControl { Classes = { "settings-section" }, Content = combo } };
        window.Bind(Window.BackgroundProperty, window.GetResourceObservable("Launcher.Color.Dialog.Background"));
        try
        {
            window.Show();
            foreach (var strategy in new[] { NeutralColorStrategies.BrandBlue, NeutralColorStrategies.SeedFollowing })
            {
                var applier = context.Provider.GetRequiredService<ThemeApplier>();
                applier.ApplyThemeMode(dark ? ThemeModes.Dark : ThemeModes.Light);
                applier.ApplyScheme(Color.Parse(seed), schemeVariant, dark, strategy);
                var popup = PreviewPart<Popup>(combo, "PART_Popup");
                popup.ShouldUseOverlayLayer = true;
                combo.IsDropDownOpen = true;
                Dispatcher.UIThread.RunJobs();
                var selected = Assert.IsType<ComboBoxItem>(combo.ContainerFromIndex(0));
                Assert.IsType<ComboBoxItem>(combo.ContainerFromIndex(1)).Focus(NavigationMethod.Unspecified);
                selected.Focus(NavigationMethod.Tab);
                var bounds = selected.Bounds;
                AssertSettingsSelectContrast(window, selected);
                window.MouseMove(selected.TranslatePoint(new Point(20, 20), window)!.Value);
                AssertSettingsSelectContrast(window, selected);
                ((IPseudoClasses)selected.Classes).Set(":pressed", true);
                AssertSettingsSelectContrast(window, selected);
                Assert.Equal(bounds, selected.Bounds);
                ((IPseudoClasses)selected.Classes).Set(":pressed", false);
                applier.ApplyThemeMode(dark ? ThemeModes.Light : ThemeModes.Dark);
                AssertSettingsSelectContrast(window, selected);
                combo.IsDropDownOpen = false;
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async System.Threading.Tasks.Task Golden_SettingsSelectSource_MatchesBaseline(bool dark)
    {
        using var variant = ThemeVariantSnapshot.Capture(dark ? ThemeVariant.Dark : ThemeVariant.Light);
        using var context = CreateContext();
        PrepareGoldenWindow(context, dark ? ThemeVariant.Dark : ThemeVariant.Light);
        context.ViewModel.Shell.ApplyLanguage(LauncherLanguages.SimplifiedChinese, hasSnapshot: false);
        OpenSettings(context);
        context.ViewModel.Settings.Editor.Current.ThemeMode = dark ? ThemeModes.Dark : ThemeModes.Light;
        context.ViewModel.Settings.SelectedCategory = SettingsCategoryCodes.Appearance;
        await context.ViewModel.Settings.Appearance.PendingThemeRefresh.WaitAsync(TimeSpan.FromSeconds(5));
        Dispatcher.UIThread.RunJobs();
        var section = context.Window.GetVisualDescendants().OfType<SettingsAppearanceSection>().Single();
        var combo = section.GetVisualDescendants().OfType<ComboBox>()
            .Single(control => ReferenceEquals(control.ItemsSource, context.ViewModel.Settings.Options.ThemeColor));
        combo.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        PreviewPart<Popup>(combo, "PART_Popup").ShouldUseOverlayLayer = true;
        combo.IsDropDownOpen = true;
        if (dark)
        {
            GoldenScreenshot.Compare(context.Window, "settings-select-source-dark");
        }
        else
        {
            GoldenScreenshot.Compare(context.Window, "settings-select-source-light");
        }
        combo.IsDropDownOpen = false;
    }

    private static void AssertSettingsSelectContrast(Window window, ComboBoxItem item)
    {
        Dispatcher.UIThread.RunJobs();
        var background = PreviewColor(Assert.IsAssignableFrom<IBrush>(window.Background));
        var fill = PreviewComposite(PreviewPart<Border>(item, "SettingsChoiceSurface").Background, background);
        fill = PreviewComposite(PreviewPart<Border>(item, "SettingsChoiceState").Background, fill);
        Assert.True(ColorUtilsForSelect(PreviewPart<ContentPresenter>(item, "PART_ContentPresenter").Foreground, fill) >= 4.5);
        Assert.True(ColorUtilsForSelect(PreviewPart<MaterialIcon>(item, "SettingsChoiceCheck").Foreground, fill) >= 3);
        Assert.True(ColorUtilsForSelect(PreviewPart<Border>(item, "SettingsChoiceFocus").BorderBrush, fill) >= 3);
    }

    private static double ColorUtilsForSelect(IBrush? foreground, Color background) =>
        UI.Helpers.ColorUtils.GetContrastRatio(PreviewColor(foreground), background);
}
