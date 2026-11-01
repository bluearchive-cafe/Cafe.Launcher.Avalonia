using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.UI.Constants;
using Cafe.Launcher.UI.Models;
using CommunityToolkit.Mvvm.Input;

namespace Cafe.Launcher.HeadlessTests;

public sealed partial class MainWindowHeadlessTests
{
    [AvaloniaTheory]
    [InlineData(false, 1300, 754)]
    [InlineData(true, 1300, 754)]
    [InlineData(false, 1024, 640)]
    [InlineData(true, 1024, 640)]
    public void LaunchSplitButton_ClickingEachPart_SeparatesLaunchFromManagement(bool hideDetails, double width, double height)
    {
        using var context = CreateContext();
        context.Window.Width = width;
        context.Window.Height = height;
        var split = ShowLaunchSplitButton(context, hideDetails);
        Assert.Same(context.ViewModel.Operations.StartGameCommand, split.Command);
        var launches = 0;
        split.Command = new RelayCommand(() => launches++);
        var primary = split.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "PART_PrimaryButton");
        var secondary = split.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "PART_SecondaryButton");
        Assert.Equal(AutomationProperties.GetName(split), AutomationProperties.GetName(primary));
        Assert.Equal(context.ViewModel.Shell.I18n[LocalizationKeys.GameManagement], AutomationProperties.GetName(secondary));
        Assert.False(primary.IsTabStop);
        Assert.False(secondary.IsTabStop);
        Assert.Equal(primary.Bounds.Right, secondary.Bounds.Left);
        ClickLaunchPart(context, primary);
        Assert.Equal(1, launches);
        Assert.False(split.Flyout!.IsOpen);

        ClickLaunchPart(context, secondary);
        Assert.Equal(1, launches);
        var menu = Assert.IsType<MenuFlyout>(split.Flyout);
        Assert.True(menu.IsOpen);
        var presenter = Assert.IsType<MenuFlyoutPresenter>(menu.Popup.Child);
        var menuBottomRight = presenter.PointToScreen(new Point(presenter.Bounds.Width, presenter.Bounds.Height));
        var buttonTopRight = split.PointToScreen(new Point(split.Bounds.Width, 0));
        Assert.InRange(Math.Abs(menuBottomRight.X - buttonTopRight.X), 0, 1);
        Assert.InRange(Math.Abs(buttonTopRight.Y - menuBottomRight.Y - 8 * context.Window.RenderScaling), 0, 1);
        var items = menu.Items.OfType<MenuItem>().ToArray();
        Assert.Equal(5, items.Length);
        Assert.Same(context.ViewModel.Operations.RequestRepairCommand, items[3].Command);
        Assert.Same(context.ViewModel.Operations.RequestUninstallCommand, items[4].Command);
        menu.Hide();
        Assert.Equal(1, launches);
    }

    [AvaloniaTheory]
    [InlineData(Key.F4, RawInputModifiers.None, PhysicalKey.F4)]
    [InlineData(Key.Down, RawInputModifiers.Alt, PhysicalKey.ArrowDown)]
    public void LaunchSplitButton_Keyboard_ExecutesPrimaryAndOpensMenu(Key menuKey, RawInputModifiers modifiers, PhysicalKey physicalKey)
    {
        using var context = CreateContext();
        var split = ShowLaunchSplitButton(context);
        var launches = 0;
        split.Command = new RelayCommand(() => launches++);
        Assert.True(split.Focus(NavigationMethod.Tab));
        Dispatcher.UIThread.RunJobs();
        var focus = split.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "LaunchFocus");
        Assert.Equal(Assert.IsAssignableFrom<ISolidColorBrush>(split.Foreground).Color,
            Assert.IsAssignableFrom<ISolidColorBrush>(focus.BorderBrush).Color);

        context.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "");
        context.Window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "");
        Assert.Equal(1, launches);
        context.Window.KeyPress(menuKey, modifiers, physicalKey, "");
        context.Window.KeyRelease(menuKey, modifiers, physicalKey, "");
        Dispatcher.UIThread.RunJobs();
        Assert.True(split.Flyout!.IsOpen);
        Assert.Equal(1, launches);
        context.Window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "");
        context.Window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "");
        Dispatcher.UIThread.RunJobs();
        Assert.False(split.Flyout.IsOpen);
        Assert.True(split.IsFocused);
    }

    [AvaloniaFact]
    public void LaunchSplitButton_WhenBusy_DisablesBothParts()
    {
        using var context = CreateContext();
        var split = ShowLaunchSplitButton(context);
        var launches = 0;
        split.Command = new RelayCommand(() => launches++);
        context.ViewModel.Shell.IsBusy = true;
        Dispatcher.UIThread.RunJobs();
        Assert.False(split.IsEffectivelyEnabled);
        foreach (var part in split.GetVisualDescendants().OfType<Button>())
        {
            Assert.False(part.IsEffectivelyEnabled);
            ClickLaunchPart(context, part);
        }

        Assert.Equal(0, launches);
        Assert.False(split.Flyout!.IsOpen);
    }

    [AvaloniaTheory]
    [InlineData(LauncherLanguages.English)]
    [InlineData(LauncherLanguages.SimplifiedChinese)]
    [InlineData(LauncherLanguages.TraditionalChinese)]
    [InlineData(LauncherLanguages.Japanese)]
    public void LaunchSplitButton_AtMinimumWindowWithLargeText_KeepsLocalizedLabelInsideButton(string language)
    {
        using var context = CreateContext();
        context.Window.Width = 1024;
        context.Window.Height = 640;
        try
        {
            context.ViewModel.Shell.ApplyLanguage(language, hasSnapshot: false);
            var split = ShowLaunchSplitButton(context);
            using var typography = new TypographyScaleSnapshot(context.Window, 1.5);
            Dispatcher.UIThread.RunJobs();
            var label = split.GetVisualDescendants().OfType<TextBlock>()
                .Single(text => text.Text == context.ViewModel.Shell.I18n[LocalizationKeys.StartGame]);
            AssertControlInsideWindow(split, context.Window);
            Assert.True(label.TextLayout.Width <= label.Bounds.Width + 1);
            Assert.True(label.TextLayout.Height <= label.Bounds.Height + 1);
        }
        finally
        {
            context.ViewModel.Shell.ApplyLanguage(LauncherLanguages.English, hasSnapshot: false);
        }
    }

    private static SplitButton ShowLaunchSplitButton(TestContext context, bool hideDetails = false)
    {
        context.ViewModel.IsMotionReduced = true;
        context.ViewModel.Shell.IsBusy = false;
        context.ViewModel.Settings.Editor.Current.StatusDetailMode = hideDetails ? StatusDetailModes.Hidden : StatusDetailModes.Compact;
        context.ViewModel.Operations.PanelMode = GameOperationPanelMode.Control;
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();
        return context.Window.GetVisualDescendants().OfType<SplitButton>().Single(button => button.IsEffectivelyVisible);
    }

    private static void ClickLaunchPart(TestContext context, Control part)
    {
        var center = part.TranslatePoint(new Point(part.Bounds.Width / 2, part.Bounds.Height / 2), context.Window)!.Value;
        context.Window.MouseDown(center, MouseButton.Left, RawInputModifiers.None);
        context.Window.MouseUp(center, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void UninstallProgress_WhenScanFinishes_SwitchesToDeterminateProgressAndShowsPercentage()
    {
        using var context = CreateContext();
        context.Window.Show();
        context.ViewModel.Operations.Uninstall.Open(new Cafe.Launcher.Core.Models.LauncherStatusSnapshot());
        context.ViewModel.Operations.Uninstall.BeginExecution();
        context.ViewModel.Operations.ApplyProgress(new GameOperationProgress
        {
            OperationKind = GameOperationKind.Uninstall,
            Stage = GameOperationStage.UninstallScanning,
            IsRunning = true
        });
        Dispatcher.UIThread.RunJobs();
        var panel = context.Window.GetVisualDescendants().OfType<Cafe.Launcher.UI.Controls.DialogSurface>()
            .Single(surface => surface.Name == "UninstallSurface");
        var bar = panel.GetVisualDescendants().OfType<ProgressBar>().Single();
        var percentage = panel.GetVisualDescendants().OfType<TextBlock>()
            .Single(text => text.Name == "UninstallPercentage");

        Assert.True(panel.IsEffectivelyVisible);
        Assert.True(bar.IsIndeterminate);
        Assert.False(percentage.IsVisible);

        context.ViewModel.Operations.ApplyProgress(new GameOperationProgress
        {
            OperationKind = GameOperationKind.Uninstall,
            Stage = GameOperationStage.Uninstalling,
            Progress = 47,
            ProcessedEntryCount = 50,
            TotalEntryCount = 100,
            IsRunning = true
        });
        Dispatcher.UIThread.RunJobs();

        Assert.False(bar.IsIndeterminate);
        Assert.Equal(47, bar.Value);
        Assert.True(percentage.IsVisible);
        Assert.Equal("47%", percentage.Text);
        Assert.Contains(panel.GetVisualDescendants().OfType<TextBlock>(),
            text => text.Text == context.ViewModel.Operations.Uninstall.ProgressDetail && text.IsVisible);
        context.ViewModel.Operations.Uninstall.Complete(new GameOperationResult { Success = true });
    }
}
