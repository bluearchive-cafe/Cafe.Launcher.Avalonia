using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cafe.Launcher.Core.Constants;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.UI.Controls;
using Cafe.Launcher.UI.Features.GameOperations;
using Cafe.Launcher.UI.Models;

namespace Cafe.Launcher.HeadlessTests;

public sealed partial class MainWindowHeadlessTests
{
    [AvaloniaTheory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task GameRail_SelectionFollowsTheCommittedGame(bool keyboard, bool canSwitch)
    {
        using var context = CreateContext();
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var games = context.ViewModel.Games!;
        var rail = context.Window.FindControl<ListBox>("GameRail")!;
        var first = Assert.IsType<ListBoxItem>(rail.ContainerFromIndex(0));
        var second = Assert.IsType<ListBoxItem>(rail.ContainerFromIndex(1));
        // 设计规范 §4.1：窄轨道只保留游戏图标，名称/区域/进度改由悬停与键盘焦点提示承载。
        Assert.Single(first.GetVisualDescendants().OfType<Image>(), image => image.Classes.Contains("game-rail-icon"));
        Assert.Single(second.GetVisualDescendants().OfType<Image>(), image => image.Classes.Contains("game-rail-icon"));
        Assert.Equal(games.Entries[0].RailTooltip, ToolTip.GetTip(first) as string);
        Assert.Equal(games.Entries[1].RailTooltip, ToolTip.GetTip(second) as string);
        Assert.Equal(games.Entries[0].Title, AutomationProperties.GetName(first));
        Assert.Equal(games.Entries[1].Title, AutomationProperties.GetName(second));
        Assert.True(first.IsSelected);
        Assert.True(first.Focus(NavigationMethod.Tab));
        Dispatcher.UIThread.RunJobs();
        // 焦点提示与悬停提示同源：窄轨道不能只靠鼠标发现名称和状态。
        Assert.True(ToolTip.GetIsOpen(first));
        var railTip = Assert.Single(context.Window.GetVisualDescendants().OfType<ToolTip>());
        Assert.Same(
            context.Window.FindResource(context.Window.ActualThemeVariant, "Launcher.Color.Glass.Strong.Background"),
            railTip.Background);
        games.CanSwitch = () => canSwitch;

        if (keyboard)
        {
            context.Window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, "");
            context.Window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, "");
        }
        else
        {
            var point = second.TranslatePoint(new Rect(second.Bounds.Size).Center, context.Window);
            context.Window.MouseDown(point!.Value, MouseButton.Left);
            context.Window.MouseUp(point.Value, MouseButton.Left);
        }
        Dispatcher.UIThread.RunJobs();
        await games.SelectGameCommand.ExecutionTask!.WaitAsync(TimeSpan.FromSeconds(5));
        Dispatcher.UIThread.RunJobs();

