using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Cafe.Launcher.UI.Services;
using Microsoft.Extensions.DependencyInjection;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.UI.Constants;
using Cafe.Launcher.UI.Models;

namespace Cafe.Launcher.HeadlessTests;

public sealed partial class MainWindowHeadlessTests
{
    [AvaloniaFact]
    public void Golden_SettingsAdvancedChinese_MatchesBaseline()
    {
        using var context = CreateContext();
        PrepareGoldenWindow(context);
        context.ViewModel.Shell.ApplyLanguage(LauncherLanguages.SimplifiedChinese, hasSnapshot: false);
        OpenSettings(context);
        context.ViewModel.Settings.SelectedCategory = SettingsCategoryCodes.Advanced;
        GoldenScreenshot.Compare(context.Window, "settings-advanced-zh");
    }

    [AvaloniaFact]
    public async Task Golden_SettingsSavingDarkScaled_MatchesBaseline()
    {
        using var theme = ThemeVariantSnapshot.Capture(ThemeVariant.Dark);
        using var context = CreateContext();
        PrepareGoldenWindow(context, ThemeVariant.Dark);
        context.Window.Width = 1024;
        context.Window.Height = 640;
        OpenSettings(context);
        context.ViewModel.Settings.Editor.Current.ThemeMode = ThemeModes.Dark;
        await context.ViewModel.Settings.PendingAppearancePreview.WaitAsync(TimeSpan.FromSeconds(5));
        context.Provider.GetRequiredService<ThemeApplier>().ApplyScheme(Color.Parse("#2E7DF6"), isDark: true);
        context.ViewModel.Settings.IsSaving = true;
        using var typography = new TypographyScaleSnapshot(context.Window, 1.5);
        GoldenScreenshot.Compare(context.Window, "settings-saving-dark-scaled");
    }

    [AvaloniaFact]
    public void Golden_SettingsMaintenancePendingChinese_MatchesBaseline()
    {
        using var context = CreateContext();
        PrepareGoldenWindow(context);
        context.ViewModel.Shell.ApplyLanguage(LauncherLanguages.SimplifiedChinese, hasSnapshot: false);
        context.ViewModel.Shell.IsBusy = false;
        context.ViewModel.Operations.ApplySnapshot(new LauncherStatusSnapshot
        {
            LocalGame = new LocalInstallationState { GamePath = @"D:\Games\BlueArchive_JP" },
            RuntimeState = LauncherRuntimeState.Ready
        });
        OpenSettings(context);
        context.ViewModel.Settings.SelectedCategory = SettingsCategoryCodes.Game;
        context.ViewModel.Settings.Editor.Current.GamePath = @"E:\NewGames\BlueArchive_JP";
        GoldenScreenshot.Compare(context.Window, "settings-maintenance-pending-zh");
    }

    [AvaloniaFact]
    public async Task Golden_ThemePaletteSelectionDarkChinese_MatchesBaseline()
    {
        using var theme = ThemeVariantSnapshot.Capture(ThemeVariant.Dark);
        using var context = CreateContext();
        PrepareGoldenWindow(context, ThemeVariant.Dark);
        context.ViewModel.Shell.ApplyLanguage(LauncherLanguages.SimplifiedChinese, hasSnapshot: false);
        OpenSettings(context);
        context.ViewModel.Settings.SelectedCategory = SettingsCategoryCodes.Appearance;
        context.ViewModel.Settings.Editor.Current.ThemeMode = ThemeModes.Dark;
        context.ViewModel.Settings.Editor.Current.ThemeColorMode = ThemeColorModes.Wallpaper;
        await context.ViewModel.Settings.PendingAppearancePreview.WaitAsync(TimeSpan.FromSeconds(5));
        await context.ViewModel.Settings.Appearance.PendingThemeRefresh.WaitAsync(TimeSpan.FromSeconds(5));
        context.ViewModel.Settings.Appearance.SelectedThemeColorPaletteIndex = 1;
        context.Provider.GetRequiredService<ThemeApplier>().ApplyScheme(Color.Parse("#2E7DF6"), isDark: true);
        Dispatcher.UIThread.RunJobs();
        var selected = context.Window.GetVisualDescendants().OfType<RadioButton>()
            .First(control => control.Classes.Contains("color-swatch-button") && control.IsChecked == true);
        selected.BringIntoView();
        selected.Focus(NavigationMethod.Tab);
        GoldenScreenshot.Compare(context.Window, "settings-palette-radio-dark-zh");
    }

    [AvaloniaFact]
    public void Golden_SettingsDownloadChinese_MatchesBaseline()
    {
        using var context = CreateContext();
        PrepareGoldenWindow(context);
        context.ViewModel.Shell.ApplyLanguage(LauncherLanguages.SimplifiedChinese, hasSnapshot: false);
        OpenSettings(context);
        context.ViewModel.Settings.SelectedCategory = SettingsCategoryCodes.DownloadNetwork;
        GoldenScreenshot.Compare(context.Window, "settings-download-zh");
    }

    [AvaloniaFact]
    public void Golden_SettingsAboutChinese_MatchesBaseline()
    {
        using var context = CreateContext();
        PrepareGoldenWindow(context);
        context.ViewModel.Shell.ApplyLanguage(LauncherLanguages.SimplifiedChinese, hasSnapshot: false);
        OpenSettings(context);
        context.ViewModel.Settings.SelectedCategory = SettingsCategoryCodes.About;
        GoldenScreenshot.Compare(context.Window, "settings-about-zh");
    }

    [AvaloniaFact]
    public void Golden_ResourcePreferencesAndDiscard_MatchBaselines()
    {
        using var context = CreateContext();
        PrepareGoldenWindow(context);
        context.ViewModel.Shell.ApplyLanguage(LauncherLanguages.SimplifiedChinese, hasSnapshot: false);
        var resource = context.ViewModel.ResourcePanel;
        resource.RefreshLocalizedText();
        resource.ResourcePanelUid = "UIDTESTA";
        foreach (var item in resource.ResourcePanelItems)
        {
            item.Status = ResourcePanelItemStatus.Ready;
            item.StatusIconKind = "CheckCircle";
            item.StatusText = context.ViewModel.Shell.I18n[LocalizationKeys.ResourcePanelReady];
            item.OfficialVersion = "1.2.0";
            item.LocalizedVersion = "1.2.0";
        }
        context.Window.Show();
        ShowResourcePanel(context);
        resource.ResourcePanelItems[0].IsEnabled = true;
        GoldenScreenshot.Compare(context.Window, "resource-preferences-unsaved");
        resource.CloseResourcePanelCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        GoldenScreenshot.Compare(context.Window, "resource-preferences-discard");
    }
}
