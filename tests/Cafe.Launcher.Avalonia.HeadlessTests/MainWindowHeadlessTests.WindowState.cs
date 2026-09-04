using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Views;

namespace Cafe.Launcher.Avalonia.HeadlessTests;

public sealed partial class MainWindowHeadlessTests
{
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
    public void MainWindow_TitleBarDragHitTesting_ExcludesInteractiveControlsAndNonTitleContent()
    {
        using var context = CreateContext();
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var titleBar = context.Window.GetVisualDescendants()
            .OfType<Grid>()
            .Single(control => control.Name == "TitleBar");
        var settingsButton = titleBar.GetVisualDescendants()
            .OfType<Button>()
            .Single(control => control.Classes.Contains("settings"));
        var title = titleBar.GetVisualDescendants()
            .OfType<TextBlock>()
            .Single(control => control.Classes.Contains("titlebar-brand"));
        var outsideTitleBar = context.Window.GetVisualDescendants()
            .OfType<Grid>()
            .Single(control => control.Classes.Contains("operation-layout"));

        Assert.True(context.Window.IsWithinTitleBar(settingsButton));
        Assert.True(context.Window.IsWithinTitleBar(title));
        Assert.True(MainWindow.IsInteractive(settingsButton));
        Assert.False(MainWindow.IsInteractive(title));
        Assert.False(context.Window.IsWithinTitleBar(outsideTitleBar));
    }

    [AvaloniaFact]
    public void ConfigureViewModel_WiresAndUnwiresPlatformCapabilities()
    {
        var context = CreateContext();
        var viewModel = context.ViewModel;

        Assert.NotNull(viewModel.Settings.Appearance.GetBackgroundBitmap);
        Assert.NotNull(viewModel.Settings.PreviewAppearanceAsync);
        Assert.NotNull(viewModel.Settings.ApplyLanguageAndTheme);
        Assert.NotNull(viewModel.RemoteContent.OpenExternalUrlRequested);

        context.Dispose();

        Assert.Null(viewModel.Settings.Appearance.GetBackgroundBitmap);
        Assert.Null(viewModel.Settings.PreviewAppearanceAsync);
        Assert.Null(viewModel.Settings.ApplyLanguageAndTheme);
        Assert.Null(viewModel.RemoteContent.OpenExternalUrlRequested);
    }

    [AvaloniaFact]
    public void DebugEntry_VisibilityMatchesBuildConfiguration()
    {
        using var context = CreateContext();
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();
        var button = context.Window.GetVisualDescendants().OfType<Button>().Single(control =>
            ReferenceEquals(control.Command, context.ViewModel.WindowChrome.OpenDebugPanelCommand));

#if DEBUG
        Assert.True(button.IsEffectivelyVisible);
#else
        Assert.False(button.IsEffectivelyVisible);
#endif
    }

    [AvaloniaFact]
    public void DebugResetSettings_WhenRequested_ShowsConfirmationDialog()
    {
        using var context = CreateContext();

        context.ViewModel.Debug.ResetSettingsCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.True(context.ViewModel.Dialogs.IsDebugResetConfirmationVisible);
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
        // ADR-016：三种状态共享同一任务容器，底栏对遥测面板的让位约束对所有模式一致。
        var operationPanel = context.Window.GetVisualDescendants().OfType<Border>()
            .Single(control => control.Classes.Contains("operation-surface"));

        Assert.True(remotePanel.IsEffectivelyVisible);
        Assert.True(
            remotePanel.Bounds.Bottom <= operationPanel.Bounds.Top,
            $"Remote panel bottom {remotePanel.Bounds.Bottom} overlaps operation panel top {operationPanel.Bounds.Top}.");
    }

