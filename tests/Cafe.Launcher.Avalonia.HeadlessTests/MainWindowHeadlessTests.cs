using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cafe.Launcher.Avalonia.Composition;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Features.GameOperations;
using Cafe.Launcher.Avalonia.Features.Settings;
using Cafe.Launcher.Avalonia.Features.SetupWizard;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.ViewModels;
using Cafe.Launcher.Avalonia.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
    public void DownloadRunningChanged_FromWorkerThread_NotifiesOnUiThread()
    {
        var journey = new ThreadAwareGameOperationJourney();
        using var context = CreateContext(new FixedGameOperationJourneyFactory(journey));
        bool? notificationHasUiAccess = null;
        context.ViewModel.Operations.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(GameOperationsViewModel.IsDownloadRunning))
            {
                notificationHasUiAccess = Dispatcher.UIThread.CheckAccess();
            }
        };

        var worker = Task.Run(() => journey.SetDownloadRunning(true));
        Assert.True(worker.Wait(TimeSpan.FromSeconds(5)));

        Assert.Null(notificationHasUiAccess);
        Dispatcher.UIThread.RunJobs();
        Assert.True(notificationHasUiAccess);
        Assert.True(context.ViewModel.Operations.IsDownloadRunning);
    }

    [AvaloniaFact]
    public void DownloadRunningChanged_WhenNewerStateArrives_DropsStaleWorkerNotification()
    {
        var journey = new ThreadAwareGameOperationJourney();
        using var context = CreateContext(new FixedGameOperationJourneyFactory(journey));
        using var workerRaisedNotification = new ManualResetEventSlim();
        using var completeWorker = new ManualResetEventSlim();
        var notificationCount = 0;
        context.ViewModel.Operations.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(GameOperationsViewModel.IsDownloadRunning))
            {
                notificationCount++;
            }
        };

        var worker = Task.Run(() =>
        {
            journey.SetDownloadRunning(true);
            workerRaisedNotification.Set();
            completeWorker.Wait();
        });
        Assert.True(workerRaisedNotification.Wait(TimeSpan.FromSeconds(5)));
        journey.SetDownloadRunning(false);
        completeWorker.Set();
        Assert.True(worker.Wait(TimeSpan.FromSeconds(5)));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, notificationCount);
        Assert.False(context.ViewModel.Operations.IsDownloadRunning);
    }

    [AvaloniaFact]
    public void DownloadRunningChanged_AfterOperationsDisposed_IgnoresStaleJourneyCallback()
    {
        var journey = new ThreadAwareGameOperationJourney();
        using var context = CreateContext(new FixedGameOperationJourneyFactory(journey));
        var notificationCount = 0;
        context.ViewModel.Operations.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(GameOperationsViewModel.IsDownloadRunning))
            {
                notificationCount++;
            }
        };

        context.ViewModel.Operations.Dispose();
        context.ViewModel.Operations.Dispose();
        journey.RaiseStaleRunningChanged();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0, notificationCount);
    }

    [AvaloniaFact]
    public void ApplyProgress_FromWorkerThread_QueuesUiUpdate()
    {
        using var context = CreateContext();
        var worker = Task.Run(() => context.ViewModel.Operations.ApplyProgress(new GameOperationProgress
        {
            OperationKind = GameOperationKind.Download,
            Stage = GameOperationStage.Downloading,
            Progress = 50
        }));
        Assert.True(worker.Wait(TimeSpan.FromSeconds(5)));
        Assert.False(context.ViewModel.Operations.IsProgressPanelVisible);

        Dispatcher.UIThread.RunJobs();

        Assert.True(context.ViewModel.Operations.IsProgressPanelVisible);
        Assert.Equal(50, context.ViewModel.Operations.ProgressValue);
    }

    [AvaloniaFact]
    public void Settings_WhenLanguageChanges_RefreshesVisibleCategoryTitle()
    {
        using var context = CreateContext();
        OpenSettings(context);

        var categoryTitle = context.Window.GetVisualDescendants().OfType<TextBlock>()
            .Single(control => control.Classes.Contains("category-title"));
        var english = categoryTitle.Text;

        context.ViewModel.Shell.ApplyLanguage(
            LauncherLanguages.SimplifiedChinese,
            context.ViewModel.Settings,
            context.ViewModel.ResourcePanel,
            hasSnapshot: false);
        Dispatcher.UIThread.RunJobs();

        Assert.NotEqual(english, categoryTitle.Text);
        Assert.Equal(
            context.ViewModel.Settings.Options.SettingsCategories.First(option =>
                option.Code == context.ViewModel.Settings.SelectedCategory).DisplayName,
            categoryTitle.Text);
    }

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
    public async Task Toast_WithActions_RendersTitlePrimaryFirstAndDisablesControlsWhileExecuting()
    {
        using var context = CreateContext();
        var release = new TaskCompletionSource<ToastActionResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var toastService = context.Provider.GetRequiredService<ToastService>();
        context.Window.Show();
        toastService.Show(new ToastOptions
        {
            Title = "Install failed",
            Message = "Offline",
            Severity = ToastSeverity.Error,
            PrimaryAction = new ToastAction("Retry", _ => release.Task),
            SecondaryAction = new ToastAction(
                "View log",
                _ => Task.FromResult(ToastActionResult.Success()))
        });
        Dispatcher.UIThread.RunJobs();

        var title = context.Window.GetVisualDescendants().OfType<TextBlock>()
            .Single(control => control.Classes.Contains("toast-title"));
        var actionButtons = context.Window.GetVisualDescendants().OfType<Button>()
            .Where(control =>
                control.Classes.Contains("toast-primary-action")
                || control.Classes.Contains("toast-secondary-action"))
            .ToArray();
        var closeButton = context.Window.GetVisualDescendants().OfType<Button>()
            .Single(control => control.Classes.Contains("toast-close"));
        var actionProgress = context.Window.GetVisualDescendants().OfType<ProgressBar>()
            .Single(control => control.Classes.Contains("toast-progress"));

        Assert.Equal("Install failed", title.Text);
        Assert.Equal(2, actionButtons.Length);
        Assert.Contains("toast-primary-action", actionButtons[0].Classes);
        Assert.Contains("toast-secondary-action", actionButtons[1].Classes);
        Assert.Equal("Retry", AutomationProperties.GetName(actionButtons[0]));
        Assert.Equal("View log", AutomationProperties.GetName(actionButtons[1]));
        Assert.False(actionProgress.IsVisible);

        var executeTask = context.ViewModel.Toasts.ExecutePrimaryToastActionCommand.ExecuteAsync(
            context.ViewModel.Toasts.ActiveToasts.Single().Id);
        Dispatcher.UIThread.RunJobs();

        Assert.All(actionButtons, button => Assert.False(button.IsEffectivelyEnabled));
        Assert.True(closeButton.IsEffectivelyEnabled);
        Assert.True(actionProgress.IsVisible);

        release.SetResult(ToastActionResult.Failure("Still offline", "Retry failed"));
        await executeTask;
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Toast_WithoutActions_DoesNotRenderCountdownProgress()
    {
        using var context = CreateContext();
        var toastService = context.Provider.GetRequiredService<ToastService>();
        context.Window.Show();
        toastService.Show(new ToastOptions
        {
            Title = "Updated",
            Message = "You are up to date.",
            DurationMs = 60000
        });
        Dispatcher.UIThread.RunJobs();

        Assert.DoesNotContain(
            context.Window.GetVisualDescendants().OfType<ProgressBar>(),
            control => control.Classes.Contains("toast-progress") && control.IsVisible);
    }

    [AvaloniaFact]
    public void ConfigureViewModel_WiresAndUnwiresPlatformCapabilities()
    {
        var context = CreateContext();
        var viewModel = context.ViewModel;

        Assert.NotNull(viewModel.Settings.PickGameFolderAsync);
        Assert.NotNull(viewModel.Settings.PickBackgroundImageAsync);
        Assert.NotNull(viewModel.Settings.PickBackgroundFolderAsync);
        Assert.NotNull(viewModel.Background.PickBackgroundImageAsync);
        Assert.NotNull(viewModel.Background.PickBackgroundFolderAsync);
        Assert.NotNull(viewModel.LogViewer.PickExportDirectoryAsync);
        Assert.NotNull(viewModel.LogViewer.OpenExportDirectory);
        Assert.NotNull(viewModel.Debug.PickExportDirectoryAsync);
        Assert.NotNull(viewModel.Debug.OpenDirectory);

        context.Dispose();

        Assert.Null(viewModel.Settings.PickGameFolderAsync);
        Assert.Null(viewModel.Settings.PickBackgroundImageAsync);
        Assert.Null(viewModel.Settings.PickBackgroundFolderAsync);
        Assert.Null(viewModel.Background.PickBackgroundImageAsync);
        Assert.Null(viewModel.Background.PickBackgroundFolderAsync);
        Assert.Null(viewModel.LogViewer.PickExportDirectoryAsync);
        Assert.Null(viewModel.LogViewer.OpenExportDirectory);
        Assert.Null(viewModel.Debug.PickExportDirectoryAsync);
        Assert.Null(viewModel.Debug.OpenDirectory);
    }

    [AvaloniaFact]
    public void TitleBar_RestoresDirectActionOrderAndDebugVisibility()
    {
        using var context = CreateContext();
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();
        var actionBar = context.Window
            .GetVisualDescendants()
            .OfType<StackPanel>()
            .Single(control => control.Classes.Contains("titlebar-actions"));
        var brandBar = context.Window
            .GetVisualDescendants()
            .OfType<StackPanel>()
            .Single(control => control.Classes.Contains("titlebar-brand-row"));
        var buttons = actionBar.Children.OfType<Button>().ToArray();
        var debugButton = buttons[0];

        Assert.Equal(5, buttons.Length);
        Assert.Same(context.ViewModel.WindowChrome.OpenDebugPanelCommand, debugButton.Command);
        Assert.Same(context.ViewModel.ResourcePanel.OpenResourcePanelCommand, buttons[1].Command);
        Assert.Same(context.ViewModel.WindowChrome.ShowSettingsCommand, buttons[2].Command);
        Assert.Same(context.ViewModel.WindowChrome.MinimizeCommand, buttons[3].Command);
        Assert.Same(context.ViewModel.WindowChrome.CloseCommand, buttons[4].Command);
        Assert.Null(debugButton.Flyout);

#if DEBUG
        Assert.True(debugButton.IsEffectivelyVisible);
#else
        Assert.False(debugButton.IsEffectivelyVisible);
#endif

        var visibleButtons = buttons.Where(button => button.IsEffectivelyVisible).ToArray();
        Assert.All(visibleButtons, button =>
        {
            Assert.Equal(visibleButtons[0].Bounds.Size, button.Bounds.Size);
            var icon = Assert.IsAssignableFrom<Control>(button.Content);
            Assert.True(icon.Bounds.Width < button.Bounds.Width);
            Assert.True(icon.Bounds.Height < button.Bounds.Height);
            AssertControlInsideWindow(button, context.Window);
        });
        Assert.True(brandBar.Margin.Left > 0);
        Assert.True(actionBar.Margin.Right > 0);
        Assert.True(buttons[2].Margin.Right > 0);
    }

    [AvaloniaFact]
    public void DebugResetSettings_WhenRequested_ShowsConfirmationDialog()
    {
        using var context = CreateContext();

        context.ViewModel.Debug.ResetSettingsCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.True(context.ViewModel.Dialogs.IsDebugResetConfirmationVisible);
    }

    [AvaloniaFact]
    public void SettingsWorkspace_WhenShown_AppliesWorkspaceAndFailureRecoveryStyles()
    {
        using var context = CreateContext();
        context.ViewModel.WindowChrome.IsSettingsVisible = true;

        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var workspace = context.Window
            .GetVisualDescendants()
            .OfType<Grid>()
            .Single(control => control.Classes.Contains("settings-workspace"));
        var saveError = context.Window
            .GetVisualDescendants()
            .OfType<Border>()
            .Single(control => control.Classes.Contains("settings-save-error"));

        Assert.Equal(new Thickness(0), workspace.Margin);
        Assert.False(saveError.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void SettingsStatusRecovery_WhenShown_HasNoDecorativeStatusSummary()
    {
        using var context = CreateContext();
        OpenSettings(context);

        var borders = context.Window.GetVisualDescendants().OfType<Border>().ToArray();
        Assert.DoesNotContain(borders, control => control.Classes.Contains("settings-status-summary"));
        Assert.Contains(borders, control => control.Classes.Contains("settings-save-error"));
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
        Assert.All(
            textBlocks
                .Where(control => control.Classes.Contains("group-title"))
                .Where(control => control.Parent is StackPanel panel
                    && panel.Classes.Contains("settings-group")),
            control => Assert.Equal(6, control.Margin.Bottom));
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

            Assert.Equal(code, context.ViewModel.Settings.SelectedCategory);
            AssertVisibleSettingsSection(context.Window, sectionType);
        }
    }

    [AvaloniaFact]
    public async Task SettingsNavigation_PersistsChangesAutomatically()
    {
        using var context = CreateContext();
        OpenSettings(context);
        Assert.True(context.ViewModel.Settings.IsAutoSaveEnabled);
        context.ViewModel.Settings.Editor.Current.Language = LauncherLanguages.Japanese;
        Assert.True(context.ViewModel.Settings.IsSettingsDirty);
        Assert.True(context.ViewModel.Settings.IsAutoSaveEnabled);
        context.ViewModel.Settings.SelectedCategory = SettingsCategoryCodes.Appearance;
        context.ViewModel.Settings.SelectedCategory = SettingsCategoryCodes.General;

        await context.ViewModel.Settings.PendingAutoSave;

        Assert.Equal(LauncherLanguages.Japanese, context.ViewModel.Settings.Editor.Current.Language);
        var persisted = await context.Provider
            .GetRequiredService<LauncherSettingsService>()
            .ReadAsync();
        Assert.Equal(LauncherLanguages.Japanese, persisted.Language);
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
            Assert.Equal(1, Grid.GetColumn(actionPresenter));
        }
    }

    [AvaloniaFact]
    public void CompactHome_DrawerStartsCollapsedAndKeepsOperationDockVisible()
    {
        using var context = CreateContext();
        context.Window.Width = 1024;
        context.Window.Height = 640;
        context.ViewModel.SetCompactHome(true);
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var drawer = context.Window.GetVisualDescendants().OfType<Border>()
            .Single(control => control.Classes.Contains("home-drawer"));
        var dock = context.Window.GetVisualDescendants().OfType<Border>()
            .First(control => control.Classes.Contains("bottom-panel") && control.IsEffectivelyVisible);

        Assert.False(drawer.IsEffectivelyVisible);
        Assert.True(dock.IsEffectivelyVisible);

        context.ViewModel.ToggleHomeDrawerCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.True(drawer.IsEffectivelyVisible);
    }

    [AvaloniaTheory]
    [InlineData(1300, 754)]
    [InlineData(1024, 640)]
    public void SettingsAdvanced_AtSupportedWindowSizes_KeepsLogAndResetActionsReachable(
        double width,
        double height)
    {
        using var context = CreateContext();
        context.Window.Width = width;
        context.Window.Height = height;
        OpenSettings(context);
        context.ViewModel.Settings.SelectedCategory = SettingsCategoryCodes.Advanced;

        var section = context.Window
            .GetVisualDescendants()
            .OfType<SettingsAdvancedSection>()
            .Single();
        section.IsVisible = true;
        section.UpdateLayout();
        var rows = section
            .GetVisualDescendants()
            .OfType<global::Cafe.Launcher.Avalonia.Controls.SettingRow>()
            .ToArray();

        Assert.Equal(3, rows.Length);
        var levelControl = rows[0]
            .GetVisualDescendants()
            .OfType<ComboBox>()
            .Single();
        var logButtons = rows[1]
            .GetVisualDescendants()
            .OfType<Button>()
            .ToArray();
        Assert.Equal(3, logButtons.Length);
        var resetButton = rows[2]
            .GetVisualDescendants()
            .OfType<Button>()
            .Single(button => ReferenceEquals(
                button.Command,
                context.ViewModel.Settings.RequestResetSettingsCommand));

        AssertControlInsideWindow(levelControl, context.Window);
        Assert.All(logButtons, button => AssertControlInsideWindow(button, context.Window));

        var settingsScroller = context.Window
            .GetVisualDescendants()
            .OfType<ScrollViewer>()
            .Single(control => control.Classes.Contains("dialog-scroll") && control.IsEffectivelyVisible);
        settingsScroller.Offset = new Vector(
            settingsScroller.Offset.X,
            Math.Max(0, settingsScroller.Extent.Height - settingsScroller.Viewport.Height));
        Dispatcher.UIThread.RunJobs();

        Assert.True(resetButton.IsEffectivelyVisible);
        AssertControlInsideWindow(resetButton, context.Window);
    }

    [AvaloniaFact]
    public void SettingsSaving_DisablesNavigationWithoutSummaryOrFooter()
    {
        using var context = CreateContext();
        OpenSettings(context);
        var navigation = GetSettingsNavigation(context.Window);
        var settingsOverlay = context.Window.GetVisualDescendants()
            .OfType<MainWindowSettingsOverlay>().Single();

        context.ViewModel.Settings.IsSaving = true;
        Dispatcher.UIThread.RunJobs();

        Assert.False(navigation.IsEnabled);
        Assert.DoesNotContain(
            settingsOverlay.GetVisualDescendants().OfType<Border>(),
            control => control.Classes.Contains("settings-status-summary"));
        Assert.DoesNotContain(
            settingsOverlay.GetVisualDescendants().OfType<Border>(),
            control => control.Classes.Contains("dialog-footer"));
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
    public void SettingsScrollViewer_WhenContentOverflows_AlignsWithContentDividerRightEdge()
    {
        using var context = CreateContext();
        OpenSettings(context);
        context.ViewModel.Settings.SelectedCategory = SettingsCategoryCodes.Appearance;
        context.ViewModel.Settings.Editor.Current.ThemeColorMode = ThemeColorModes.Wallpaper;
        context.ViewModel.Settings.Editor.Current.BackgroundSource = BackgroundSources.Custom;
        context.ViewModel.Settings.Editor.Current.BackgroundFit = BackgroundFits.Uniform;
        Dispatcher.UIThread.RunJobs();

        var settingsOverlay = context.Window
            .GetVisualDescendants()
            .OfType<MainWindowSettingsOverlay>()
            .Single();
        var divider = settingsOverlay
            .GetVisualDescendants()
            .OfType<Border>()
            .Single(control => control.Classes.Contains("settings-content-divider"));
        var scrollViewer = settingsOverlay
            .GetVisualDescendants()
            .OfType<ScrollViewer>()
            .Single(control => control.Classes.Contains("dialog-scroll"));

        Assert.True(scrollViewer.Extent.Height > scrollViewer.Viewport.Height);
        Assert.Equal(new Thickness(0, 0, 28, 0), scrollViewer.Padding);

        var dividerRight = divider.TranslatePoint(new Point(divider.Bounds.Width, 0), context.Window);
        var scrollViewerRight = scrollViewer.TranslatePoint(new Point(scrollViewer.Bounds.Width, 0), context.Window);
        Assert.NotNull(dividerRight);
        Assert.NotNull(scrollViewerRight);
        Assert.InRange(Math.Abs(dividerRight!.Value.X - scrollViewerRight!.Value.X), 0, 0.5);
    }

    [AvaloniaFact]
    public void SettingsOverlay_AtMinimumWindowSize_KeepsDialogVisible()
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
        Assert.True(dialog.Bounds.Width <= context.Window.ClientSize.Width - 48);
        Assert.True(dialog.Bounds.Height <= context.Window.ClientSize.Height - 48);
        Assert.True(dialog.Bounds.Height > 0);
        Assert.DoesNotContain(
            dialog.GetVisualDescendants().OfType<Border>(),
            control => control.Classes.Contains("dialog-footer"));
    }

    [AvaloniaTheory]
    [InlineData("resource-panel")]
    [InlineData("log-viewer")]
    [InlineData("confirmation")]
    [InlineData("setup-wizard")]
    public void SecondaryOverlay_AtMinimumWindowSize_KeepsCriticalActionsReachable(string overlay)
    {
        using var context = CreateContext();
        context.Window.Width = 1024;
        context.Window.Height = 640;
        context.Window.Show();

        Button[] actions = overlay switch
        {
            "resource-panel" => ShowResourcePanel(context),
            "log-viewer" => ShowLogViewer(context),
            "confirmation" => ShowLongConfirmation(context),
            "setup-wizard" => ShowSetupWizard(context),
            _ => throw new ArgumentOutOfRangeException(nameof(overlay))
        };
        Dispatcher.UIThread.RunJobs();

        Assert.NotEmpty(actions);
        Assert.All(actions, action =>
        {
            Assert.True(action.IsEffectivelyVisible);
            Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(action)));
            AssertControlInsideWindow(action, context.Window);
        });
    }

    [AvaloniaFact]
    public void ResourcePanel_UsesCompactDialogTypographyAndPaddedBodyLayout()
    {
        using var context = CreateContext();
        context.Window.Width = 1300;
        context.Window.Height = 754;
        context.Window.Show();
        ShowResourcePanel(context);

        var title = context.Window
            .GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(control => control.IsEffectivelyVisible)
            .Single(control => control.Classes.Contains("dialog-title"));
        var body = context.Window
            .GetVisualDescendants()
            .OfType<ScrollViewer>()
            .Where(control => control.IsEffectivelyVisible)
            .Single(control => control.Classes.Contains("dialog-frame-body"));
        var status = context.Window
            .GetVisualDescendants()
            .OfType<Border>()
            .Where(control => control.IsEffectivelyVisible)
            .Single(control => control.Classes.Contains("resource-panel-status"));

        Assert.Equal(18, title.FontSize);
        Assert.Equal(new Thickness(16), body.Padding);

        var titleTopLeft = title.TranslatePoint(default, context.Window);
        var statusTopLeft = status.TranslatePoint(default, context.Window);
        Assert.NotNull(titleTopLeft);
        Assert.NotNull(statusTopLeft);
        Assert.InRange(Math.Abs(titleTopLeft.Value.X - statusTopLeft.Value.X), 0, 0.5);
    }

    [AvaloniaFact]
    public void ResourcePanel_InitialViewportKeepsResourceCardsAboveFooter()
    {
        using var context = CreateContext();
        context.Window.Width = 1300;
        context.Window.Height = 754;
        context.Window.Show();
        ShowResourcePanel(context);

        var body = context.Window
            .GetVisualDescendants()
            .OfType<ScrollViewer>()
            .Where(control => control.IsEffectivelyVisible)
            .Single(control => control.Classes.Contains("dialog-frame-body"));
        var bodyTopLeft = body.TranslatePoint(default, context.Window);
        Assert.NotNull(bodyTopLeft);
        var bodyBottom = bodyTopLeft.Value.Y + body.Bounds.Height;
        var cards = context.Window
            .GetVisualDescendants()
            .OfType<Border>()
            .Where(control =>
                control.IsEffectivelyVisible
                && control.Classes.Contains("resource-panel-item-card"))
            .ToArray();

        Assert.Equal(3, cards.Length);
        Assert.All(cards, card =>
        {
            var topLeft = card.TranslatePoint(default, context.Window);
            Assert.NotNull(topLeft);
            Assert.True(topLeft.Value.Y + card.Bounds.Height <= bodyBottom);
        });
    }

    [AvaloniaFact]
    public void SetupWizard_InJapaneseAtMinimumWindowSize_KeepsScrollableContentAndNavigationReachable()
    {
        using var context = CreateContext();
        context.Window.Width = 1024;
        context.Window.Height = 640;
        context.Window.Show();
        context.ViewModel.Dialogs.ShowSetupWizard();
        context.ViewModel.Shell.ApplyLanguage(
            LauncherLanguages.Japanese,
            context.ViewModel.Settings,
            context.ViewModel.ResourcePanel,
            hasSnapshot: false);
        Dispatcher.UIThread.RunJobs();

        var wizard = context.Window.GetVisualDescendants().OfType<SetupWizardOverlay>().Single();
        var content = wizard.GetVisualDescendants().OfType<ScrollViewer>()
            .Single(control => control.Classes.Contains("scroll-pad"));
        var next = GetWizardNextButton(context.Window, context.ViewModel);

        Assert.True(content.IsEffectivelyVisible);
        Assert.True(content.Viewport.Height > 0);
        Assert.True(next.IsEffectivelyVisible);
        AssertControlInsideWindow(next, context.Window);
    }

    [AvaloniaTheory]
    [InlineData("install")]
    [InlineData("progress")]
    [InlineData("control")]
    public void MainWindow_AtMinimumWindowSize_RemoteContentStartsCollapsedAboveOperationPanel(
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

        Assert.False(remotePanel.IsEffectivelyVisible);
        Assert.True(operationPanel.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void MainWindow_NewsList_WithMoreThanThreeItems_KeepsAllItemsAccessibleByScrolling()
    {
        using var context = CreateContext();
        const string longTitle =
            "This deliberately long launcher news title must wrap onto exactly two visible lines without being clipped by the fixed-height clickable row";
        var rows = Enumerable.Range(1, 5)
            .Select(index => new NewsRowItem
            {
                Title = index == 1 ? longTitle : $"News item {index}",
                Link = $"https://news.example.invalid/{index}",
                PublishTime = 1_700_000_000_000 + index
            })
            .ToList();
        context.ViewModel.RemoteContent.Apply(
            new LauncherRemoteState
            {
                OperationsResource = new OperationsResourceResponse
                {
                    OperationsResourceOpen = true,
                    NewsList = new NewsListEnvelope
                    {
                        Code = 0,
                        Data = new NewsListData
                        {
                            News =
                            [
                                new NewsTypeItem
                                {
                                    TypeLabel = "News",
                                    Rows = rows
                                }
                            ]
                        }
                    }
                }
            },
            new LauncherSettings { ShowRemoteContentCard = true },
            CancellationToken.None);
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var viewport = context.Window
            .GetVisualDescendants()
            .OfType<ScrollViewer>()
            .Single(control => control.Classes.Contains("news-viewport"));
        var rowButtons = viewport
            .GetVisualDescendants()
            .OfType<Button>()
            .Where(control => control.Classes.Contains("news-row"))
            .ToArray();

        Assert.Equal(5, context.ViewModel.RemoteContent.SelectedNewsCategory?.Items.Count);
        Assert.Equal(5, rowButtons.Length);
        Assert.Equal(184, viewport.Viewport.Height);
        Assert.All(rowButtons, row => Assert.Equal(56, row.Bounds.Height));
        Assert.True(viewport.Extent.Height > viewport.Viewport.Height);

        var longTitleText = rowButtons[0]
            .GetVisualDescendants()
            .OfType<TextBlock>()
            .Single(control => control.Text == longTitle);
        var longTitleTop = longTitleText.TranslatePoint(default, rowButtons[0]);
        Assert.Equal(2, longTitleText.TextLayout.TextLines.Count);
        Assert.True(longTitleText.TextLayout.Height <= longTitleText.Bounds.Height);
        Assert.NotNull(longTitleTop);
        Assert.True(longTitleTop.Value.Y >= 0);
        Assert.True(longTitleTop.Value.Y + longTitleText.Bounds.Height <= rowButtons[0].Bounds.Height);

        viewport.Offset = new Vector(0, viewport.Extent.Height - viewport.Viewport.Height);
        Dispatcher.UIThread.RunJobs();

        var lastRowTop = rowButtons[^1].TranslatePoint(default, viewport);
        Assert.NotNull(lastRowTop);
        Assert.InRange(lastRowTop.Value.Y, 0, viewport.Viewport.Height - rowButtons[^1].Bounds.Height);
    }

    [AvaloniaFact]
    public void MainWindow_BannerNavigationButtons_AreCenteredAcrossTheFullBanner()
    {
        using var context = CreateContext();
        context.ViewModel.RemoteContent.Apply(
            new LauncherRemoteState
            {
                OperationsResource = new OperationsResourceResponse
                {
                    OperationsResourceOpen = true,
                    BannerLoop = false,
                    OperationsBannerList =
                    [
                        new OperationsBannerItem { BannerImg = "", JumpUrl = "https://banner.example.invalid/1" },
                        new OperationsBannerItem { BannerImg = "", JumpUrl = "https://banner.example.invalid/2" }
                    ]
                }
            },
            new LauncherSettings { ShowRemoteContentCard = true },
            CancellationToken.None);
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var bannerShell = context.Window.GetVisualDescendants().OfType<Border>().Single(control =>
            control.Classes.Contains("banner-shell"));
        var previous = context.Window.GetVisualDescendants().OfType<Button>().Single(button =>
            ReferenceEquals(button.Command, context.ViewModel.RemoteContent.SelectPreviousBannerCommand));
        var next = context.Window.GetVisualDescendants().OfType<Button>().Single(button =>
            ReferenceEquals(button.Command, context.ViewModel.RemoteContent.SelectNextBannerCommand));

        Assert.All(new[] { previous, next }, button =>
        {
            var topLeft = button.TranslatePoint(default, bannerShell);
            Assert.NotNull(topLeft);
            var center = topLeft.Value.Y + button.Bounds.Height / 2;
            Assert.InRange(Math.Abs(center - bannerShell.Bounds.Height / 2), 0, 0.51);
        });
    }

    [AvaloniaFact]
    public void MainWindow_BannerNavigationButton_WhenHovered_UsesSemiTransparentChromeBackground()
    {
        using var context = CreateContext();
        context.ViewModel.RemoteContent.Apply(
            new LauncherRemoteState
            {
                OperationsResource = new OperationsResourceResponse
                {
                    OperationsResourceOpen = true,
                    BannerLoop = false,
                    OperationsBannerList =
                    [
                        new OperationsBannerItem { BannerImg = "", JumpUrl = "https://banner.example.invalid/1" },
                        new OperationsBannerItem { BannerImg = "", JumpUrl = "https://banner.example.invalid/2" }
                    ]
                }
            },
            new LauncherSettings { ShowRemoteContentCard = true },
            CancellationToken.None);
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var bannerShell = context.Window.GetVisualDescendants().OfType<Border>().Single(control =>
            control.Classes.Contains("banner-shell"));
        var bannerControls = context.Window.GetVisualDescendants().OfType<Grid>().Single(control =>
            control.Classes.Contains("banner-controls"));
        var next = context.Window.GetVisualDescendants().OfType<Button>().Single(button =>
            ReferenceEquals(button.Command, context.ViewModel.RemoteContent.SelectNextBannerCommand));

        var bannerCenter = bannerShell.TranslatePoint(
            new Point(bannerShell.Bounds.Width / 2, bannerShell.Bounds.Height / 2),
            context.Window);
        Assert.NotNull(bannerCenter);
        context.Window.MouseMove(bannerCenter.Value);
        Dispatcher.UIThread.RunJobs();

        var nextCenter = next.TranslatePoint(
            new Point(next.Bounds.Width / 2, next.Bounds.Height / 2),
            context.Window);
        Assert.NotNull(nextCenter);
        context.Window.MouseMove(nextCenter.Value);
        Dispatcher.UIThread.RunJobs();

        Assert.True(next.IsPointerOver);
        Assert.Equal(1, bannerControls.Opacity);
        Assert.Equal(
            Color.Parse("#CC000000"),
            Assert.IsType<SolidColorBrush>(next.Background).Color);
    }

    [AvaloniaFact]
    public void NewsCategoryTab_WhenKeyboardFocused_UsesUnderlineWithoutFocusAdorner()
    {
        using var context = CreateContext();
        context.ViewModel.RemoteContent.Apply(
            new LauncherRemoteState
            {
                OperationsResource = new OperationsResourceResponse
                {
                    OperationsResourceOpen = true,
                    NewsList = new NewsListEnvelope
                    {
                        Code = 0,
                        Data = new NewsListData
                        {
                            News =
                            [
                                new NewsTypeItem
                                {
                                    TypeLabel = "News",
                                    Rows =
                                    [
                                        new NewsRowItem
                                        {
                                            Title = "Focus test",
                                            Link = "https://news.example.invalid/focus",
                                            PublishTime = 1_700_000_000_000
                                        }
                                    ]
                                }
                            ]
                        }
                    }
                }
            },
            new LauncherSettings { ShowRemoteContentCard = true },
            CancellationToken.None);
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var tab = context.Window.GetVisualDescendants().OfType<Button>().Single(control =>
            control.Classes.Contains("news-category-tab"));

        Assert.Equal(new CornerRadius(0), tab.CornerRadius);

        context.Window.Activate();
        tab.Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();

        Assert.True(tab.IsFocused);
        Assert.Equal(new Thickness(0, 0, 0, 2), tab.BorderThickness);
        Assert.Equal(new CornerRadius(0), tab.CornerRadius);

        var adornerLayer = AdornerLayer.GetAdornerLayer(tab);
        Assert.NotNull(adornerLayer);
        Assert.DoesNotContain(
            adornerLayer.Children,
            child => ReferenceEquals(AdornerLayer.GetAdornedElement(child), tab));
    }

    [AvaloniaTheory]
    [InlineData("install", 4, 1)]
    [InlineData("progress", 2, 0)]
    [InlineData("control", 2, 1)]
    public void MainWindow_AtMinimumWindowSize_KeepsOperationContentAndActionsInsideWindow(
        string panelMode,
        int expectedActionCount,
        int expectedPrimaryActionCount)
    {
        using var context = CreateContext();
        context.ViewModel.Settings.Editor.Current.StatusDetailMode = StatusDetailModes.Compact;
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

        var panel = context.Window
            .GetVisualDescendants()
            .OfType<Border>()
            .Single(control =>
                control.Classes.Contains("bottom-panel")
                && control.IsEffectivelyVisible);
        var actions = panel
            .GetVisualDescendants()
            .OfType<Button>()
            .Where(control =>
                control.IsEffectivelyVisible
                && (ReferenceEquals(control.Command, context.ViewModel.RefreshCommand)
                    || ReferenceEquals(control.Command, context.ViewModel.Operations.InstallOrUpdateCommand)
                    || ReferenceEquals(control.Command, context.ViewModel.Settings.ChangePersistedGamePathCommand)
                    || ReferenceEquals(control.Command, context.ViewModel.Settings.SelectInstalledGameCommand)
                    || ReferenceEquals(control.Command, context.ViewModel.Operations.PauseResumeCommand)
                    || ReferenceEquals(control.Command, context.ViewModel.Operations.StopOperationCommand)
                    || ReferenceEquals(control.Command, context.ViewModel.WindowChrome.OpenOfficialSiteCommand)
                    || ReferenceEquals(control.Command, context.ViewModel.Operations.StartGameCommand)))
            .ToArray();

        Assert.Equal(expectedActionCount, actions.Length);
        Assert.Equal(
            expectedPrimaryActionCount,
            actions.Count(control =>
                ReferenceEquals(control.Command, context.ViewModel.Operations.InstallOrUpdateCommand)
                || ReferenceEquals(control.Command, context.ViewModel.Operations.StartGameCommand)));
        Assert.All(actions, control =>
        {
            Assert.True(control.IsEffectivelyVisible);
            AssertControlInsideWindow(control, context.Window);
        });

        if (panelMode == "progress")
        {
            var title = panel
                .GetVisualDescendants()
                .OfType<TextBlock>()
                .Single(control => control.Classes.Contains("operation-status-title"));
            Assert.True(title.IsEffectivelyVisible);
            AssertControlInsideWindow(title, context.Window);
        }
    }

    [AvaloniaTheory]
    [InlineData(StatusDetailModes.Detailed)]
    [InlineData(StatusDetailModes.Compact)]
    [InlineData(StatusDetailModes.Hidden)]
    public void MainWindow_ControlPanel_AllStatusModes_RestoresOfficialSiteAndStartActions(
        string statusDetailMode)
    {
        using var context = CreateContext();
        context.Window.Width = 1024;
        context.Window.Height = 640;
        var settings = context.ViewModel.Settings.Editor.GetSnapshot();
        settings.StatusDetailMode = statusDetailMode;
        context.ViewModel.Settings.Editor.ApplySnapshot(settings);
        context.ViewModel.Operations.PanelMode = GameOperationPanelMode.Control;
        context.ViewModel.Shell.IsBusy = false;
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var controlPanel = context.Window
            .GetVisualDescendants()
            .OfType<Border>()
            .Single(control =>
                control.Classes.Contains("control-panel")
                && control.IsEffectivelyVisible);
        var visibleActions = controlPanel
            .GetVisualDescendants()
            .OfType<Button>()
            .Where(button => button.IsEffectivelyVisible)
            .ToArray();

        Assert.Equal(2, visibleActions.Length);
        var officialSiteButton = visibleActions.Single(button => ReferenceEquals(
            button.Command,
            context.ViewModel.WindowChrome.OpenOfficialSiteCommand));
        var startButton = visibleActions.Single(button => ReferenceEquals(
            button.Command,
            context.ViewModel.Operations.StartGameCommand));
        var startButtonText = startButton
            .GetVisualDescendants()
            .OfType<TextBlock>()
            .Single();
        var application = Application.Current!;
        Assert.True(application.TryGetResource(
            "Cafe.Color.OnAccent",
            application.ActualThemeVariant,
            out var onAccentBrush));
        Assert.Equal(startButton.Bounds.Size, officialSiteButton.Bounds.Size);
        Assert.Equal(startButton.Foreground, startButtonText.Foreground);
        Assert.Same(onAccentBrush, startButtonText.Foreground);
        Assert.DoesNotContain(
            visibleActions,
            button => ReferenceEquals(button.Command, context.ViewModel.RefreshCommand)
                || ReferenceEquals(
                    button.Command,
                    context.ViewModel.Settings.ChangePersistedGamePathCommand));
        Assert.All(visibleActions, button => AssertControlInsideWindow(button, context.Window));

        context.ViewModel.Shell.IsBusy = true;
        Dispatcher.UIThread.RunJobs();

        Assert.False(startButton.IsEffectivelyEnabled);
        Assert.True(application.TryGetResource(
            "Cafe.Color.OnChrome.Muted",
            application.ActualThemeVariant,
            out var onChromeMutedBrush));
        Assert.Same(onChromeMutedBrush, startButtonText.Foreground);
    }

    [AvaloniaFact]
    public void MainWindow_ControlPanel_ActionButtons_ApplyPressedBrushesAtRuntime()
    {
        using var context = CreateContext();
        context.Window.Width = 1024;
        context.Window.Height = 640;
        context.ViewModel.Operations.PanelMode = GameOperationPanelMode.Control;
        context.ViewModel.Shell.IsBusy = false;
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var controlPanel = context.Window
            .GetVisualDescendants()
            .OfType<Border>()
            .Single(control =>
                control.Classes.Contains("control-panel")
                && control.IsEffectivelyVisible);
        var officialSiteButton = controlPanel
            .GetVisualDescendants()
            .OfType<Button>()
            .Where(button => button.IsEffectivelyVisible)
            .Single(button => ReferenceEquals(
                button.Command,
                context.ViewModel.WindowChrome.OpenOfficialSiteCommand));
        var startButton = controlPanel
            .GetVisualDescendants()
            .OfType<Button>()
            .Where(button => button.IsEffectivelyVisible)
            .Single(button => ReferenceEquals(
                button.Command,
                context.ViewModel.Operations.StartGameCommand));
        var application = Application.Current!;

        Assert.True(startButton.IsEffectivelyEnabled);
        Assert.True(application.TryGetResource(
            "Cafe.Color.Accent.Pressed",
            application.ActualThemeVariant,
            out var accentPressedBrush));
        Assert.True(application.TryGetResource(
            "Cafe.Color.Surface.Info",
            application.ActualThemeVariant,
            out var surfaceInfoBrush));
        Assert.True(application.TryGetResource(
            "Cafe.Color.Surface",
            application.ActualThemeVariant,
            out var surfaceBrush));
        var surfaceInfoColor = Assert.IsType<SolidColorBrush>(surfaceInfoBrush).Color;
        Assert.Equal(byte.MaxValue, surfaceInfoColor.A);

        var startPoint = startButton.TranslatePoint(
            new Point(startButton.Bounds.Width / 2, startButton.Bounds.Height / 2),
            context.Window);
        Assert.NotNull(startPoint);
        context.Window.MouseMove(startPoint.Value);
        context.Window.MouseDown(startPoint.Value, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Same(accentPressedBrush, startButton.Background);
        Assert.Same(accentPressedBrush, startButton.BorderBrush);
        Assert.Equal(new Thickness(1), startButton.BorderThickness);

        context.Window.MouseUp(startPoint.Value, MouseButton.Left);
        var officialSitePoint = officialSiteButton.TranslatePoint(
            new Point(officialSiteButton.Bounds.Width / 2, officialSiteButton.Bounds.Height / 2),
            context.Window);
        Assert.NotNull(officialSitePoint);
        context.Window.MouseMove(officialSitePoint.Value);
        Dispatcher.UIThread.RunJobs();

        Assert.Same(surfaceBrush, officialSiteButton.Background);

        context.Window.MouseDown(officialSitePoint.Value, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Same(surfaceInfoBrush, officialSiteButton.Background);
    }

    [AvaloniaTheory]
    [InlineData(1300, 754)]
    [InlineData(1024, 640)]
    public void MainWindow_InstallPathRow_AtDefaultAndMinimumWindowSizes_KeepsInlinePathActionAndExternalActionsReachable(
        double width,
        double height)
    {
        using var context = CreateContext();
        context.Window.Width = width;
        context.Window.Height = height;
        context.ViewModel.Operations.PanelMode = GameOperationPanelMode.Install;
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var pathRow = context.Window
            .GetVisualDescendants()
            .OfType<Grid>()
            .Single(control => control.Classes.Contains("install-path-row"));
        var pathField = Assert.IsType<Border>(pathRow.Parent);
        Assert.Contains("path-field", pathField.Classes);
        var changePathButton = pathField
            .GetVisualDescendants()
            .OfType<Button>()
            .Single(control => ReferenceEquals(
                control.Command,
                context.ViewModel.Settings.ChangePersistedGamePathCommand));
        var detectButton = pathField
            .GetVisualDescendants()
            .OfType<Button>()
            .Single(control => ReferenceEquals(
                control.Command,
                context.ViewModel.Settings.SelectInstalledGameCommand));
        var pathText = pathField
            .GetVisualDescendants()
            .OfType<TextBlock>()
            .Single(control => control.Classes.Contains("caption"));
        var actionPanel = context.Window
            .GetVisualDescendants()
            .OfType<StackPanel>()
            .Single(control =>
                control.Classes.Contains("operation-actions")
                && control.IsEffectivelyVisible);
        var externalActions = actionPanel
            .GetVisualDescendants()
            .OfType<Button>()
            .OrderBy(control => control.Bounds.Left)
            .ToArray();
        var changePathTopLeft = changePathButton.TranslatePoint(default, pathField);
        var detectTopLeft = detectButton.TranslatePoint(default, pathField);
        var pathTextTopLeft = pathText.TranslatePoint(default, pathField);
        var pathFieldTopLeft = pathField.TranslatePoint(default, context.Window);
        var actionPanelTopLeft = actionPanel.TranslatePoint(default, context.Window);

        Assert.NotNull(changePathTopLeft);
        Assert.NotNull(detectTopLeft);
        Assert.NotNull(pathTextTopLeft);
        Assert.NotNull(pathFieldTopLeft);
        Assert.NotNull(actionPanelTopLeft);

        var changePathTopInset = changePathTopLeft.Value.Y;
        var changePathBottomInset = pathField.Bounds.Height
            - (changePathTopLeft.Value.Y + changePathButton.Bounds.Height);

        Assert.Equal(2, externalActions.Length);
        Assert.True(pathField.Bounds.Width > 0);
        Assert.True(pathFieldTopLeft.Value.X + pathField.Bounds.Width <= actionPanelTopLeft.Value.X);
        Assert.True(externalActions[0].Bounds.Right <= externalActions[1].Bounds.Left);
        Assert.True(pathTextTopLeft.Value.X + pathText.Bounds.Width <= changePathTopLeft.Value.X);
        Assert.True(changePathTopLeft.Value.X >= 0);
        Assert.True(changePathTopLeft.Value.X + changePathButton.Bounds.Width <= detectTopLeft.Value.X);
        Assert.True(detectTopLeft.Value.X + detectButton.Bounds.Width <= pathField.Bounds.Width);
        Assert.True(changePathTopLeft.Value.Y >= 0);
        Assert.True(changePathTopLeft.Value.Y + changePathButton.Bounds.Height <= pathField.Bounds.Height);
        Assert.InRange(Math.Abs(changePathTopInset - changePathBottomInset), 0, 4);
        Assert.Same(context.ViewModel.RefreshCommand, externalActions[0].Command);
        Assert.Same(context.ViewModel.Operations.InstallOrUpdateCommand, externalActions[1].Command);
        Assert.Contains("primary-action", externalActions[1].Classes);
        AssertControlInsideWindow(pathField, context.Window);
        AssertControlInsideWindow(changePathButton, context.Window);
        AssertControlInsideWindow(detectButton, context.Window);
        Assert.All(externalActions, action => AssertControlInsideWindow(action, context.Window));
    }

    [AvaloniaTheory]
    [InlineData(true, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    public void MainWindow_InstallButton_CombinesDiskSpaceCommandAndBusyBinding(
        bool isBlockedByDiskSpace,
        bool isBusy,
        bool expectedEnabled)
    {
        using var context = CreateContext();
        context.ViewModel.Settings.Editor.Current.StatusDetailMode = StatusDetailModes.Compact;
        context.Window.Show();
        context.ViewModel.Shell.IsInstallBlockedByDiskSpace = isBlockedByDiskSpace;
        context.ViewModel.Shell.InstallDiskSpaceMessage = isBlockedByDiskSpace
            ? "磁盘空间不足：需要 10GB，可用 6GB。"
            : "";
        context.ViewModel.Shell.IsBusy = isBusy;
        context.ViewModel.Operations.ApplySnapshot(new LauncherStatusSnapshot
        {
            RuntimeState = LauncherRuntimeState.NotInstalled
        });
        Dispatcher.UIThread.RunJobs();

        var installButton = context.Window
            .GetVisualDescendants()
            .OfType<Button>()
            .Single(button => ReferenceEquals(
                button.Command,
                context.ViewModel.Operations.InstallOrUpdateCommand));

        Assert.Equal(expectedEnabled, installButton.IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public void Settings_WhenLegacyDetailedModeIsLoaded_RemainsRenderable()
    {
        using var context = CreateContext();
        context.ViewModel.Settings.Editor.Current.StatusDetailMode = StatusDetailModes.Detailed;

        Assert.Equal(StatusDetailModes.Compact, context.ViewModel.Settings.Editor.Current.StatusDetailMode);
        OpenSettings(context);

        var statusCombo = context.Window
            .GetVisualDescendants()
            .OfType<ComboBox>()
            .Single(control =>
                control.Classes.Contains("setting-control")
                && control.SelectedValue?.ToString() == StatusDetailModes.Compact);

        Assert.True(statusCombo.IsEffectivelyVisible);
        Assert.Equal(2, statusCombo.ItemCount);
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
    public void SettingsOverlay_AutoSavesWithoutFooterActions()
    {
        using var context = CreateContext();
        context.Window.Show();
        context.ViewModel.WindowChrome.ShowSettingsCommand.Execute(null);
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
        Assert.Empty(footerButtons);
        Assert.True(context.ViewModel.WindowChrome.IsSettingsVisible);
    }

    [AvaloniaFact]
    public void ModalIsolation_WhenConfirmationIsVisible_DisablesBackgroundAndRestoresItAfterClose()
    {
        using var context = CreateContext();
        context.Window.Show();
        context.ViewModel.Dialogs.ShowResourcePanelSourceConfirm("switch source");
        Dispatcher.UIThread.RunJobs();
        var settingsButton = context.Window
            .GetVisualDescendants()
            .OfType<Button>()
            .Single(button =>
                button.Classes.Contains("settings")
                && ReferenceEquals(
                    button.Command,
                    context.ViewModel.WindowChrome.ShowSettingsCommand));
        var startButton = context.Window
            .GetVisualDescendants()
            .OfType<Button>()
            .First(button =>
                ReferenceEquals(
                    button.Command,
                    context.ViewModel.Operations.StartGameCommand));
        var cancelButton = context.Window
            .GetVisualDescendants()
            .OfType<Button>()
            .First(button =>
                button.IsEffectivelyVisible
                && ReferenceEquals(
                    button.Command,
                    context.ViewModel.Dialogs.CancelResourcePanelSourceSwitchCommand));

        Assert.False(settingsButton.IsEffectivelyEnabled);
        Assert.False(startButton.IsEffectivelyEnabled);
        Assert.True(cancelButton.IsEffectivelyEnabled);

        cancelButton.Command!.Execute(cancelButton.CommandParameter);
        Dispatcher.UIThread.RunJobs();

        Assert.True(settingsButton.IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public void ModalIsolation_WhenConfirmationCoversSettings_DisablesSettingsLayer()
    {
        using var context = CreateContext();
        context.Window.Show();
        context.ViewModel.WindowChrome.IsSettingsVisible = true;
        context.ViewModel.Dialogs.ShowRepairConfirm("repair confirmation");
        Dispatcher.UIThread.RunJobs();
        var settingsCancelButton = context.Window
            .GetVisualDescendants()
            .OfType<Button>()
            .Single(button =>
                button.Classes.Contains("dialog-close")
                && ReferenceEquals(
                    button.Command,
                    context.ViewModel.WindowChrome.ShowSettingsCommand));
        var confirmDialog = context.Window
            .GetVisualDescendants()
            .OfType<global::Cafe.Launcher.Avalonia.Controls.ConfirmDialog>()
            .Single(control => control.IsOpen);

        Assert.False(settingsCancelButton.IsEffectivelyEnabled);
        Assert.True(confirmDialog.IsEffectivelyEnabled);
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
            context.ViewModel.Shell.I18n["setupWizardGamePathAvailable"],
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
            context.ViewModel.Shell.I18n["setupWizardGamePathCorrupted"],
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
                == context.ViewModel.Shell.I18n["setupWizardEditStep"])
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
            AutomationProperties.GetName(control) == context.ViewModel.Shell.I18n["downloadSourceCafe"]);
        var official = context.Window.GetVisualDescendants().OfType<RadioButton>().Single(control =>
            AutomationProperties.GetName(control) == context.ViewModel.Shell.I18n["downloadSourceOfficial"]);

        official.IsChecked = true;
        Dispatcher.UIThread.RunJobs();

        Assert.True(context.ViewModel.Dialogs.SetupWizard.IsPatchUrlGroupOfficial);
        Assert.False(context.ViewModel.Dialogs.SetupWizard.IsPatchUrlGroupCafe);
        Assert.False(cafe.IsChecked);
        Assert.True(official.IsChecked);

        context.ViewModel.Dialogs.SetupWizard.NextCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var auto = context.Window.GetVisualDescendants().OfType<RadioButton>().Single(control =>
            AutomationProperties.GetName(control) == context.ViewModel.Shell.I18n["proxyAuto"]);
        var direct = context.Window.GetVisualDescendants().OfType<RadioButton>().Single(control =>
            AutomationProperties.GetName(control) == context.ViewModel.Shell.I18n["proxyDirect"]);
        var system = context.Window.GetVisualDescendants().OfType<RadioButton>().Single(control =>
            AutomationProperties.GetName(control) == context.ViewModel.Shell.I18n["proxySystem"]);

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
        context.ViewModel.Dialogs.SetupWizard.Language = language;
        Dispatcher.UIThread.RunJobs();
        context.ViewModel.Dialogs.SetupWizard.NextCommand.Execute(null);
        await WaitForGamePathStatusAsync(
            context.ViewModel.Dialogs.SetupWizard,
            SetupWizardGamePathStatus.AvailableForInstallation);
        Dispatcher.UIThread.RunJobs();

        var statusLine = GetWizardGamePathStatus(context.Window);
        var navigation = context.Window.GetVisualDescendants().OfType<ListBox>()
            .Single(control => control.Classes.Contains("wizard-navigation"));

        Assert.Equal(
            context.ViewModel.Shell.I18n["setupWizardGamePathAvailable"],
            statusLine.Text);
        Assert.Equal(statusLine.Text, AutomationProperties.GetName(statusLine));
        Assert.Equal(
            context.ViewModel.Shell.I18n["setupWizardStepTitle"],
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

    private static Button[] ShowResourcePanel(TestContext context)
    {
        context.ViewModel.ResourcePanel.IsResourcePanelVisible = true;
        Dispatcher.UIThread.RunJobs();
        return context.Window.GetVisualDescendants().OfType<Button>()
            .Where(button =>
                ReferenceEquals(button.Command, context.ViewModel.ResourcePanel.CloseResourcePanelCommand)
                || ReferenceEquals(button.Command, context.ViewModel.ResourcePanel.RefreshResourcePanelCommand)
                || ReferenceEquals(button.Command, context.ViewModel.ResourcePanel.SaveResourcePanelCommand))
            .ToArray();
    }

    private static Button[] ShowLogViewer(TestContext context)
    {
        context.ViewModel.LogViewer.IsVisible = true;
        Dispatcher.UIThread.RunJobs();
        return context.Window.GetVisualDescendants().OfType<Button>()
            .Where(button =>
                ReferenceEquals(button.Command, context.ViewModel.LogViewer.CloseCommand)
                || ReferenceEquals(button.Command, context.ViewModel.LogViewer.ExportCommand))
            .ToArray();
    }

    private static Button[] ShowLongConfirmation(TestContext context)
    {
        context.ViewModel.Dialogs.ShowRepairConfirm(string.Concat(Enumerable.Repeat(
            "下载源已切换，修复前需要重新确认本地文件状态。",
            30)));
        Dispatcher.UIThread.RunJobs();
        return context.Window.GetVisualDescendants().OfType<Button>()
            .Where(button =>
                (ReferenceEquals(button.Command, context.ViewModel.Dialogs.CancelRepairCommand)
                    || ReferenceEquals(button.Command, context.ViewModel.Dialogs.ConfirmRepairCommand))
                && button.IsEffectivelyVisible)
            .ToArray();
    }

    private static Button[] ShowSetupWizard(TestContext context)
    {
        context.ViewModel.Dialogs.ShowSetupWizard();
        Dispatcher.UIThread.RunJobs();
        return context.Window.GetVisualDescendants().OfType<Button>()
            .Where(button =>
                ReferenceEquals(button.Command, context.ViewModel.Dialogs.RequestSetupWizardExitCommand)
                || ReferenceEquals(button.Command, context.ViewModel.Dialogs.SetupWizard.NextCommand))
            .ToArray();
    }

    private static TestContext CreateContext(IGameOperationJourneyFactory? journeyFactory = null)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var services = new ServiceCollection();
        services.AddLauncherServices();
        services.RemoveAll<LauncherSettingsService>();
        services.AddSingleton(new LauncherSettingsService(Path.Combine(tempDir, "settings.json")));
        if (journeyFactory is not null)
        {
            services.AddSingleton(journeyFactory);
        }

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
        context.ViewModel.RemoteContent.StopCarouselTimer();
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
        Assert.Equal(new Thickness(0), selectedItem.BorderThickness);
        Assert.Equal(Color.Parse("#00000000"), Assert.IsType<SolidColorBrush>(selectedItem.BorderBrush).Color);
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

    private sealed class FixedGameOperationJourneyFactory(IGameOperationJourney journey)
        : IGameOperationJourneyFactory
    {
        public IGameOperationJourney Create(IGameOperationJourneyHost host) => journey;
    }

    private sealed class ThreadAwareGameOperationJourney : IGameOperationJourney
    {
        private bool isDownloadRunning;
        private Action? isRunningChanged;
        private Action? staleIsRunningChanged;

        public event Func<GameOperationsRefreshMode, Task>? RefreshRequested { add { } remove { } }
        public event Func<Task>? OpenLogViewerRequested { add { } remove { } }
        public event Action? MinimizeRequested { add { } remove { } }
        public event Action? IsRunningChanged
        {
            add
            {
                isRunningChanged += value;
                staleIsRunningChanged = value;
            }
            remove => isRunningChanged -= value;
        }

        public bool IsDownloadRunning => isDownloadRunning;
        public bool IsPaused => false;

        public void SetDownloadRunning(bool value)
        {
            isDownloadRunning = value;
            isRunningChanged?.Invoke();
        }

        public void RaiseStaleRunningChanged() => staleIsRunningChanged?.Invoke();

        public Task StartGameAsync(LauncherStatusSnapshot snapshot) => Task.CompletedTask;
        public Task InstallOrUpdateAsync(LauncherStatusSnapshot snapshot) => Task.CompletedTask;
        public Task RequestRepairAsync(LauncherStatusSnapshot snapshot) => Task.CompletedTask;
        public Task RepairAsync(LauncherStatusSnapshot snapshot) => Task.CompletedTask;
        public Task RequestUninstallAsync(LauncherStatusSnapshot snapshot) => Task.CompletedTask;
        public Task ConfirmUninstallAsync(LauncherStatusSnapshot snapshot) => Task.CompletedTask;
        public Task ResumePersistedAsync(
            LauncherStatusSnapshot snapshot,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public void RequestStop()
        {
        }

        public void PerformStop()
        {
        }

        public void Stop(bool clearPersistedState)
        {
        }

        public void Pause()
        {
        }

        public void Resume()
        {
        }
    }
}
