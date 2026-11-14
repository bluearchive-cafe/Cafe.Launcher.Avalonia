using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.UI.Controls;
using Cafe.Launcher.UI.Features.SetupWizard;
using Cafe.Launcher.UI.Helpers;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.UI.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Cafe.Launcher.HeadlessTests;

public sealed partial class MainWindowHeadlessTests
{
    [AvaloniaTheory]
    [InlineData(LauncherLanguages.English)]
    [InlineData(LauncherLanguages.SimplifiedChinese)]
    [InlineData(LauncherLanguages.TraditionalChinese)]
    [InlineData(LauncherLanguages.Japanese)]
    public void Visual_SettingsHeader_TitlesAndCloseButtonShareVerticalCenter(string language)
    {
        using var context = CreateContext();
        var window = context.Window;
        window.Width = 1024;
        window.Height = 640;
        context.ViewModel.Shell.ApplyLanguage(language, hasSnapshot: false);
        OpenSettings(context);
        Assert.Equal(new Size(1024, 640), window.ClientSize);
        var overlay = window.GetVisualDescendants().OfType<MainWindowSettingsOverlay>().Single();
        var navigation = overlay.FindControl<ListBox>("SettingsNavigation")!;
        var navigationHeader = overlay.GetVisualDescendants().OfType<Border>()
            .Single(control => control.Classes.Contains("settings-navigation-header"));
        var settingsTitle = navigationHeader.GetVisualDescendants().OfType<TextBlock>()
            .Single(control => control.Classes.Contains("dialog-title"));
        var categoryTitle = overlay.GetVisualDescendants().OfType<TextBlock>()
            .Single(control => control.Classes.Contains("category-title"));
        var close = overlay.GetVisualDescendants().OfType<Button>()
            .Single(control => control.Classes.Contains("content-header-action"));
        var settingsTitleSize = settingsTitle.FontSize;
        var categoryTitleSize = categoryTitle.FontSize;
        var settings = context.ViewModel.Settings;

        // Visit every category at 100% first so its templates exist before scaling the window.
        foreach (var scale in new[] { 1d, 1.5d, 2d })
        {
            using var typography = scale == 1d ? null : new TypographyScaleSnapshot(window, scale);
            foreach (var (category, _) in SettingsSections)
            {
                settings.SelectedCategory = category;
                if (category == SettingsCategoryCodes.Appearance)
                {
                    settings.Appearance.IsAdvancedColorExpanded = true;
                }
                Dispatcher.UIThread.RunJobs();
                var selectedCategory = Assert.IsType<SettingOption>(navigation.SelectedItem);
                Assert.Equal(category, selectedCategory.Code);
                Assert.Equal(selectedCategory.DisplayName, categoryTitle.Text);
                Assert.Equal(settingsTitleSize * scale, settingsTitle.FontSize);
                Assert.Equal(categoryTitleSize * scale, categoryTitle.FontSize);

                var settingsCenter = settingsTitle.TranslatePoint(default, window)!.Value.Y + settingsTitle.Bounds.Height / 2;
                var categoryCenter = categoryTitle.TranslatePoint(default, window)!.Value.Y + categoryTitle.Bounds.Height / 2;
                var closeCenter = close.TranslatePoint(default, window)!.Value.Y + close.Bounds.Height / 2;
                var geometry = $"{language}, {scale}, {category}: settings={settingsCenter}, category={categoryCenter}, close={closeCenter}";
                Assert.True(Math.Abs(settingsCenter - categoryCenter) <= 1, geometry);
                Assert.True(Math.Abs(categoryCenter - closeCenter) <= 1, geometry);
            }
        }
    }

