using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Features.SetupWizard;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.ViewModels;
using Cafe.Launcher.Avalonia.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Cafe.Launcher.Avalonia.HeadlessTests;

public sealed class MainWindowHeadlessTests
{
    private static readonly (string Code, Type SectionType)[] SettingsSections =
    [
        (SettingsCategoryCodes.General, typeof(SettingsGeneralSection)),
        (SettingsCategoryCodes.Game, typeof(SettingsGameSection)),
        (SettingsCategoryCodes.DownloadNetwork, typeof(SettingsDownloadNetworkSection)),
        (SettingsCategoryCodes.Appearance, typeof(SettingsAppearanceSection)),
        (SettingsCategoryCodes.Advanced, typeof(SettingsAdvancedSection)),
        (SettingsCategoryCodes.About, typeof(SettingsAboutSection))
    ];

    [AvaloniaFact]
    public void MainWindow_WhenShown_LoadsRealXamlAndOverlayBindings()
    {
        using var context = CreateContext();
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var settingsOverlay = context.Window
            .GetVisualDescendants()
            .OfType<Grid>()
            .Single(control => control.Classes.Contains("settings-overlay"));
        Assert.False(settingsOverlay.IsVisible);

        context.ViewModel.WindowChrome.IsSettingsVisible = true;
        Dispatcher.UIThread.RunJobs();

        Assert.True(settingsOverlay.IsVisible);
        Assert.Single(context.Window.GetVisualDescendants().OfType<MainWindowSettingsOverlay>());
        Assert.Single(context.Window.GetVisualDescendants().OfType<MainWindowDialogsOverlay>());
        Assert.Single(context.Window.GetVisualDescendants().OfType<MainWindowToastOverlay>());
    }

    [AvaloniaFact]
    public void SettingsWorkspace_WhenShown_AppliesWorkspaceAndStatusSummaryStyles()
    {
        using var context = CreateContext();
        context.ViewModel.WindowChrome.IsSettingsVisible = true;

        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var workspace = context.Window
            .GetVisualDescendants()
            .OfType<Grid>()
            .Single(control => control.Classes.Contains("settings-workspace"));
        var statusSummary = context.Window
            .GetVisualDescendants()
            .OfType<Border>()
            .Single(control => control.Classes.Contains("settings-status-summary"));

        Assert.Equal(new Thickness(0), workspace.Margin);
        Assert.Equal(new Thickness(0, 0, 0, 8), statusSummary.Padding);
    }

    [AvaloniaFact]
    public void SettingsStatusSummary_WhenShown_UsesUniformThirtyTwoPixelElements()
    {
        using var context = CreateContext();
        OpenSettings(context);

        var statusSummary = context.Window
            .GetVisualDescendants()
            .OfType<Border>()
            .Single(control => control.Classes.Contains("settings-status-summary"));
        var statusIcon = statusSummary
            .GetVisualDescendants()
            .OfType<Border>()
            .Single(control => control.Classes.Contains("settings-icon"));
        var statusDetails = statusSummary
            .GetVisualDescendants()
            .OfType<Border>()
            .Where(control => control.Classes.Contains("status-detail"))
            .ToArray();

        Assert.Equal(32, statusIcon.Bounds.Height);
        Assert.Equal(2, statusDetails.Length);
        Assert.All(statusDetails, detail => Assert.Equal(32, detail.Bounds.Height));
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
            context.ViewModel.Settings,
            context.ViewModel.ResourcePanel,
            hasSnapshot: false);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Microsoft JhengHei UI", context.Window.FontFamily.Name);
        Assert.Equal("Microsoft JhengHei UI", visibleText.FontFamily.Name);

        context.ViewModel.Shell.ApplyLanguage(
            LauncherLanguages.Japanese,
            context.ViewModel.Settings,
            context.ViewModel.ResourcePanel,
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
    public void SettingsNavigation_WhenOpened_KeepsGeneralItemVisuallySelectedWithoutFocus()
    {
        using var context = CreateContext();
        OpenSettings(context);

        var navigation = GetSettingsNavigation(context.Window);
        Assert.Equal(SettingsCategoryCodes.General, context.ViewModel.Settings.SelectedCategory);
        Assert.False(navigation.IsKeyboardFocusWithin);
        AssertNavigationSelectionVisual(navigation, SettingsCategoryCodes.General);
    }

    [AvaloniaFact]
    public async Task SettingsNavigation_AfterSave_KeepsSelectedItemVisuallySelectedWithoutFocus()
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
        var savedCount = 0;
        context.ViewModel.Settings.SettingsSaved += () =>
        {
            savedCount++;
            return Task.CompletedTask;
        };
        context.ViewModel.Settings.Editor.Current.Language = LauncherLanguages.Japanese;
        Assert.True(context.ViewModel.Settings.IsSettingsDirty);

        context.ViewModel.Settings.SelectedCategory = SettingsCategoryCodes.Appearance;
        context.ViewModel.Settings.SelectedCategory = SettingsCategoryCodes.General;

        Assert.Equal(LauncherLanguages.Japanese, context.ViewModel.Settings.Editor.Current.Language);
        Assert.True(context.ViewModel.Settings.IsSettingsDirty);
        Assert.Equal(0, savedCount);
    }

