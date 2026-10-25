using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.UI.Controls;
using Cafe.Launcher.UI.Helpers;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Cafe.Launcher.HeadlessTests;

public sealed partial class MainWindowHeadlessTests
{
    public static TheoryData<string, double> SettingsHeaderCases
    {
        get
        {
            var cases = new TheoryData<string, double>();
            foreach (var language in new[] { LauncherLanguages.English, LauncherLanguages.SimplifiedChinese, LauncherLanguages.TraditionalChinese, LauncherLanguages.Japanese })
            {
                foreach (var scale in new[] { 1d, 1.5d, 2d })
                {
                    cases.Add(language, scale);
                }
            }

            return cases;
        }
    }

    [AvaloniaTheory]
    [MemberData(nameof(SettingsHeaderCases))]
    public void Visual_SettingsHeader_TitlesAndCloseButtonShareVerticalCenter(string language, double scale)
    {
        using var context = CreateContext();
        context.Window.Width = 1024;
        context.Window.Height = 640;
        context.ViewModel.Shell.ApplyLanguage(language, hasSnapshot: false);
        OpenSettings(context);
        var navigationHeader = context.Window.GetVisualDescendants().OfType<Border>()
            .Single(control => control.Classes.Contains("settings-navigation-header"));
        var settingsTitle = navigationHeader.GetVisualDescendants().OfType<TextBlock>()
            .Single(control => control.Classes.Contains("dialog-title"));
        var categoryTitle = context.Window.GetVisualDescendants().OfType<TextBlock>()
            .Single(control => control.Classes.Contains("category-title"));
        var close = context.Window.GetVisualDescendants().OfType<Button>()
            .Single(control => control.Classes.Contains("content-header-action"));
        foreach (var (category, _) in SettingsSections)
        {
            context.ViewModel.Settings.SelectedCategory = category;
            Dispatcher.UIThread.RunJobs();
            using var typography = new TypographyScaleSnapshot(context.Window, scale);
            Dispatcher.UIThread.RunJobs();
            var settingsCenter = settingsTitle.TranslatePoint(default, context.Window)!.Value.Y + settingsTitle.Bounds.Height / 2;
            var categoryCenter = categoryTitle.TranslatePoint(default, context.Window)!.Value.Y + categoryTitle.Bounds.Height / 2;
            var closeCenter = close.TranslatePoint(default, context.Window)!.Value.Y + close.Bounds.Height / 2;
            var geometry = $"{language}, {scale}, {category}: settings={settingsCenter}, category={categoryCenter}, close={closeCenter}";
            Assert.True(Math.Abs(settingsCenter - categoryCenter) <= 1, geometry);
            Assert.True(Math.Abs(categoryCenter - closeCenter) <= 1, geometry);
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

    public static TheoryData<string, int, int, double, bool> VisualSettingsCases
    {
        get
        {
            var cases = new TheoryData<string, int, int, double, bool>();
            foreach (var language in new[] { LauncherLanguages.English, LauncherLanguages.SimplifiedChinese, LauncherLanguages.TraditionalChinese, LauncherLanguages.Japanese })
            {
                foreach (var (width, height) in new[] { (1300, 754), (1024, 640) })
                {
                    foreach (var scale in new[] { 1d, 1.25, 1.5 })
                    {
                        cases.Add(language, width, height, scale, false);
                        cases.Add(language, width, height, scale, true);
                    }
                }
            }

            return cases;
        }
    }

    [AvaloniaTheory]
    [MemberData(nameof(VisualSettingsCases))]
    public void Visual_Settings_AllCategoriesKeepCopyAndActionsSeparate(string language, int width, int height, double scale, bool isDark)
    {
        using var variant = ThemeVariantSnapshot.Capture(isDark ? ThemeVariant.Dark : ThemeVariant.Light);
        using var context = CreateContext();
        context.Window.Width = width;
        context.Window.Height = height;
        context.ViewModel.Shell.ApplyLanguage(language, hasSnapshot: false);
        context.Provider.GetRequiredService<ThemeApplier>().ApplyScheme(Color.Parse("#2E7DF6"), isDark: isDark);
        OpenSettings(context);
        foreach (var (category, _) in SettingsSections)
        {
            context.ViewModel.Settings.SelectedCategory = category;
            Dispatcher.UIThread.RunJobs();
            using var typography = new TypographyScaleSnapshot(context.Window, scale);
            Dispatcher.UIThread.RunJobs();
            var footerActions = context.Window.GetVisualDescendants().OfType<Button>()
                .Where(button => button.IsEffectivelyVisible && button.Classes.Contains("dialog-action"))
                .ToArray();
            Assert.Equal(2, footerActions.Length);
            Assert.Equal(footerActions[0].Bounds.Height, footerActions[1].Bounds.Height);
            foreach (var row in context.Window.GetVisualDescendants().OfType<SettingRow>().Where(row => row.IsEffectivelyVisible))
            {
                var title = row.FindControl<TextBlock>("RowTitle")!;
                var action = row.FindControl<ContentPresenter>("ActionPresenter")!;
                Assert.Equal(14 * scale, title.FontSize);
                var copy = title.GetVisualAncestors().OfType<StackPanel>().First();
                Assert.True(copy.Bounds.Width > 0, $"{category}: {row.Title}");
                Assert.True(action.Bounds.Right <= row.Bounds.Width + 1, $"{category}: {row.Title} action overflows");
                foreach (var control in action.GetVisualDescendants().OfType<Control>().Where(control =>
                             control.IsEffectivelyVisible && control is Button or ComboBox or TextBox or ToggleSwitch))
                {
                    var point = control.TranslatePoint(default, row);
                    Assert.NotNull(point);
                    Assert.True(point.Value.X >= -1 && point.Value.X + control.Bounds.Width <= row.Bounds.Width + 1,
                        $"{category}: {row.Title} {control.GetType().Name} overflows");
                }
                Assert.True(
                    Grid.GetRow(action) == 1 ? action.Bounds.Top >= copy.Bounds.Bottom : action.Bounds.Left >= copy.Bounds.Right,
                    $"{category}: {row.Title} overlaps");
                foreach (var text in copy.Children.OfType<TextBlock>().Where(text => text.IsVisible))
                {
                    Assert.True(text.TextLayout.Height <= text.Bounds.Height + 1, $"{category}: {text.Text} is clipped");
                }
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