    [AvaloniaTheory]
    [InlineData(1d)]
    [InlineData(1.5d)]
    public void Visual_SettingRow_WithoutDescription_CentersTitleAndActionWithVerticalInsets(double scale)
    {
        using var context = CreateContext();
        context.ViewModel.Shell.ApplyLanguage(LauncherLanguages.SimplifiedChinese, hasSnapshot: false);
        OpenSettings(context);
        using var typography = new TypographyScaleSnapshot(context.Window, scale);
        Dispatcher.UIThread.RunJobs();

        var rows = context.Window.GetVisualDescendants().OfType<SettingRow>()
            .Where(row => row.IsEffectivelyVisible && string.IsNullOrEmpty(row.Description)
                && row.FindControl<TextBlock>("RowHint") is not { IsVisible: true })
            .ToArray();
        Assert.Equal(4, rows.Length);
        foreach (var row in rows)
        {
            var title = row.FindControl<TextBlock>("RowTitle");
            var layout = row.FindControl<Grid>("RowLayout");
            var action = Assert.IsAssignableFrom<Control>(row.Action);
            Assert.NotNull(title);
            Assert.NotNull(layout);
            var titlePosition = title.TranslatePoint(default, row);
            var actionPosition = action.TranslatePoint(default, row);
            Assert.NotNull(titlePosition);
            Assert.NotNull(actionPosition);
            var center = row.Bounds.Height / 2;
            var geometry = $"{row.Title}: row={row.Bounds.Height}, firstRow={layout.RowDefinitions[0].ActualHeight}, "
                + $"titleY={titlePosition.Value.Y}, titleHeight={title.Bounds.Height}, "
                + $"actionY={actionPosition.Value.Y}, actionHeight={action.Bounds.Height}";
            Assert.True(Math.Abs(titlePosition.Value.Y + title.Bounds.Height / 2 - center) <= 1, geometry);
            Assert.True(Math.Abs(actionPosition.Value.Y + action.Bounds.Height / 2 - center) <= 1, geometry);
            Assert.True(actionPosition.Value.Y >= 6 && row.Bounds.Height - actionPosition.Value.Y - action.Bounds.Height >= 6, geometry);
        }
    }