    [AvaloniaTheory]
    [InlineData("install", 4, 1)]
    [InlineData("progress", 2, 0)]
    [InlineData("control", 2, 1)]
    public void MainWindow_AtMinimumWindowSize_KeepsOperationActionsInsideWindow(
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

        var actions = context.Window
            .GetVisualDescendants()
            .OfType<Button>()
            .Where(control =>
                control.IsEffectivelyVisible
                && (control.Classes.Contains("primary-operation")
                    || control.Classes.Contains("secondary-operation")))
            .ToArray();

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

    [AvaloniaTheory]
    [InlineData(1300, 754)]
    [InlineData(1024, 640)]
    public void MainWindow_InstallPanel_AtDefaultAndMinimumWindowSizes_KeepsPathAndActionsReachable(
        double width,
        double height)
    {
        using var context = CreateContext();
        context.Window.Width = width;
        context.Window.Height = height;
        context.ViewModel.Operations.PanelMode = GameOperationPanelMode.Install;
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var pathField = context.Window
            .GetVisualDescendants()
            .OfType<Border>()
            .Single(control => control.Classes.Contains("path-field"));
        var actions = context.Window
            .GetVisualDescendants()
            .OfType<Button>()
            .Where(control => control.IsEffectivelyVisible
                && (control.Classes.Contains("primary-operation")
                    || control.Classes.Contains("secondary-operation")))
            .ToArray();

        Assert.Equal(4, actions.Length);
        Assert.True(pathField.Bounds.Width > 0);
        AssertControlInsideWindow(pathField, context.Window);
        Assert.All(actions, action => AssertControlInsideWindow(action, context.Window));
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

        // The compact install button carries the disk-space tooltip.
        var installButton = context.Window
            .GetVisualDescendants()
            .OfType<Button>()
            .Where(button => ReferenceEquals(
                button.Command,
                context.ViewModel.Operations.InstallOrUpdateCommand))
            .Single(button => button.IsEffectivelyVisible);

        Assert.Equal(expectedEnabled, installButton.IsEffectivelyEnabled);
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
    public void MainWindow_WindowStateSetting_RestoresAndCapturesNormalWindowBounds()
    {
        using var context = CreateContext();
        context.Window.Show();

        context.Window.ApplySavedWindowState(new LauncherSettings
        {
            RememberWindowPositionAndSize = true,
            WindowPositionX = 120,
            WindowPositionY = 240,
            WindowWidth = 1200,
            WindowHeight = 700
        });

        Assert.Equal(new PixelPoint(120, 240), context.Window.Position);
        Assert.Equal(1200, context.Window.Width);
        Assert.Equal(700, context.Window.Height);

        var captured = new LauncherSettings { RememberWindowPositionAndSize = true };
        context.Window.CaptureWindowState(captured);

        Assert.Equal(120, captured.WindowPositionX);
        Assert.Equal(240, captured.WindowPositionY);
        Assert.Equal(1200, captured.WindowWidth);
        Assert.Equal(700, captured.WindowHeight);
    }

    [AvaloniaFact]
    public void MainWindow_WindowStateSetting_WhenDisabledLeavesBoundsUnchanged()
    {
        using var context = CreateContext();
        context.Window.Show();
        var originalPosition = context.Window.Position;
        var originalWidth = context.Window.Width;
        var originalHeight = context.Window.Height;

        context.Window.ApplySavedWindowState(new LauncherSettings
        {
            RememberWindowPositionAndSize = false,
            WindowPositionX = 120,
            WindowPositionY = 240,
            WindowWidth = 1200,
            WindowHeight = 700
        });

        var captured = new LauncherSettings { RememberWindowPositionAndSize = false };
        context.Window.CaptureWindowState(captured);

        Assert.Equal(originalPosition, context.Window.Position);
        Assert.Equal(originalWidth, context.Window.Width);
        Assert.Equal(originalHeight, context.Window.Height);
        Assert.Null(captured.WindowPositionX);
        Assert.Null(captured.WindowPositionY);
        Assert.Null(captured.WindowWidth);
        Assert.Null(captured.WindowHeight);
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
}
