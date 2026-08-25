using System.Reflection;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Cafe.Launcher.Avalonia.Controls;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.ViewModels;

namespace Cafe.Launcher.Avalonia.HeadlessTests;

public sealed class ToastStackMotionTests
{
    [AvaloniaFact]
    public void CalculateInitialOffset_WhenLayoutMovesDown_ReturnsPreviousVisualPosition()
    {
        var behaviorType = typeof(ToastHostViewModel).Assembly.GetType(
            "Cafe.Launcher.Avalonia.Controls.ToastStackMotion");

        Assert.NotNull(behaviorType);
        var calculateInitialOffset = behaviorType.GetMethod(
            "CalculateInitialOffset",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(calculateInitialOffset);
        var offset = calculateInitialOffset.Invoke(null, [12d, 52d]);

        Assert.Equal(-40d, offset);
    }

    [AvaloniaFact]
    public void Enable_WhenPanelRebounds_AnchorsChildAndAppliesFastLayoutTransition()
    {
        var panel = new StackPanel();
        var first = new Border { Height = 40, Width = 300 };
        panel.Children.Add(first);

        var window = new Window { Content = panel };
        ToastStackMotion.SetIsEnabled(panel, true);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        panel.Children.Insert(0, new Border { Height = 24, Width = 300 });
        Dispatcher.UIThread.RunJobs();

        Assert.True(ToastStackMotion.GetIsEnabled(panel));
        var transform = Assert.IsType<TranslateTransform>(first.RenderTransform);
        Assert.NotNull(transform.Transitions);
        var transition = Assert.IsType<DoubleTransition>(Assert.Single(transform.Transitions));
        Assert.Equal(MotionTokens.FastDuration, transition.Duration);
        window.Close();
    }

    [AvaloniaFact]
    public void Disable_WhenAnchoredPanelDisabled_ClearsAnimationConfiguration()
    {
        var panel = new StackPanel();
        var first = new Border { Height = 40, Width = 300 };
        panel.Children.Add(first);

        var window = new Window { Content = panel };
        ToastStackMotion.SetIsEnabled(panel, true);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        panel.Children.Insert(0, new Border { Height = 24, Width = 300 });
        Dispatcher.UIThread.RunJobs();
        var transform = Assert.IsType<TranslateTransform>(first.RenderTransform);
        Assert.NotNull(transform.Transitions);
        Assert.Single(transform.Transitions);

        ToastStackMotion.SetIsEnabled(panel, false);
        Dispatcher.UIThread.RunJobs();

        Assert.False(ToastStackMotion.GetIsEnabled(panel));
        Assert.Null(transform.Transitions);
        Assert.Equal(0, transform.Y);
        window.Close();
    }
}
