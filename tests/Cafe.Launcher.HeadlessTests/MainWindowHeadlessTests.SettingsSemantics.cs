using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
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
using Cafe.Launcher.UI.Controls;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Cafe.Launcher.HeadlessTests;

public sealed partial class MainWindowHeadlessTests
{
    [AvaloniaTheory]
    [InlineData(SettingsCategoryCodes.General, LocalizationKeys.RememberWindowPositionAndSize)]
    [InlineData(SettingsCategoryCodes.DownloadNetwork, LocalizationKeys.EnableHttp2)]
    [InlineData(SettingsCategoryCodes.Advanced, LocalizationKeys.AllowBlockedNetworkAddresses)]
    public void DeferredSettings_CheckboxChangesDraftAndDiscardRestoresSavedValue(string category, LocalizationKeys key)
    {
        using var context = CreateContext();
        OpenSettings(context);
        context.ViewModel.Settings.SelectedCategory = category;
        Dispatcher.UIThread.RunJobs();
        var box = context.Window.GetVisualDescendants().OfType<CheckBox>().Single(control =>
            control.IsEffectivelyVisible && AutomationProperties.GetName(control) == context.ViewModel.Shell.I18n[key]);
        var before = context.ViewModel.Settings.Editor.GetSavedSnapshot();
        var original = box.IsChecked;
        box.IsChecked = !original;
        Assert.True(context.ViewModel.Settings.IsSettingsDirty);
        Assert.True(before.HasSameSettingsState(context.ViewModel.Settings.Editor.GetSavedSnapshot()));
        context.ViewModel.WindowChrome.ToggleSettingsCommand.Execute(null);
        Assert.True(context.ViewModel.Settings.IsUnsavedChangesVisible);
        context.ViewModel.WindowChrome.DiscardSettingsChangesCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(original, box.IsChecked);
    }

    [AvaloniaFact]
    public void GameMaintenance_WhilePathIsEdited_KeepsAppliedTargetAndDisablesActions()
    {
        using var context = CreateContext();
        context.ViewModel.Operations.ApplySnapshot(new LauncherStatusSnapshot
        {
            LocalGame = new LocalInstallationState { GamePath = @"D:\Games\BlueArchive_JP" },
            RuntimeState = LauncherRuntimeState.Ready
        });
        context.ViewModel.Shell.IsBusy = false;
        OpenSettings(context);
        context.ViewModel.Settings.SelectedCategory = SettingsCategoryCodes.Game;
        context.ViewModel.Settings.Editor.Current.GamePath = @"E:\Different\BlueArchive_JP";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(@"D:\Games\BlueArchive_JP", context.ViewModel.Operations.GameManagementPath);
        Assert.True(context.ViewModel.Settings.HasGamePathChanges);
        var buttons = context.Window.GetVisualDescendants().OfType<Button>().Where(button =>
            button.IsEffectivelyVisible && (ReferenceEquals(button.Command, context.ViewModel.Operations.OpenGameFolderCommand)
                || ReferenceEquals(button.Command, context.ViewModel.Operations.RequestRepairCommand)
                || ReferenceEquals(button.Command, context.ViewModel.Operations.RequestUninstallCommand))).ToArray();
        Assert.Equal(3, buttons.Length);
        Assert.All(buttons, button => Assert.False(button.IsEffectivelyEnabled));
        context.ViewModel.Settings.Editor.Current.GamePath = context.ViewModel.Settings.Editor.GetSavedSnapshot().GamePath;
        Dispatcher.UIThread.RunJobs();
        Assert.False(context.ViewModel.Settings.HasGamePathChanges);
        Assert.All(buttons, button => Assert.True(button.IsEffectivelyEnabled));
    }

    [AvaloniaFact]
    public void ResourcePanel_EscapeWithChanges_OpensTopConfirmationAndSecondEscapeKeepsEditing()
    {
        using var context = CreateContext();
        PrepareGoldenWindow(context);
        context.Window.Show();
        ShowResourcePanel(context);
        context.ViewModel.ResourcePanel.ResourcePanelItems[0].IsEnabled = true;
        Assert.True(context.ViewModel.TryHandleEscape());
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(ModalKind.ResourcePanelDiscardConfirmation, context.ViewModel.ModalHost.Top?.Kind);
        Assert.False(context.ViewModel.ModalHost.IsResourcePanelInteractive);
        Assert.True(context.ViewModel.TryHandleEscape());
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(ModalKind.ResourcePanel, context.ViewModel.ModalHost.Top?.Kind);
        Assert.True(context.ViewModel.ResourcePanel.ResourcePanelItems[0].IsEnabled);
        Assert.True(context.ViewModel.ResourcePanel.HasUnsavedChanges);
    }

