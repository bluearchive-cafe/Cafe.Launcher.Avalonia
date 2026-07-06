using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
        // Reset dirty state after binding resolution (same rationale as OpenSettings)
        context.ViewModel.Settings.Editor.ApplySnapshot(
            context.ViewModel.Settings.Editor.GetSnapshot());

        var handled = context.ViewModel.TryHandleEscape();
        Dispatcher.UIThread.RunJobs();

        Assert.True(handled);
        Assert.False(context.ViewModel.WindowChrome.IsSettingsVisible);
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
        // Reset dirty state: TwoWay bindings on initial load may write back the
        // same value, which triggers IsDirty via PropertyChanged. This is a UI
        // loading artifact, not a real user change.
        context.ViewModel.Settings.Editor.ApplySnapshot(
            context.ViewModel.Settings.Editor.GetSnapshot());
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
        Assert.Equal(5, sections.Length);
        Assert.Single(sections, control => control.GetType() == expectedType && control.IsEffectivelyVisible);
        Assert.All(
            sections.Where(control => control.GetType() != expectedType),
            control => Assert.False(control.IsEffectivelyVisible));
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