    [AvaloniaFact]
    public void SettingsCategory_IsSessionScopedAndNewViewModelDefaultsToGeneral()
    {
        using var context = CreateContext();
        OpenSettings(context);
        context.ViewModel.Settings.SelectedCategory = SettingsCategoryCodes.DownloadNetwork;
        context.ViewModel.WindowChrome.ShowSettingsCommand.Execute(null);
        context.ViewModel.WindowChrome.ShowSettingsCommand.Execute(null);

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

        var rows = context.Window.GetVisualDescendants().OfType<global::Cafe.Launcher.Avalonia.Controls.SettingRow>().ToArray();
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

    [AvaloniaFact]
    public void SettingsSaving_DisablesNavigationButKeepsSummaryAndFooterVisible()
    {
        using var context = CreateContext();
        OpenSettings(context);
        var navigation = GetSettingsNavigation(context.Window);
        var summary = context.Window.GetVisualDescendants().OfType<Border>()
            .Single(control => control.Classes.Contains("settings-status-summary"));
        var settingsOverlay = context.Window.GetVisualDescendants()
            .OfType<MainWindowSettingsOverlay>().Single();
        var footer = settingsOverlay.GetVisualDescendants().OfType<Border>()
            .Single(control => control.Classes.Contains("dialog-footer"));

        context.ViewModel.Settings.IsSaving = true;
        Dispatcher.UIThread.RunJobs();

        Assert.False(navigation.IsEnabled);
        Assert.True(summary.IsEffectivelyVisible);
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
            .OfType<Border>()
            .Single(control => control.Classes.Contains("overlay-dialog"));
        var footer = dialog
            .GetVisualDescendants()
            .OfType<Border>()
            .Single(control => control.Classes.Contains("dialog-footer"));

        Assert.True(dialog.Bounds.Width <= context.Window.ClientSize.Width - 48);
        Assert.True(dialog.Bounds.Height <= context.Window.ClientSize.Height - 48);
        Assert.True(dialog.Bounds.Height > 0);
        Assert.True(footer.IsEffectivelyVisible);
        Assert.True(footer.Bounds.Bottom <= dialog.Bounds.Height);
    }

    [AvaloniaTheory]
    [InlineData("install")]
    [InlineData("progress")]
    [InlineData("control")]
    public void MainWindow_AtMinimumWindowSize_RemoteContentDoesNotOverlapOperationPanel(
        string panelMode)
    {
        using var context = CreateContext();
        context.Window.Width = 1024;
        context.Window.Height = 640;
        context.ViewModel.RemoteContent.IsPanelVisible = true;
        context.ViewModel.Operations.PanelMode = panelMode switch
        {
            "install" => GameOperationPanelMode.Install,
            "progress" => GameOperationPanelMode.Progress,
            "control" => GameOperationPanelMode.Control,
            _ => throw new ArgumentOutOfRangeException(nameof(panelMode)),
        };
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var remotePanel = context.Window.GetVisualDescendants().OfType<Border>()
            .Single(control => control.Classes.Contains("remote-surface"));
        var operationPanelClass = panelMode == "control" ? "control-panel" : "bottom-panel";
        var operationPanel = context.Window.GetVisualDescendants().OfType<Border>()
            .Single(control => control.Classes.Contains(operationPanelClass)
                && control.IsEffectivelyVisible);

        Assert.True(remotePanel.IsEffectivelyVisible);
        Assert.True(remotePanel.Bounds.Bottom <= operationPanel.Bounds.Top);
    }

    [AvaloniaTheory]
    [InlineData("install", 4, 1)]
    [InlineData("progress", 2, 0)]
    [InlineData("control", 2, 1)]
    public void MainWindow_AtMinimumWindowSize_KeepsOperationStatusAndActionsInsideWindow(
        string panelMode,
        int expectedActionCount,
        int expectedPrimaryActionCount)
    {
        using var context = CreateContext();
        context.Window.Width = 1024;
        context.Window.Height = 640;
        context.ViewModel.Operations.PanelMode = panelMode switch
        {
            "install" => GameOperationPanelMode.Install,
            "progress" => GameOperationPanelMode.Progress,
            "control" => GameOperationPanelMode.Control,
            _ => throw new ArgumentOutOfRangeException(nameof(panelMode)),
        };
        context.ViewModel.Operations.CanPauseOperation = panelMode == "progress";
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var layout = context.Window
            .GetVisualDescendants()
            .OfType<Grid>()
            .Single(control =>
                control.Classes.Contains("operation-layout")
                && control.IsEffectivelyVisible);
        var title = layout
            .GetVisualDescendants()
            .OfType<TextBlock>()
            .Single(control => control.Classes.Contains("operation-status-title"));
        var actions = layout
            .GetVisualDescendants()
            .OfType<Button>()
            .Where(control =>
                control.Classes.Contains("primary-operation")
                || control.Classes.Contains("secondary-operation"))
            .ToArray();

        Assert.True(title.IsEffectivelyVisible);
        AssertControlInsideWindow(title, context.Window);
        Assert.Equal(expectedActionCount, actions.Length);
        Assert.Equal(
            expectedPrimaryActionCount,
            actions.Count(control => control.Classes.Contains("primary-operation")));
        Assert.All(actions, control =>
        {
            Assert.True(control.IsEffectivelyVisible);
            AssertControlInsideWindow(control, context.Window);
        });
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
    public void SettingsContent_ShowsLocalizedTitleForCurrentCategory()
    {
        using var context = CreateContext();
        OpenSettings(context);
        var title = context.Window.GetVisualDescendants().OfType<TextBlock>()
            .Single(control => control.Classes.Contains("category-title"));

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

        Assert.True(generalSection.IsKeyboardFocusWithin);
    }

    [AvaloniaFact]
    public void MainWindow_SettingsButtonHasBoundCommandAndAutomationName()
    {
        using var context = CreateContext();
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();
        var button = context.Window
            .GetVisualDescendants()
            .OfType<Button>()
            .Single(control => control.Classes.Contains("settings"));

        Assert.Same(context.ViewModel.WindowChrome.ShowSettingsCommand, button.Command);
        Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)));