    [AvaloniaFact]
    public async System.Threading.Tasks.Task ThemePalette_RadioSelection_ExposesSelectionAndSupportsKeyboard()
    {
        using var context = CreateContext();
        OpenSettings(context);
        context.ViewModel.Settings.SelectedCategory = SettingsCategoryCodes.Appearance;
        context.ViewModel.Settings.Editor.Current.ThemeColorMode = ThemeColorModes.Wallpaper;
        await context.ViewModel.Settings.PendingAppearancePreview.WaitAsync(TimeSpan.FromSeconds(5));
        await context.ViewModel.Settings.Appearance.PendingThemeRefresh.WaitAsync(TimeSpan.FromSeconds(5));
        Dispatcher.UIThread.RunJobs();
        var choices = context.Window.GetVisualDescendants().OfType<RadioButton>()
            .Where(control => control.Classes.Contains("color-swatch-button")).ToArray();
        Assert.True(context.ViewModel.Settings.Appearance.ThemeColorPaletteItems.Count >= 2);
        Assert.True(context.ViewModel.Settings.Appearance.IsThemeColorPaletteVisible);
        Assert.Equal(context.ViewModel.Settings.Appearance.ThemeColorPaletteItems.Count, choices.Length);
        Assert.True(choices[0].IsChecked);
        Assert.Equal(AutomationControlType.RadioButton, ControlAutomationPeer.CreatePeerForElement(choices[0])!.GetAutomationControlType());
        Assert.Null(choices[0].FocusAdorner);
        choices[0].Focus(NavigationMethod.Tab);
        context.Window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, "");
        Dispatcher.UIThread.RunJobs();
        Assert.True(choices[1].IsChecked);
        Assert.False(choices[0].IsChecked);
        Assert.Equal(1, context.ViewModel.Settings.Appearance.SelectedThemeColorPaletteIndex);
        foreach (var (key, physical, expected) in new[]
        {
            (Key.Left, PhysicalKey.ArrowLeft, 0),
            (Key.Left, PhysicalKey.ArrowLeft, choices.Length - 1),
            (Key.Home, PhysicalKey.Home, 0),
            (Key.End, PhysicalKey.End, choices.Length - 1),
            (Key.Up, PhysicalKey.ArrowUp, choices.Length - 2),
            (Key.Down, PhysicalKey.ArrowDown, choices.Length - 1),
            (Key.Down, PhysicalKey.ArrowDown, 0),
            (Key.F1, PhysicalKey.F1, 0)
        })
        {
            context.Window.KeyPress(key, RawInputModifiers.None, physical, "");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(expected, context.ViewModel.Settings.Appearance.SelectedThemeColorPaletteIndex);
            Assert.True(choices[expected].IsChecked);
            Assert.Single(choices, choice => choice.IsChecked == true);
        }
        var provider = Assert.IsAssignableFrom<ISelectionItemProvider>(
            ControlAutomationPeer.CreatePeerForElement(choices[1])!);
        provider.Select();
        Dispatcher.UIThread.RunJobs();
        Assert.True(provider.IsSelected);
        Assert.Equal(1, context.ViewModel.Settings.Appearance.SelectedThemeColorPaletteIndex);

    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void VolumeSlider_RenderedPartsFollowDynamicPrimaryAndKeepGeometryOnFocus(bool dark)
    {
        using var theme = ThemeVariantSnapshot.Capture(dark ? ThemeVariant.Dark : ThemeVariant.Light);
        using var context = CreateContext();
        PrepareGoldenWindow(context, dark ? ThemeVariant.Dark : ThemeVariant.Light);
        context.Provider.GetRequiredService<ThemeApplier>().ApplyScheme(Color.Parse("#A010C0"), isDark: dark);
        var slider = new Slider { Minimum = 0, Maximum = 100, Value = 35, Width = 220, Classes = { "setting-control" } };
        var window = new Window { Width = 400, Height = 150, Content = slider };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var thumb = Assert.Single(slider.GetVisualDescendants().OfType<Thumb>());
            var track = slider.GetVisualDescendants().OfType<RepeatButton>().Single(control => control.Name == "PART_DecreaseButton");
            var primary = window.FindResource(window.ActualThemeVariant, "Launcher.Color.Primary");
            Assert.Equal(primary, thumb.Background);
            Assert.Equal(primary, track.Background);
            var bounds = slider.Bounds;
            slider.Focus(NavigationMethod.Tab);
            window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, "");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(36, slider.Value);
            Assert.Equal(bounds, slider.Bounds);
            Assert.Equal(window.FindResource(window.ActualThemeVariant, "Launcher.Color.FocusRing"), slider.BorderBrush);
        }
        finally
        {
            window.Close();
        }
    }
}
