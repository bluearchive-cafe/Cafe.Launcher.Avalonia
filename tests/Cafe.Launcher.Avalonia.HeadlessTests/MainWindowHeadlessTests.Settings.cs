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
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Views;

namespace Cafe.Launcher.Avalonia.HeadlessTests;

public sealed partial class MainWindowHeadlessTests
{
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
    public void SettingsAbout_InlineLegalLinks_DoNotExpandCaptionLineHeight()
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

            var caption = link
                .GetVisualAncestors()
                .OfType<TextBlock>()
                .Single();

            Assert.Equal(caption.FontSize, link.FontSize);
        });
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
            .OfType<global::Cafe.Launcher.Avalonia.Controls.DialogSurface>()
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
            .OfType<global::Cafe.Launcher.Avalonia.Controls.SettingRow>()
            .Where(row => row.IsEffectivelyVisible)
            .ToArray();

        // 日志级别 / 日志文件操作 / 重置设置三行。
        Assert.Equal(3, rows.Length);
        var levelRow = Assert.Single(rows, row => row.GetVisualDescendants().OfType<ComboBox>().Any());
        var levelControl = levelRow.GetVisualDescendants().OfType<ComboBox>().Single();
        var logRow = Assert.Single(rows, row => row.GetVisualDescendants().OfType<Button>().Count() == 3);
        var logButtons = logRow
            .GetVisualDescendants()
            .OfType<Button>()
            .ToArray();
        Assert.Equal(3, logButtons.Length);
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

        var description = rows[1].FindControl<TextBlock>("RowDescription");
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
            .OfType<global::Cafe.Launcher.Avalonia.Controls.SettingRow>()
            .Single(row => row.GetVisualDescendants().OfType<Button>().Count() == 2);
        var buttons = managementRow.GetVisualDescendants().OfType<Button>().ToArray();
        var repairButton = Assert.Single(
            buttons,
            button => button.Classes.Contains("flat-action") && !button.Classes.Contains("danger-action"));
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
            .OfType<global::Cafe.Launcher.Avalonia.Controls.DialogSurface>()
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
            .OfType<global::Cafe.Launcher.Avalonia.Controls.DialogSurface>()
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
    public void SettingsContent_ShowsLocalizedTitleAndSubtitleForCurrentCategory()
    {
        using var context = CreateContext();
        OpenSettings(context);
        var title = context.Window.GetVisualDescendants().OfType<TextBlock>()
            .Single(control => control.Classes.Contains("category-title"));
        var subtitle = context.Window.GetVisualDescendants().OfType<TextBlock>()
            .Single(control => control.Classes.Contains("category-subtitle"));

        Assert.Equal(
            context.ViewModel.Settings.Options.SettingsCategories.Single(
                option => option.Code == SettingsCategoryCodes.General).DisplayName,
            title.Text);
        Assert.Equal(title.Text, AutomationProperties.GetName(title));
        Assert.Equal(
            context.ViewModel.Settings.Options.SettingsCategories.Single(
                option => option.Code == SettingsCategoryCodes.General).Description,
            subtitle.Text);

        context.ViewModel.Settings.SelectedCategory = SettingsCategoryCodes.Appearance;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(
            context.ViewModel.Settings.Options.SettingsCategories.Single(
                option => option.Code == SettingsCategoryCodes.Appearance).DisplayName,
            title.Text);
        Assert.Equal(title.Text, AutomationProperties.GetName(title));
        Assert.Equal(
            context.ViewModel.Settings.Options.SettingsCategories.Single(
                option => option.Code == SettingsCategoryCodes.Appearance).Description,
            subtitle.Text);
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