    [AvaloniaFact]
    public async Task Visual_DecorativeBordersAndDividers_SnapToDevicePixels()
    {
        using var context = CreateContext();
        PrepareGoldenWindow(context);
        var window = context.Window;
        window.Width = 1301;
        double[] scales = [1, 1.25, 1.5, 1.75, 2];

        OpenSettings(context);
        var topEdge = new Thickness(0, 1, 0, 0);
        foreach (var (category, _) in SettingsSections)
        {
            context.ViewModel.Settings.SelectedCategory = category;
            AssertAtEachScale(scale => AssertBorders(scale, "settings-row-divider", topEdge));
        }

        context.ViewModel.Settings.SelectedCategory = SettingsCategoryCodes.About;
        AssertAtEachScale(scale =>
        {
            AssertBorders(scale, "about-identity-card", new Thickness(1));
            AssertBorders(scale, "about-credit-divider", topEdge);
        });
        window.GetVisualDescendants().OfType<Expander>()
            .Single(expander => expander.Name == "AboutBuildExpander").IsExpanded = true;
        AssertAtEachScale(scale =>
        {
            AssertBorders(scale, "about-kv-list", new Thickness(1));
            AssertBorders(scale, "settings-row-divider", topEdge);
        });
        context.ViewModel.WindowChrome.ToggleSettingsCommand.Execute(null);

        context.ViewModel.ResourcePanel.IsResourcePanelVisible = true;
        AssertAtEachScale(scale =>
        {
            AssertBorders(scale, "dialog-card", new Thickness(1));
            AssertBorders(scale, "uid-section-divider", topEdge);
            AssertBorders(scale, "resource-row-divider", topEdge);
            AssertBorders(scale, "PART_FooterBand", topEdge);
        });
        context.ViewModel.ResourcePanel.IsResourcePanelVisible = false;

        context.ViewModel.Operations.Uninstall.Open(UninstallSnapshot());
        AssertAtEachScale(scale =>
        {
            AssertBorders(scale, "uninstall-divider");
            AssertBorders(scale, "PART_FooterBand", topEdge);
        });
        context.ViewModel.Operations.Uninstall.CloseCommand.Execute(null);

        context.ViewModel.Dialogs.ShowSetupWizard();
        var wizard = context.ViewModel.Dialogs.SetupWizard;
        AssertAtEachScale(scale => Assert.Equal(6, AssertBorders(scale, "wizard-divider")));
        wizard.NextCommand.Execute(null);
        wizard.NextCommand.Execute(null);
        AssertAtEachScale(scale => Assert.Equal(3, AssertBorders(scale, "wizard-divider")));
        AcceptAgreement(wizard);
        wizard.NextCommand.Execute(null);
        wizard.GamePath = System.IO.Path.Combine(context.Directory.Path, "available-installation");
        await WaitForGamePathStatusAsync(wizard, SetupWizardGamePathStatus.AvailableForInstallation);
        wizard.NextCommand.Execute(null);
        wizard.NextCommand.Execute(null);
        wizard.NextCommand.Execute(null);
        Assert.True(wizard.IsLastStep);
        var summaryDividerCount = 2 + wizard.SummaryItems.Count(item => item.HasDivider);
        AssertAtEachScale(scale => Assert.Equal(summaryDividerCount, AssertBorders(scale, "wizard-divider")));

        void AssertAtEachScale(Action<double> assertLayout)
        {
            foreach (var scale in scales)
            {
                if (window.RenderScaling != scale)
                {
                    // A shown headless window drains layout and renders Skia frames
                    // on every scaling change (~200 ms per switch here). Hiding
                    // skips the renders; Show still runs the scaling-changed
                    // layout pass, like a freshly shown surface.
                    window.Hide();
                    window.SetRenderScaling(scale);
                    window.Show();
                }
                else
                {
                    Dispatcher.UIThread.RunJobs();
                }
                Assert.Equal(scale, window.RenderScaling);
                assertLayout(scale);
            }

            // Continue from the current DPI when the next surface becomes visible.
            Array.Reverse(scales);
        }

        int AssertBorders(double scale, string name, Thickness? thickness = null)
        {
            var borders = window.GetVisualDescendants().OfType<Border>()
                .Where(border => border.IsEffectivelyVisible && (border.Name == name || border.Classes.Contains(name)))
                .ToArray();
            Assert.NotEmpty(borders);
            foreach (var border in borders)
            {
                Assert.True(border.UseLayoutRounding, name);
                if (thickness is { } expected)
                {
                    Assert.Equal(expected, border.BorderThickness);
                }
                else
                {
                    Assert.Equal(1d, border.Height);
                }

                var origin = border.TranslatePoint(default, window)!.Value;
                foreach (var edge in new[] { origin.X, origin.Y, origin.X + border.Bounds.Width, origin.Y + border.Bounds.Height })
                {
                    var pixel = edge * scale;
                    Assert.True(Math.Abs(pixel - Math.Round(pixel)) < 1e-6, $"{name}: edge {pixel} px at {scale:P0}");
                }
            }

            return borders.Length;
        }
    }

    [AvaloniaFact]
    public void Visual_SettingRow_EmptyDescriptionDoesNotReserveALine()
    {
        using var context = CreateContext();
        var row = new SettingRow { Title = "Language" };
        context.Window.Content = row;
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var description = row.FindControl<TextBlock>("RowDescription");
        Assert.NotNull(description);
        Assert.False(description.IsVisible);
        row.Description = "Explains a choice";
        Dispatcher.UIThread.RunJobs();
        Assert.True(description.IsVisible);
        row.Description = string.Empty;
        Dispatcher.UIThread.RunJobs();
        Assert.False(description.IsVisible);
    }

    [AvaloniaFact]
    public void Visual_SettingRow_HintShowsByDefaultAndCollapsesWhenEmptyOrGated()
    {
        using var context = CreateContext();
        var row = new SettingRow { Title = "Neutral strategy", Hint = "Seed follows the selected strategy." };
        context.Window.Content = row;
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var hint = row.FindControl<TextBlock>("RowHint");
        Assert.NotNull(hint);
        // 默认开启：设置 Hint 不再需要显式设 IsHintVisible。
        Assert.True(hint!.IsVisible);
        row.IsHintVisible = false;
        Dispatcher.UIThread.RunJobs();
        Assert.False(hint.IsVisible);
        row.IsHintVisible = true;
        row.Hint = string.Empty;
        Dispatcher.UIThread.RunJobs();
        Assert.False(hint.IsVisible);
    }