        var expected = canSwitch ? second : first;
        Assert.Same(games.Entries[canSwitch ? 1 : 0], rail.SelectedItem);
        Assert.Same(games.ActiveEntry, rail.SelectedItem);
        Assert.True(expected.IsSelected);
        Assert.True(expected.IsFocused);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NextSync_SettingsPreviewRetainsModalFocusAndDraft(bool gameScope)
    {
        using var context = CreateContext();
        PrepareGoldenWindow(context);
        context.Window.Width = 1024;
        context.Window.Height = 640;
        if (gameScope) OpenGameSettings(context); else OpenSettings(context);
        var settings = context.ViewModel.Settings;
        settings.SelectedCategory = gameScope ? SettingsCategoryCodes.GameBackground : SettingsCategoryCodes.DefaultBackground;
        if (gameScope) settings.BackgroundEditor.UseBackgroundOverride = true;
        settings.BackgroundEditor.BackgroundFit = BackgroundFits.Uniform;
        await settings.PendingAppearancePreview.WaitAsync(TimeSpan.FromSeconds(5));
        using var typography = new TypographyScaleSnapshot(context.Window, 2);
        Dispatcher.UIThread.RunJobs();
        var footer = context.Window.GetVisualDescendants().OfType<Grid>()
            .Single(panel => panel.Name == "SettingsFooter" && panel.IsEffectivelyVisible);
        Assert.False(footer.Children[0].Bounds.Intersects(footer.Children[1].Bounds));
        var preview = footer.GetVisualDescendants().OfType<Button>()
            .Single(button => ReferenceEquals(button.Command, settings.PreviewMainWindowCommand));
        Assert.True(preview.Focus(NavigationMethod.Tab));
        settings.PreviewMainWindowCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var toolbar = context.Window.FindControl<Border>("AppearancePreviewToolbar")!;
        var returnButton = context.Window.FindControl<Button>("ReturnToSettingsButton")!;
        var main = context.Window.FindControl<Grid>("MainContentLayer")!;
        AssertControlInsideWindow(toolbar, context.Window);
        Assert.False(footer.IsEffectivelyVisible);
        Assert.False(main.IsEffectivelyEnabled);
        Assert.True(returnButton.IsFocused);
        Assert.True(main.Bounds.Bottom <= toolbar.Bounds.Top);
        context.Window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, "");
        context.Window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, "");
        Assert.True(returnButton.IsFocused);

        context.Window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "");
        context.Window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "");
        Dispatcher.UIThread.RunJobs();
        Assert.False(settings.IsPreviewing);
        Assert.True(context.ViewModel.WindowChrome.IsSettingsVisible);
        Assert.True(preview.IsFocused);
        Assert.True(settings.IsSettingsDirty);
        Assert.Equal(gameScope, settings.IsGameSettingsEditing);
        Assert.Equal(gameScope ? SettingsCategoryCodes.GameBackground : SettingsCategoryCodes.DefaultBackground, settings.SelectedCategory);

        await context.ViewModel.WindowChrome.DiscardSettingsChangesCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(settings.IsGlobalBackgroundPreview);
        Assert.False(settings.IsSettingsDirty);
        Assert.True(main.IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public async Task NextSync_GameHeader_KeepsScopedActionsReachableWhenSwitchingGames()
    {
        using var context = CreateContext();
        var window = context.Window;
        window.Width = 1024;
        window.Height = 640;
        context.ViewModel.Shell.ApplyLanguage(LauncherLanguages.English, hasSnapshot: false);
        window.Show();
        using var typography = new TypographyScaleSnapshot(window, 2d);
        Dispatcher.UIThread.RunJobs();

        var header = window.FindControl<Border>("GameHeader")!;
        var resource = window.FindControl<Button>("GameResourceAction")!;
        var settings = window.FindControl<Button>("GameSettingsAction")!;
        var wordmark = window.FindControl<Rectangle>("GameWordmark")!;
        var wordmarkPeer = ControlAutomationPeer.CreatePeerForElement(wordmark)!;
        Assert.True(wordmarkPeer.IsContentElement());
        Assert.Equal(AutomationControlType.Image, wordmarkPeer.GetAutomationControlType());
        Assert.Equal(context.ViewModel.Shell.I18n["gameNameBlueArchiveJapan"], wordmarkPeer.GetName());
        Assert.True(resource.IsEffectivelyVisible);
        Assert.True(settings.IsEffectivelyVisible);
        AssertControlInsideWindow(resource, window);
        AssertControlInsideWindow(settings, window);
        var settingsRight = settings.TranslatePoint(new Point(settings.Bounds.Width, 0), header)!.Value.X;

        var stella = LauncherProfiles.StellaSoraChina.RuntimeId;
        Assert.True(await context.ViewModel.Games!.TrySelectGameAsync(stella));
        Dispatcher.UIThread.RunJobs();
        Assert.False(resource.IsEffectivelyVisible);
        Assert.False(wordmark.IsEffectivelyVisible);
        Assert.True(settings.IsEffectivelyVisible);
        AssertControlInsideWindow(settings, window);
        Assert.Equal(settingsRight, settings.TranslatePoint(new Point(settings.Bounds.Width, 0), header)!.Value.X);

        Assert.True(settings.Focus(NavigationMethod.Tab));
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "");
        window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "");
        Dispatcher.UIThread.RunJobs();
        Assert.True(context.ViewModel.WindowChrome.IsSettingsVisible);
        Assert.True(context.ViewModel.Settings.IsGameSettingsEditing);
        Assert.Equal(stella, context.ViewModel.Settings.EditingGameId);
    }

    [AvaloniaFact]
    public void NextSync_NewsCanCollapseWithoutHidingTheGameAction()
    {
        using var context = CreateContext();
        ApplyRemoteState(context, state => state.OperationsResource = new OperationsResourceResponse
        {
            OperationsResourceOpen = true,
            NewsList = new NewsListEnvelope
            {
                Code = 0,
                Data = new NewsListData
                {
                    News = [new NewsTypeItem
                    {
                        TypeLabel = "News",
                        Rows = Enumerable.Range(1, 5)
                            .Select(i => new NewsRowItem { Title = $"News item {i}", Link = $"https://news.example.invalid/{i}" })
                            .ToList()
                    }]
                }
            }
        });
        context.Window.Width = 1024;
        context.Window.Height = 640;
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();
        var vm = context.ViewModel.RemoteContent;
        var news = context.Window.FindControl<Border>("HomeNews")!;
        var operation = context.Window.FindControl<Border>("OperationSurface")!;
        var button = news.GetVisualDescendants().OfType<Button>()
            .Single(control => ReferenceEquals(control.Command, vm.ToggleNewsCategoryCommand));
        Assert.True(button.IsEffectivelyVisible);
        var collapsed = vm.NewsExpansionActionLabel;
        vm.ToggleNewsCategoryCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm.SelectedNewsCategory!.IsExpanded);
        Assert.Equal(5, vm.SelectedNewsCategory.VisibleItems.Count());
        Assert.NotEqual(collapsed, vm.NewsExpansionActionLabel);
        Assert.True(operation.IsEffectivelyVisible);
        // 新骨架中资讯占左侧身份区、操作浮在右下角：两者以水平分离保证不互相遮挡。
        var newsOrigin = news.TranslatePoint(default, context.Window)!.Value;
        var operationOrigin = operation.TranslatePoint(default, context.Window)!.Value;
        Assert.True(newsOrigin.X + news.Bounds.Width <= operationOrigin.X);
        Assert.True(newsOrigin.Y + news.Bounds.Height <= context.Window.ClientSize.Height);
        vm.ToggleNewsCategoryCommand.Execute(null);
        Assert.False(vm.SelectedNewsCategory.IsExpanded);
        Assert.Equal(3, vm.SelectedNewsCategory.VisibleItems.Count());
        Assert.Equal(collapsed, vm.NewsExpansionActionLabel);
    }

    [AvaloniaFact]
    public async Task NextSync_BackgroundFailureReturnsToOwningGame()
    {
        using var context = CreateContext();
        PrepareGoldenWindow(context);
        context.Window.Show();
        var games = context.ViewModel.Games!;
        var owner = games.Active;
        Assert.True(await games.TrySelectGameAsync(LauncherProfiles.StellaSoraChina.RuntimeId));
        var host = (IGameOperationJourneyHost)owner.Operations;
        host.Present(new GameOperationPresentation.Preparing());
        host.Present(new GameOperationPresentation.Completed(new GameOperationResult
        {
            Message = "Connection interrupted.",
            ErrorCode = GameOperationErrorCode.Network
        }));
        Dispatcher.UIThread.RunJobs();
        Assert.Same(owner, games.BackgroundError?.Context);
        // 窄轨道用图标角标提示后台失败；打开任务仍由所属游戏的条目承担。
        var ownerEntry = games.Entries.Single(entry => ReferenceEquals(entry.Context, owner));
        Assert.True(ownerEntry.HasErrorBadge);
        Assert.False(ownerEntry.HasTaskBadge);
        await games.ShowBackgroundErrorCommand.ExecuteAsync(null);
        Assert.Same(owner, games.Active);
        Assert.False(games.HasBackgroundError);
        Assert.True(owner.Operations.IsTaskFailed);
        Assert.Contains("Connection interrupted.", owner.Operations.TaskMessage);
    }
}
