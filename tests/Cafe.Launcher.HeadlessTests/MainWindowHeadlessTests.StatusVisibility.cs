using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Testing;
using Cafe.Launcher.UI.Constants;
using Cafe.Launcher.UI.Features.GameOperations;
using Cafe.Launcher.UI.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Cafe.Launcher.HeadlessTests;

public sealed partial class MainWindowHeadlessTests
{
    [AvaloniaFact]
    public void Golden_SessionRunningChinese_MatchesBaseline()
    {
        var monitor = new FakeGameSessionMonitor();
        using var context = CreateContext(configure: services =>
            services.AddSingleton<IGameSessionMonitor>(monitor));
        PrepareGoldenWindow(context);
        ShowLaunchSplitButton(context);
        context.ViewModel.Shell.ApplyLanguage(LauncherLanguages.SimplifiedChinese, hasSnapshot: false);
        monitor.Transition(GameSessionState.Running);
        GoldenScreenshot.Compare(context.Window, "session-running-zh");
    }

    [AvaloniaTheory]
    [InlineData(LauncherLanguages.English)]
    [InlineData(LauncherLanguages.SimplifiedChinese)]
    [InlineData(LauncherLanguages.TraditionalChinese)]
    [InlineData(LauncherLanguages.Japanese)]
    public void MainWindow_StatusCaptions_FollowSessionAndStatusVisibility(string language)
    {
        var monitor = new FakeGameSessionMonitor();
        using var context = CreateContext(configure: services =>
            services.AddSingleton<IGameSessionMonitor>(monitor));
        context.Window.Width = 1024;
        context.Window.Height = 640;
        context.ViewModel.Settings.Editor.Current.StatusDetailMode = StatusDetailModes.Compact;
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var panel = context.Window.FindControl<Border>("OperationControlState")!;
        var row = Assert.Single(panel.GetVisualDescendants().OfType<WrapPanel>());
        var caption = context.Window.FindControl<TextBlock>("GameSessionStatusText");
        Assert.NotNull(caption);
        Assert.Same(row, caption.Parent);
        Assert.False(caption.IsEffectivelyVisible);
        var localizer = context.Provider.GetRequiredService<LocalizationService>();

        foreach (var (state, key) in new[]
        {
            (GameSessionState.Starting, LocalizationKeys.GameSessionStarting),
            (GameSessionState.Running, LocalizationKeys.GameSessionRunning),
            (GameSessionState.Exited, LocalizationKeys.GameSessionExitedNoCode)
        })
        {
            monitor.Transition(state);
            context.ViewModel.Shell.ApplyLanguage(language, hasSnapshot: false);
            Dispatcher.UIThread.RunJobs();
            Assert.True(caption.IsEffectivelyVisible);
            Assert.Equal(localizer.T(key), caption.Text);
            AssertControlInsideWindow(caption, context.Window);

            context.ViewModel.Settings.Editor.Current.StatusDetailMode = StatusDetailModes.Hidden;
            Dispatcher.UIThread.RunJobs();
            Assert.False(caption.IsEffectivelyVisible);
            context.ViewModel.Settings.Editor.Current.StatusDetailMode = StatusDetailModes.Compact;
            Dispatcher.UIThread.RunJobs();
            Assert.True(caption.IsEffectivelyVisible);
            Assert.Equal(localizer.T(key), caption.Text);
        }
    }

    [AvaloniaTheory]
    [InlineData(false, 1300, 754, 1d)]
    [InlineData(true, 1300, 754, 1d)]
    [InlineData(false, 1024, 640, 1.5d)]
    [InlineData(true, 1024, 640, 1.5d)]
    public void MainWindow_OperationPanel_UsesStatusDependentBottomInset(bool hideDetails, double width, double height, double scale)
    {
        using var context = CreateContext();
        PrepareGoldenWindow(context);
        context.Window.Width = width;
        context.Window.Height = height;
        var action = ShowLaunchSplitButton(context, hideDetails);
        using var typography = new TypographyScaleSnapshot(context.Window, scale);
        Dispatcher.UIThread.RunJobs();
        var panel = context.Window.FindControl<Border>("OperationControlState")!;
        var status = Assert.Single(panel.GetVisualDescendants().OfType<WrapPanel>());
        var actionBottomRight = action.TranslatePoint(new Point(action.Bounds.Width, action.Bounds.Height), context.Window)!.Value;
        var lastContent = hideDetails ? (Control)action : status;
        var contentBottomRight = lastContent.TranslatePoint(new Point(lastContent.Bounds.Width, lastContent.Bounds.Height), context.Window)!.Value;
        Assert.InRange(context.Window.Bounds.Width - actionBottomRight.X, 39, 41);
        var expectedBottomInset = hideDetails ? 40 : 24;
        Assert.InRange(context.Window.Bounds.Height - contentBottomRight.Y, expectedBottomInset - 1, expectedBottomInset + 1);
        if (!hideDetails)
        {
            Assert.InRange(Math.Abs(contentBottomRight.X - actionBottomRight.X), 0, 1);
            var statusTop = status.TranslatePoint(default, context.Window)!.Value.Y;
            Assert.InRange(statusTop - actionBottomRight.Y, 15, 17);
        }
    }

    [AvaloniaTheory]
    [InlineData(LauncherRuntimeState.NotInstalled)]
    [InlineData(LauncherRuntimeState.Corrupted)]
    [InlineData(LauncherRuntimeState.IoFailure)]
    [InlineData(LauncherRuntimeState.RemoteUnavailable)]
    [InlineData(LauncherRuntimeState.BelowLowestVersion)]
    [InlineData(LauncherRuntimeState.UpdateAvailable)]
    public void MainWindow_HiddenStatusDetails_KeepsPrimaryActionVisible(
        LauncherRuntimeState runtimeState)
    {
        using var context = CreateContext();
        context.Window.Width = 1024;
        context.Window.Height = 640;
        context.ViewModel.Settings.Editor.Current.StatusDetailMode = StatusDetailModes.Hidden;
        context.ViewModel.Operations.ApplySnapshot(new LauncherStatusSnapshot { RuntimeState = runtimeState });
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var installState = context.Window.FindControl<Border>("OperationControlState")!;
        var action = Assert.Single(installState.GetVisualDescendants().OfType<SplitButton>());
        Assert.True(action.IsEffectivelyVisible);
        Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(action)));
        AssertControlInsideWindow(action, context.Window);
        Assert.Null(context.Window.FindControl<Border>("OperationInstallState"));
        Assert.Null(context.Window.FindControl<Border>("OperationProgressState"));

        var statusSummary = Assert.Single(installState.GetVisualDescendants().OfType<WrapPanel>());
        Assert.False(statusSummary.IsEffectivelyVisible);

        context.ViewModel.Settings.Editor.Current.StatusDetailMode = StatusDetailModes.Compact;
        Dispatcher.UIThread.RunJobs();
        Assert.True(statusSummary.IsEffectivelyVisible);
        Assert.True(action.IsEffectivelyVisible);

        context.ViewModel.Settings.Editor.Current.StatusDetailMode = StatusDetailModes.Hidden;
        Dispatcher.UIThread.RunJobs();
        Assert.False(statusSummary.IsEffectivelyVisible);
        Assert.True(action.IsEffectivelyVisible);
    }
}
