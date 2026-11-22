using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cafe.Launcher.Core.Constants;
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
            AddBlueArchiveSessionMonitor(services, monitor));
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
            AddBlueArchiveSessionMonitor(services, monitor));
        Assert.Same(monitor, context.ViewModel.Games!.Active.SessionMonitor);
        context.Window.Width = 1024;
        context.Window.Height = 640;
        context.ViewModel.Settings.Editor.Current.StatusDetailMode = StatusDetailModes.Compact;
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var row = context.Window.FindControl<WrapPanel>("GameStatusDetails")!;
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
    public void MainWindow_StatusAndPrimaryAction_KeepSeparateRegions(bool hideDetails, double width, double height, double scale)
    {
        using var context = CreateContext();
        PrepareGoldenWindow(context);
        context.Window.Width = width;
        context.Window.Height = height;
        var action = ShowLaunchSplitButton(context, hideDetails);
        using var typography = new TypographyScaleSnapshot(context.Window, scale);
        Dispatcher.UIThread.RunJobs();
        var status = context.Window.FindControl<WrapPanel>("GameStatusDetails")!;
        var actionOrigin = action.TranslatePoint(default, context.Window)!.Value;
        AssertControlInsideWindow(action, context.Window);
        Assert.Equal(12, context.Window.ClientSize.Width - actionOrigin.X - action.Bounds.Width);
        Assert.Equal(12, context.Window.ClientSize.Height - actionOrigin.Y - action.Bounds.Height);
        if (!hideDetails)
        {
            var statusOrigin = status.TranslatePoint(default, context.Window)!.Value;
            Assert.True(status.IsEffectivelyVisible);
            Assert.True(statusOrigin.X < actionOrigin.X);
            Assert.True(statusOrigin.X + status.Bounds.Width <= actionOrigin.X ||
                        statusOrigin.Y + status.Bounds.Height <= actionOrigin.Y);
            Assert.All(status.Children.OfType<TextBlock>().Where(caption => caption.IsEffectivelyVisible),
                caption => AssertControlInsideWindow(caption, context.Window));
        }
        else
        {
            Assert.False(status.IsEffectivelyVisible);
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

        var statusSummary = context.Window.FindControl<WrapPanel>("GameStatusDetails")!;
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

    private static void AddBlueArchiveSessionMonitor(IServiceCollection services, FakeGameSessionMonitor monitor)
    {
        services.AddSingleton<Func<LauncherGameContext, IGameSessionMonitor>>(
            (IServiceProvider _) => context => monitor);
    }
}