    [AvaloniaFact]
    public void Visual_SettingRow_NarrowThenWide_ReflowsWithoutChangingSelection()
    {
        using var context = CreateContext();
        var select = new ComboBox
        {
            ItemsSource = new[] { "System", "Light", "Dark" },
            SelectedIndex = 2,
            Classes = { "setting-control" }
        };
        var row = new SettingRow { Title = "Theme", Description = "A longer description that must remain readable", Action = select };
        context.Window.Content = row;
        context.Window.Show();
        row.Width = 360;
        Dispatcher.UIThread.RunJobs();
        var action = row.FindControl<ContentPresenter>("ActionPresenter");
        var title = row.FindControl<TextBlock>("RowTitle");
        Assert.NotNull(action);
        Assert.NotNull(title);
        Assert.Equal(1, Grid.GetRow(action));
        Assert.True(action.Bounds.Y >= title.Bounds.Bottom);
        Assert.True(action.Bounds.Right <= row.Bounds.Width);

        row.Width = 700;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, Grid.GetRow(action));
        Assert.Equal(1, Grid.GetColumn(action));
        Assert.Equal(2, select.SelectedIndex);
    }

    public static TheoryData<string, double, bool> StackedSettingRowCases
    {
        get
        {
            var cases = new TheoryData<string, double, bool>();
            foreach (var copyState in new[] { "title", "description", "hint" })
            {
                foreach (var scale in new[] { 1d, 1.5d })
                {
                    cases.Add(copyState, scale, false);
                    cases.Add(copyState, scale, true);
                }
            }

            return cases;
        }
    }

    [AvaloniaTheory]
    [MemberData(nameof(StackedSettingRowCases))]
    public void Visual_SettingRow_StackedActionKeepsBottomInsetAcrossReflow(string copyState, double scale, bool groupedAction)
    {
        using var context = CreateContext();
        var select = new ComboBox
        {
            ItemsSource = new[] { "System", "Light", "Dark" },
            SelectedIndex = 2,
            Classes = { "setting-control" }
        };
        var button = new Button { Content = "Choose Image", Classes = { "flat-action" } };
        Control action = groupedAction
            ? new WrapPanel
            {
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Children = { button, new Button { Content = "Choose Folder", Classes = { "flat-action" } } }
            }
            : select;
        var row = new SettingRow
        {
            Width = 700,
            Title = "Theme",
            Description = copyState == "title" ? null : "Choose how the launcher looks. This explanation must remain readable when the row becomes narrow.",
            Hint = "This additional hint appears for the selected option and must also wrap without clipping.",
            IsHintVisible = copyState == "hint",
            Action = action
        };
        var nextRow = new SettingRow { Title = "Next setting" };
        context.Window.Content = new StackPanel { Children = { row, nextRow } };
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();
        using var typography = new TypographyScaleSnapshot(context.Window, scale);
        Dispatcher.UIThread.RunJobs();
        var presenter = row.FindControl<ContentPresenter>("ActionPresenter")!;
        var copy = row.FindControl<TextBlock>("RowTitle")!.GetVisualAncestors().OfType<StackPanel>().First();
        var divider = nextRow.FindControl<Border>("TopDivider")!;
        Control focusTarget = groupedAction ? button : select;
        Assert.True(focusTarget.Focus(NavigationMethod.Tab));
        Dispatcher.UIThread.RunJobs();
        var horizontalHeight = row.Bounds.Height;

        foreach (var width in new[] { 360, 700, 360 })
        {
            row.Width = width;
            Dispatcher.UIThread.RunJobs();
            Assert.Same(action, presenter.Content);
            Assert.True(focusTarget.IsFocused);
            if (!groupedAction)
            {
                Assert.Equal(2, select.SelectedIndex);
            }
            var actionPosition = action.TranslatePoint(default, row)!.Value;
            if (width == 700)
            {
                Assert.Equal(0, Grid.GetRow(presenter));
                Assert.Equal(horizontalHeight, row.Bounds.Height);
                Assert.InRange(Math.Abs(actionPosition.Y + action.Bounds.Height / 2 - row.Bounds.Height / 2), 0, 1);
                continue;
            }

            Assert.Equal(1, Grid.GetRow(presenter));
            var actionBottom = action.GetVisualDescendants().Prepend(action).OfType<Control>()
                .Where(control => control.IsEffectivelyVisible && control is Button or ComboBox)
                .Max(control => control.TranslatePoint(default, row)!.Value.Y + control.Bounds.Height);
            var dividerTop = divider.TranslatePoint(default, row)!.Value.Y;
            var expectedInset = copy.Margin.Bottom;
            Assert.True(expectedInset > 0);
            Assert.InRange(row.Bounds.Height - actionBottom, expectedInset - 1, expectedInset + 1);
            Assert.InRange(dividerTop - actionBottom, expectedInset - 1, expectedInset + 1);
        }
    }

    public static TheoryData<string, bool> VisualSettingsCases
    {
        get
        {
            var cases = new TheoryData<string, bool>();
            foreach (var language in new[] { LauncherLanguages.English, LauncherLanguages.SimplifiedChinese, LauncherLanguages.TraditionalChinese, LauncherLanguages.Japanese })
            {
                cases.Add(language, false);
                cases.Add(language, true);
            }

            return cases;
        }
    }

    [AvaloniaTheory]
    [MemberData(nameof(VisualSettingsCases))]
    public void Visual_Settings_AllCategoriesKeepCopyAndActionsSeparate(string language, bool isDark)
    {
        var theme = isDark ? ThemeVariant.Dark : ThemeVariant.Light;
        using var variant = ThemeVariantSnapshot.Capture(theme);
        using var context = CreateContext();
        var window = context.Window;
        var sizes = new[] { (Width: 1300, Height: 754), (Width: 1024, Height: 640) };
        window.Width = sizes[0].Width;
        window.Height = sizes[0].Height;
        context.ViewModel.Shell.ApplyLanguage(language, hasSnapshot: false);
        context.Provider.GetRequiredService<ThemeApplier>().ApplyScheme(Color.Parse("#2E7DF6"), isDark: isDark);
        OpenSettings(context);
        Assert.Equal(theme, window.ActualThemeVariant);
        var overlay = window.GetVisualDescendants().OfType<MainWindowSettingsOverlay>().Single();
        foreach (var (category, sectionType) in SettingsSections)
        {
            context.ViewModel.Settings.SelectedCategory = category;
            Dispatcher.UIThread.RunJobs();
            var section = Assert.Single(overlay.GetVisualDescendants().OfType<UserControl>(),
                control => control.GetType() == sectionType && control.IsEffectivelyVisible);

            // Keep the section realized while exercising every font size and window size.
            foreach (var scale in new[] { 1d, 1.25, 1.5 })
            {
                using var typography = new TypographyScaleSnapshot(window, scale);
                foreach (var (width, height) in sizes)
                {
                    window.Width = width;
                    window.Height = height;
                    Dispatcher.UIThread.RunJobs();
                    Assert.Equal(new Size(width, height), window.ClientSize);
                    AssertSettingsLayout(overlay, section, scale,
                        $"{category}, {width}×{height}, scale {scale}");
                }

                // Start the next scale at the current size so applying it needs no extra resize.
                Array.Reverse(sizes);
            }
        }
    }

    private static void AssertSettingsLayout(MainWindowSettingsOverlay overlay, UserControl section, double scale, string scenario)
    {
        var footerActions = overlay.GetVisualDescendants().OfType<Button>()
            .Where(button => button.IsEffectivelyVisible && button.Classes.Contains("dialog-action"))
            .ToArray();
        Assert.Equal(2, footerActions.Length);
        Assert.Equal(footerActions[0].Bounds.Height, footerActions[1].Bounds.Height);
        foreach (var row in section.GetVisualDescendants().OfType<SettingRow>().Where(row => row.IsEffectivelyVisible))
        {
            var title = row.FindControl<TextBlock>("RowTitle")!;
            var action = row.FindControl<ContentPresenter>("ActionPresenter")!;
            Assert.Equal(14 * scale, title.FontSize);
            var copy = title.GetVisualAncestors().OfType<StackPanel>().First();
            Assert.True(copy.Bounds.Width > 0, $"{scenario}: {row.Title}");
            Assert.True(action.Bounds.Right <= row.Bounds.Width + 1, $"{scenario}: {row.Title} action overflows");
            foreach (var control in action.GetVisualDescendants().OfType<Control>().Where(control =>
                         control.IsEffectivelyVisible && control is Button or ComboBox or TextBox or ToggleSwitch))
            {
                var point = control.TranslatePoint(default, row);
                Assert.NotNull(point);
                Assert.True(point.Value.X >= -1 && point.Value.X + control.Bounds.Width <= row.Bounds.Width + 1,
                    $"{scenario}: {row.Title} {control.GetType().Name} overflows");
            }
            Assert.True(
                Grid.GetRow(action) == 1 ? action.Bounds.Top >= copy.Bounds.Bottom : action.Bounds.Left >= copy.Bounds.Right,
                $"{scenario}: {row.Title} overlaps");
            foreach (var text in copy.Children.OfType<TextBlock>().Where(text => text.IsVisible))
            {
                Assert.True(text.TextLayout.Height <= text.Bounds.Height + 1, $"{scenario}: {text.Text} is clipped");
            }
        }
    }

    [AvaloniaFact]
    public void Golden_SettingsDarkChineseScaled_MatchesBaseline()
    {
        using var variant = ThemeVariantSnapshot.Capture(ThemeVariant.Dark);
        using var context = CreateContext();
        PrepareGoldenWindow(context, ThemeVariant.Dark);
        context.Window.Width = 1024;
        context.Window.Height = 640;
        context.ViewModel.Shell.ApplyLanguage(LauncherLanguages.SimplifiedChinese, hasSnapshot: false);
        context.Provider.GetRequiredService<ThemeApplier>().ApplyScheme(Color.Parse("#2E7DF6"), isDark: true);
        OpenSettings(context);
        using var typography = new TypographyScaleSnapshot(context.Window, 1.5);
        Dispatcher.UIThread.RunJobs();
        GoldenScreenshot.Compare(context.Window, "settings-dark-zh-scaled");
    }

    [AvaloniaFact]
    public void Golden_SettingsAppearanceDynamicDarkScaled_MatchesBaseline()
    {
        using var variant = ThemeVariantSnapshot.Capture(ThemeVariant.Dark);
        using var context = CreateContext();
        PrepareGoldenWindow(context, ThemeVariant.Dark);
        context.Window.Width = 1024;
        context.Window.Height = 640;
        OpenSettings(context);
        context.ViewModel.Settings.SelectedCategory = SettingsCategoryCodes.Appearance;
        context.ViewModel.Settings.Editor.Current.ThemeMode = ThemeModes.Dark;
        context.ViewModel.Settings.Appearance.SelectedCustomThemeColor = Color.Parse("#00FF00");
        context.ViewModel.Settings.Editor.Current.ThemeColorMode = ThemeColorModes.Custom;
        context.ViewModel.Settings.Editor.Current.ThemeColorVariant = ThemeColorVariants.Fidelity;
        context.ViewModel.Settings.Editor.Current.NeutralColorStrategy = NeutralColorStrategies.SeedFollowing;
        context.Provider.GetRequiredService<ThemeApplier>().ApplyScheme(Color.Parse("#00FF00"), ThemeColorVariants.Fidelity,
            isDark: true, neutralStrategy: NeutralColorStrategies.SeedFollowing);
        Dispatcher.UIThread.RunJobs();
        using var typography = new TypographyScaleSnapshot(context.Window, 1.5);
        Dispatcher.UIThread.RunJobs();
        Assert.All(context.Window.GetVisualDescendants().OfType<SettingRow>().Where(row => row.IsEffectivelyVisible),
            row => Assert.Equal(21, row.FindControl<TextBlock>("RowTitle")!.FontSize));
        GoldenScreenshot.Compare(context.Window, "settings-appearance-dynamic-dark-scaled");
    }

    [AvaloniaFact]
    public void Visual_UninstallScopeDetail_LongCopyWrapsWithoutClipping()
    {
        using var context = CreateContext();
        PrepareUninstallGolden(context);
        Dispatcher.UIThread.RunJobs();
        var detail = context.Window.GetVisualDescendants().OfType<TextBlock>()
            .Single(text => text.Text == context.ViewModel.Operations.Uninstall.ScopeDetailText);
        Assert.Equal(TextWrapping.Wrap, detail.TextWrapping);
        Assert.True(detail.TextLayout.TextLines.Count > 1);
        Assert.True(detail.TextLayout.Width <= detail.Bounds.Width + 1);
        Assert.True(detail.TextLayout.Height <= detail.Bounds.Height + 1);
    }

    [AvaloniaTheory]
    [InlineData("install", 1d)]
    [InlineData("install", 1.5)]
    [InlineData("control", 1d)]
    [InlineData("control", 1.5)]
    [InlineData("progress", 1d)]
    [InlineData("progress", 1.5)]
    public void Visual_LoadedOperationMetadata_StaysInsideWindow(string mode, double scale)
    {
        using var context = CreateContext();
        context.Window.Width = scale > 1 ? 1024 : 1300;
        context.Window.Height = scale > 1 ? 640 : 754;
        context.ViewModel.IsMotionReduced = true;
        context.ViewModel.Operations.PanelMode = mode switch
        {
            "install" => GameOperationPanelMode.Install,
            "control" => GameOperationPanelMode.Control,
            "progress" => GameOperationPanelMode.Progress,
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
        context.ViewModel.Shell.DiskSpaceText = "Required 38.5 GB / Available 128.6 GB";
        context.ViewModel.Shell.NetworkText = "Network: connected (automatic system proxy)";
        context.ViewModel.Shell.DownloadSourceText = "Download source: Official";
        context.ViewModel.Shell.VersionText = "Version: 1.92.382472";
        context.ViewModel.Shell.LaunchCheckText = "Launch verification: local manifest";
        context.ViewModel.Shell.PathText = @"D:\Games\BlueArchive_JP";
        context.ViewModel.Operations.ProgressTitle = "Downloading";
        context.ViewModel.Operations.ProgressDetail = "Processed files: 19000 / 38472";
        context.ViewModel.Operations.ProgressSpeed = "Download speed: 24.8 MB/s";
        context.ViewModel.Operations.ProgressSize = "Downloaded: 18.5 GB / 38.5 GB";
        context.ViewModel.Operations.ProgressEstimated = "Time remaining: 2 hours 15 minutes";
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();
        using var typography = new TypographyScaleSnapshot(context.Window, scale);
        Dispatcher.UIThread.RunJobs();
        var samples = new[]
        {
            context.ViewModel.Shell.DiskSpaceText, context.ViewModel.Shell.NetworkText,
            context.ViewModel.Shell.DownloadSourceText, context.ViewModel.Shell.VersionText,
            context.ViewModel.Shell.LaunchCheckText, context.ViewModel.Operations.ProgressDetail,
            context.ViewModel.Operations.ProgressSpeed, context.ViewModel.Operations.ProgressSize,
            context.ViewModel.Operations.ProgressEstimated
        };
        var metadata = context.Window.GetVisualDescendants().OfType<TextBlock>()
            .Where(text => text.IsEffectivelyVisible && samples.Contains(text.Text)).ToArray();
        Assert.NotEmpty(metadata);
        foreach (var text in metadata)
        {
            AssertControlInsideWindow(text, context.Window);
            Assert.True(text.TextLayout.Width <= text.Bounds.Width + 1, $"{mode}: {text.Text} is clipped");
        }
    }
}