        button.Command!.Execute(button.CommandParameter);
        Dispatcher.UIThread.RunJobs();

        Assert.True(context.ViewModel.WindowChrome.IsSettingsVisible);
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
                        context.ViewModel.WindowChrome.ShowSettingsCommand)))
            .ToArray();
        Assert.Equal(2, footerButtons.Length);
        var save = footerButtons.Single(button =>
            ReferenceEquals(button.Command, context.ViewModel.Settings.SaveSettingsCommand));
        var cancel = footerButtons.Single(button =>
            ReferenceEquals(button.Command, context.ViewModel.WindowChrome.ShowSettingsCommand));

        Assert.True(save.IsEnabled);
        cancel.Command!.Execute(cancel.CommandParameter);
        Dispatcher.UIThread.RunJobs();

        Assert.True(context.ViewModel.WindowChrome.IsSettingsVisible);
        Assert.True(context.ViewModel.Settings.IsUnsavedChangesVisible);
    }

    [AvaloniaFact]
    public void DialogOverlay_WhenRepairIsRequested_BecomesVisible()
    {
        using var context = CreateContext();
        context.Window.Show();
        context.ViewModel.Dialogs.ShowRepairConfirm("repair confirmation");
        Dispatcher.UIThread.RunJobs();

        Assert.Contains(
            context.Window.GetVisualDescendants().OfType<Grid>(),
            grid => grid.Classes.Contains("dialog-overlay")
                && grid.IsEffectivelyVisible);
        Assert.Contains(
            context.Window.GetVisualDescendants().OfType<TextBlock>(),
            text => text.Text == "repair confirmation");
    }

    [AvaloniaFact]
    public void RepairConfirm_WithLongMessage_DoesNotExceedDefaultMaximumWidth()
    {
        using var context = CreateContext();
        context.Window.Show();
        context.ViewModel.Dialogs.ShowRepairConfirm(
            "下载源已切换。Cafe 下载源与官方下载源使用不同的文件清单，因此必须根据当前下载源修复已安装的游戏，才能得到可靠的启动校验结果。现在开始修复吗？");
        Dispatcher.UIThread.RunJobs();

        var dialog = context.Window
            .GetVisualDescendants()
            .OfType<global::Cafe.Launcher.Avalonia.Controls.ConfirmDialog>()
            .Single(control => control.IsOpen);
        var panel = dialog
            .GetVisualDescendants()
            .OfType<Border>()
            .Single(control => control.Classes.Contains("confirm-panel"));

        Assert.True(panel.Bounds.Width <= 540);
    }

    [AvaloniaFact]
    public void MainWindow_WhenEscapeRouteIsInvoked_ClosesSettingsOverlay()
    {
        using var context = CreateContext();
        context.Window.Show();
        context.ViewModel.Dialogs.IsDownloadRunningCloseConfirmVisible = false;
        context.ViewModel.Dialogs.IsStopConfirmVisible = false;
        context.ViewModel.Settings.IsUnsavedChangesVisible = false;
        context.ViewModel.Dialogs.IsRepairConfirmVisible = false;
        context.ViewModel.Dialogs.IsResourcePanelSourceConfirmVisible = false;
        context.ViewModel.Dialogs.IsUninstallConfirmVisible = false;
        context.ViewModel.Dialogs.IsNoticeDialogVisible = false;
        context.ViewModel.ResourcePanel.IsResourcePanelVisible = false;
        context.ViewModel.Settings.Editor.ApplySnapshot(
            context.ViewModel.Settings.Editor.GetSnapshot());
        context.ViewModel.WindowChrome.IsSettingsVisible = true;
        Dispatcher.UIThread.RunJobs();

        var handled = context.ViewModel.TryHandleEscape();
        Dispatcher.UIThread.RunJobs();

        Assert.True(handled);
        Assert.False(context.ViewModel.WindowChrome.IsSettingsVisible);
    }

    [AvaloniaFact]
    public void SettingsOverlay_WhenOpenedWithoutChanges_RemainsClean()
    {
        using var context = CreateContext();
        context.Window.Show();
        var changedProperties = new List<string>();
        context.ViewModel.Settings.Editor.CurrentPropertyChanged += (_, args) =>
            changedProperties.Add(args.PropertyName ?? "<null>");

        context.ViewModel.WindowChrome.ShowSettingsCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.True(context.ViewModel.WindowChrome.IsSettingsVisible);
        Assert.False(
            context.ViewModel.Settings.IsSettingsDirty,
            string.Join(", ", changedProperties));
    }

    [AvaloniaFact]
    public void MainWindow_CloseCommand_WhenConfiguredToMinimize_UsesWindowFallback()
    {
        using var context = CreateContext();
        context.ViewModel.Settings.Editor.ApplySnapshot(new LauncherSettings
        {
            CloseBehavior = CloseBehaviors.Minimize
        });
        context.Window.Show();

        context.ViewModel.WindowChrome.CloseCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(WindowState.Minimized, context.Window.WindowState);
    }

    [AvaloniaFact]
    public void MainWindow_CloseCommand_WhenTrayIsConfigured_HidesWindow()
    {
        using var context = CreateContext();
        context.ViewModel.Settings.Editor.ApplySnapshot(new LauncherSettings
        {
            CloseBehavior = CloseBehaviors.Minimize
        });
        using var trayService = new SystemTrayService(
            context.Window,
            new LocalizationService(),
            new TestTrayPlatform());
        context.Window.SetSystemTray(trayService);
        context.Window.Show();

        context.ViewModel.WindowChrome.CloseCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.False(context.Window.IsVisible);
        Assert.NotEqual(WindowState.Minimized, context.Window.WindowState);
    }

    [AvaloniaFact]
    public void MainWindow_WhenEscapeKeyIsPressed_ClosesSettingsOverlay()
    {
        using var context = CreateContext();
        OpenSettings(context);

        var isDirty = context.ViewModel.Settings.IsSettingsDirty;
        var isUnsaved = context.ViewModel.Settings.IsUnsavedChangesVisible;

        context.Window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "");
        context.Window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "");
        Dispatcher.UIThread.RunJobs();

        Assert.False(context.ViewModel.WindowChrome.IsSettingsVisible,
            $"IsDirty={isDirty} IsUnsaved={isUnsaved} IsVisible={context.ViewModel.WindowChrome.IsSettingsVisible}");
    }

    [AvaloniaFact]
    public void ConfigureViewModel_WhenCalledAgain_UnsubscribesPreviousViewModel()
    {
        using var first = CreateContext();
        using var second = CreateContext();
        first.Window.ConfigureViewModel(second.ViewModel);
        first.Window.WindowState = WindowState.Normal;

        first.ViewModel.WindowChrome.MinimizeCommand.Execute(null);
        Assert.Equal(WindowState.Normal, first.Window.WindowState);

        second.ViewModel.WindowChrome.MinimizeCommand.Execute(null);
        Assert.Equal(WindowState.Minimized, first.Window.WindowState);
    }

    [AvaloniaFact]
    public async Task SetupWizard_WhenGamePathStatusChanges_UpdatesStatusLineAndNextAvailability()
    {
        using var context = CreateContext();
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        // Simulate first-launch trigger (settings.json missing)
        context.ViewModel.Dialogs.ShowSetupWizard();
        Dispatcher.UIThread.RunJobs();

        Assert.True(context.ViewModel.Dialogs.IsSetupWizardVisible);
        Assert.True(context.ViewModel.Dialogs.SetupWizard.IsFirstStep);

        var installationBasePath = Path.Combine(context.TempDir, "available-installation");
        context.ViewModel.Dialogs.SetupWizard.GamePath = installationBasePath;

        // Step 0 → 1 detects only the preconfigured test path.
        context.ViewModel.Dialogs.SetupWizard.NextCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, context.ViewModel.Dialogs.SetupWizard.Step);
        Assert.Equal(installationBasePath, context.ViewModel.Dialogs.SetupWizard.GamePath);
        Assert.True(GetWizardGamePathStatus(context.Window).IsEffectivelyVisible);

        await WaitForGamePathStatusAsync(
            context.ViewModel.Dialogs.SetupWizard,
            SetupWizardGamePathStatus.AvailableForInstallation);
        Dispatcher.UIThread.RunJobs();

        var statusLine = GetWizardGamePathStatus(context.Window);
        var nextButton = GetWizardNextButton(context.Window, context.ViewModel);
        Assert.Equal(
            context.ViewModel.Shell.I18n.SetupWizardGamePathAvailable,
            statusLine.Text);
        Assert.True(context.ViewModel.Dialogs.SetupWizard.CanGoNext);
        Assert.True(nextButton.IsEnabled);

        var corruptedInstallationPath = new GameInstallationPath().NormalizeGamePath(
            Path.Combine(context.TempDir, "corrupted-installation"));
        Directory.CreateDirectory(corruptedInstallationPath);
        await File.WriteAllTextAsync(
            Path.Combine(corruptedInstallationPath, GamePaths.ManifestFileName),
            "{}");
        context.ViewModel.Dialogs.SetupWizard.GamePath = corruptedInstallationPath;
        await WaitForGamePathStatusAsync(
            context.ViewModel.Dialogs.SetupWizard,
            SetupWizardGamePathStatus.CorruptedInstallation);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(
            context.ViewModel.Shell.I18n.SetupWizardGamePathCorrupted,
            statusLine.Text);
        Assert.False(context.ViewModel.Dialogs.SetupWizard.CanGoNext);
        Assert.False(nextButton.IsEnabled);

        context.ViewModel.Dialogs.SetupWizard.NextCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, context.ViewModel.Dialogs.SetupWizard.Step);
    }

    [AvaloniaFact]
    public async Task SetupWizard_ReviewList_EditButtonsNavigateToTheirSteps()
    {
        using var context = CreateContext();
        var wizard = context.ViewModel.Dialogs.SetupWizard;
        wizard.GamePath = Path.Combine(context.TempDir, "available-installation");

        context.Window.Show();
        context.ViewModel.Dialogs.ShowSetupWizard();
        wizard.NextCommand.Execute(null);
        await WaitForGamePathStatusAsync(wizard, SetupWizardGamePathStatus.AvailableForInstallation);
        while (!wizard.IsLastStep)
        {
            wizard.NextCommand.Execute(null);
        }
        Dispatcher.UIThread.RunJobs();

        var editButtons = context.Window
            .GetVisualDescendants()
            .OfType<Button>()
            .Where(control => AutomationProperties.GetName(control)
                == context.ViewModel.Shell.I18n.SetupWizardEditStep)
            .ToArray();

        Assert.Equal(4, editButtons.Length);

        foreach (var (editButton, expectedStep) in editButtons.Zip([0, 2, 1, 3]))
        {
            new ButtonAutomationPeer(editButton).Invoke();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(expectedStep, wizard.Step);

            while (!wizard.IsLastStep)
            {
                if (wizard.IsStep1)
                {
                    await WaitForGamePathStatusAsync(
                        wizard,
                        SetupWizardGamePathStatus.AvailableForInstallation);
                }

                wizard.NextCommand.Execute(null);
                Dispatcher.UIThread.RunJobs();
            }
        }
    }

    [AvaloniaFact]
    public void SetupWizard_RadioChoices_KeepGroupsIndependent()
    {
        using var context = CreateContext();
        context.Window.Show();
        context.ViewModel.Dialogs.ShowSetupWizard();
        context.ViewModel.Dialogs.SetupWizard.Step = 2;
        Dispatcher.UIThread.RunJobs();

        var cafe = context.Window.GetVisualDescendants().OfType<RadioButton>().Single(control =>
            AutomationProperties.GetName(control) == context.ViewModel.Shell.I18n.DownloadSourceCafe);
        var official = context.Window.GetVisualDescendants().OfType<RadioButton>().Single(control =>
            AutomationProperties.GetName(control) == context.ViewModel.Shell.I18n.DownloadSourceOfficial);

        official.IsChecked = true;
        Dispatcher.UIThread.RunJobs();

        Assert.True(context.ViewModel.Dialogs.SetupWizard.IsPatchUrlGroupOfficial);
        Assert.False(context.ViewModel.Dialogs.SetupWizard.IsPatchUrlGroupCafe);
        Assert.False(cafe.IsChecked);
        Assert.True(official.IsChecked);

        context.ViewModel.Dialogs.SetupWizard.NextCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var auto = context.Window.GetVisualDescendants().OfType<RadioButton>().Single(control =>
            AutomationProperties.GetName(control) == context.ViewModel.Shell.I18n.ProxyAuto);
        var direct = context.Window.GetVisualDescendants().OfType<RadioButton>().Single(control =>
            AutomationProperties.GetName(control) == context.ViewModel.Shell.I18n.ProxyDirect);
        var system = context.Window.GetVisualDescendants().OfType<RadioButton>().Single(control =>
            AutomationProperties.GetName(control) == context.ViewModel.Shell.I18n.ProxySystem);

        direct.IsChecked = true;
        Dispatcher.UIThread.RunJobs();

        Assert.True(context.ViewModel.Dialogs.SetupWizard.IsProxyDirect);
        Assert.False(context.ViewModel.Dialogs.SetupWizard.IsProxySystem);
        Assert.True(context.ViewModel.Dialogs.SetupWizard.IsPatchUrlGroupOfficial);
        Assert.False(auto.IsChecked);
        Assert.True(direct.IsChecked);
        Assert.False(system.IsChecked);

        system.IsChecked = true;
        Dispatcher.UIThread.RunJobs();

        Assert.True(context.ViewModel.Dialogs.SetupWizard.IsProxySystem);
        Assert.False(context.ViewModel.Dialogs.SetupWizard.IsProxyAuto);
        Assert.False(context.ViewModel.Dialogs.SetupWizard.IsProxyDirect);
        Assert.True(system.IsChecked);
    }

    [AvaloniaTheory]
    [InlineData(LauncherLanguages.English)]
    [InlineData(LauncherLanguages.SimplifiedChinese)]
    [InlineData(LauncherLanguages.TraditionalChinese)]
    [InlineData(LauncherLanguages.Japanese)]
    public async Task SetupWizard_WhenLanguageChanges_LocalizesStatusLineAndKeepsNavigationAccessible(
        string language)
    {
        using var context = CreateContext();
        var installationBasePath = Path.Combine(context.TempDir, "available-installation");
        context.ViewModel.Dialogs.SetupWizard.GamePath = installationBasePath;
        context.Window.Show();
        context.ViewModel.Dialogs.ShowSetupWizard();
        context.ViewModel.Dialogs.SetupWizard.NextCommand.Execute(null);
        await WaitForGamePathStatusAsync(
            context.ViewModel.Dialogs.SetupWizard,
            SetupWizardGamePathStatus.AvailableForInstallation);

        context.ViewModel.Shell.ApplyLanguage(
            language,
            context.ViewModel.Settings,
            context.ViewModel.ResourcePanel,
            hasSnapshot: false);
        Dispatcher.UIThread.RunJobs();

        var statusLine = GetWizardGamePathStatus(context.Window);
        var navigation = context.Window.GetVisualDescendants().OfType<ListBox>()
            .Single(control => control.Classes.Contains("wizard-navigation"));

        Assert.Equal(
            context.ViewModel.Shell.I18n.SetupWizardGamePathAvailable,
            statusLine.Text);
        Assert.Equal(statusLine.Text, AutomationProperties.GetName(statusLine));
        Assert.Equal(
            context.ViewModel.Shell.I18n.SetupWizardStepTitle,
            AutomationProperties.GetName(navigation));
        Assert.All(
            navigation.GetVisualDescendants().OfType<TextBlock>()
                .Where(control => control.Classes.Contains("settings-navigation-item")),
            control => Assert.Equal(control.Text, AutomationProperties.GetName(control)));
    }

    [AvaloniaFact]
    public void SetupWizard_WhenEscapeIsPressed_RequiresExitConfirmation()
    {
        using var context = CreateContext();
        context.Window.Show();
        context.ViewModel.Dialogs.ShowSetupWizard();
        Dispatcher.UIThread.RunJobs();

        var firstHandled = context.ViewModel.TryHandleEscape();
        Dispatcher.UIThread.RunJobs();

        Assert.True(firstHandled);
        Assert.True(context.ViewModel.Dialogs.IsSetupWizardVisible);
        Assert.True(context.ViewModel.Dialogs.IsSetupWizardExitConfirmVisible);

        var secondHandled = context.ViewModel.TryHandleEscape();
        Dispatcher.UIThread.RunJobs();

        Assert.True(secondHandled);
        Assert.True(context.ViewModel.Dialogs.IsSetupWizardVisible);
        Assert.False(context.ViewModel.Dialogs.IsSetupWizardExitConfirmVisible);
    }

    [AvaloniaFact]
    public async Task SetupWizard_WhenExitIsConfirmed_AppliesSkipAndClosesWizard()
    {
        using var context = CreateContext();
        context.Window.Show();
        context.ViewModel.Dialogs.ShowSetupWizard();
        context.ViewModel.TryHandleEscape();
        Dispatcher.UIThread.RunJobs();

        await context.ViewModel.Dialogs.ConfirmSetupWizardExitCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        Assert.False(context.ViewModel.Dialogs.IsSetupWizardExitConfirmVisible);
        Assert.False(context.ViewModel.Dialogs.IsSetupWizardVisible);
    }

    [AvaloniaFact]
    public async Task SetupWizard_WhenSkipped_HidesOverlay()
    {
        using var context = CreateContext();
        LauncherSettings? applied = null;
        context.ViewModel.Dialogs.SetupWizard.SettingsApplied += settings =>
        {
            applied = settings;
            // Simulate the parent ViewModel's behavior: hide wizard on completion
            context.ViewModel.Dialogs.IsSetupWizardVisible = false;
            return Task.CompletedTask;
        };
        context.Window.Show();
        context.ViewModel.Dialogs.ShowSetupWizard();
        Dispatcher.UIThread.RunJobs();

        await context.ViewModel.Dialogs.SetupWizard.SkipCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        Assert.False(context.ViewModel.Dialogs.IsSetupWizardVisible);
        Assert.NotNull(applied);
        Assert.Equal("auto", applied!.Language);
    }

    [AvaloniaFact]
    public async Task SetupWizard_WhenCompleted_BuildsSettingsAndHidesOverlay()
    {
        using var context = CreateContext();
        LauncherSettings? applied = null;
        context.ViewModel.Dialogs.SetupWizard.SettingsApplied += settings =>
        {
            applied = settings;
            // Simulate the parent ViewModel's behavior: hide wizard on completion
            context.ViewModel.Dialogs.IsSetupWizardVisible = false;
            return Task.CompletedTask;
        };
        context.Window.Show();
        context.ViewModel.Dialogs.ShowSetupWizard();
        Dispatcher.UIThread.RunJobs();

        // Navigate to step 1 (GamePath) and set a path
        context.ViewModel.Dialogs.SetupWizard.NextCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        context.ViewModel.Dialogs.SetupWizard.GamePath = @"C:\Games\YostarGames\BlueArchive_JP";
        context.ViewModel.Dialogs.SetupWizard.NextCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        context.ViewModel.Dialogs.SetupWizard.NextCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        context.ViewModel.Dialogs.SetupWizard.NextCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.True(context.ViewModel.Dialogs.SetupWizard.IsLastStep);

        await context.ViewModel.Dialogs.SetupWizard.CompleteCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        Assert.False(context.ViewModel.Dialogs.IsSetupWizardVisible);
        Assert.NotNull(applied);
        Assert.Contains(@"BlueArchive_JP", applied!.GamePath);
    }

    private static TestContext CreateContext()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var services = new ServiceCollection();
        services.AddLauncherServices();
        services.AddSingleton(_ => new UnifiedLogger(Path.Combine(tempDir, "logs")));
        var provider = services.BuildServiceProvider();
        var viewModel = provider.GetRequiredService<MainWindowViewModel>();
        viewModel.Shell.ApplyLanguage(
            LauncherLanguages.English,
            viewModel.Settings,
            viewModel.ResourcePanel,
            hasSnapshot: false);
        viewModel.Settings.Editor.ApplySnapshot(
            viewModel.Settings.Editor.GetSnapshot());
        // Apply default theme accent brushes so navigation selection visual
        // matches the real app's initialization behavior.
        SettingsAppearanceViewModel.ApplyAccentBrushes(
            Color.Parse("#FF2E7DF6"));
        var window = new MainWindow { DataContext = viewModel };
        window.ConfigureViewModel(viewModel);
        return new TestContext(tempDir, provider, window, viewModel);
    }

    private static void OpenSettings(TestContext context)
    {
        context.Window.Show();
        context.ViewModel.WindowChrome.ShowSettingsCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
    }

    private static TextBlock GetWizardGamePathStatus(MainWindow window) =>
        window.GetVisualDescendants().OfType<TextBlock>().Single(control =>
            control.Classes.Contains("wizard-game-path-status"));

    private static Button GetWizardNextButton(MainWindow window, MainWindowViewModel viewModel) =>
        window.GetVisualDescendants().OfType<Button>().Single(control =>
            ReferenceEquals(control.Command, viewModel.Dialogs.SetupWizard.NextCommand));

    private static async Task WaitForGamePathStatusAsync(
        SetupWizardViewModel viewModel,
        SetupWizardGamePathStatus expectedStatus)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        PropertyChangedEventHandler? handler = null;
        handler = (_, args) =>
        {
            if (args.PropertyName == nameof(SetupWizardViewModel.GamePathStatus)
                && viewModel.GamePathStatus == expectedStatus)
            {
                completion.TrySetResult();
            }
        };
        viewModel.PropertyChanged += handler;
        try
        {
            if (viewModel.GamePathStatus == expectedStatus)
            {
                return;
            }

            await completion.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            viewModel.PropertyChanged -= handler;
        }
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
        Assert.Equal(new Thickness(3, 0, 0, 0), selectedItem.BorderThickness);
        Assert.Equal(Color.Parse("#FF2E7DF6"), Assert.IsType<SolidColorBrush>(selectedItem.BorderBrush).Color);
        Assert.Equal(Color.Parse("#302E7DF6"), Assert.IsType<SolidColorBrush>(selectedItem.Background).Color);
        Assert.Equal(Color.Parse("#302E7DF6"), Assert.IsType<SolidColorBrush>(presenter.Background).Color);
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

    private static void AssertControlInsideWindow(Control control, Window window)
    {
        var topLeft = control.TranslatePoint(default, window);
        Assert.NotNull(topLeft);
        Assert.True(control.Bounds.Width > 0);
        Assert.True(control.Bounds.Height > 0);
        Assert.True(topLeft.Value.X >= 0);
        Assert.True(topLeft.Value.Y >= 0);
        Assert.True(topLeft.Value.X + control.Bounds.Width <= window.ClientSize.Width);
        Assert.True(topLeft.Value.Y + control.Bounds.Height <= window.ClientSize.Height);
    }

    private sealed record TestContext(
        string TempDir,
        ServiceProvider Provider,
        MainWindow Window,
        MainWindowViewModel ViewModel) : IDisposable
    {
        public void Dispose()
        {
            Window.Close();
            Provider.Dispose();
            if (Directory.Exists(TempDir))
            {
                Directory.Delete(TempDir, recursive: true);
            }
        }
    }

    private sealed class TestTrayPlatform : ISystemTrayPlatform
    {
        public bool Initialize(
            SystemTrayMenuText text,
            Action showWindow,
            Action exitApplication) => true;

        public void UpdateText(SystemTrayMenuText text)
        {
        }

        public void Dispose()
        {
        }
    }
}
