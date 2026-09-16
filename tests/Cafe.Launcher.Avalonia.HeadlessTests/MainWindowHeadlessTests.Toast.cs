using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Cafe.Launcher.Avalonia.HeadlessTests;

public sealed partial class MainWindowHeadlessTests
{
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
        Assert.True(actionProgress.IsIndeterminate);
        Assert.False(actionProgress.IsVisible);

        var executeTask = context.ViewModel.Toasts.ExecutePrimaryToastActionCommand.ExecuteAsync(
            context.ViewModel.Toasts.ActiveToasts.Single().Id);
        Dispatcher.UIThread.RunJobs();

        Assert.All(actionButtons, button => Assert.False(button.IsEffectivelyEnabled));
        Assert.True(closeButton.IsEffectivelyEnabled);
        Assert.True(actionProgress.IsEffectivelyVisible);

        release.SetResult(ToastActionResult.Failure("Still offline", "Retry failed"));
        await executeTask;
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Toast_WithoutActions_ShowsNoProgressBar()
    {
        using var context = CreateContext();
        var toastService = context.Provider.GetRequiredService<ToastService>();
        context.Window.Show();
        toastService.Show(new ToastOptions
        {
            Title = "Updated",
            Message = "You are up to date.",
            Duration = ToastDuration.Extended
        });
        Dispatcher.UIThread.RunJobs();

        var visibleProgress = context.Window.GetVisualDescendants().OfType<ProgressBar>()
            .Where(control => control.Classes.Contains("toast-progress") && control.IsVisible)
            .ToArray();

        Assert.Empty(visibleProgress);
    }

    [AvaloniaFact]
    public void Toast_WhilePointerRestsOnTheCard_SuspendsDisplayCountdown()
    {
        using var context = CreateContext();
        var toastService = context.Provider.GetRequiredService<ToastService>();
        context.Window.Show();
        toastService.Show(new ToastOptions
        {
            Title = "Updated",
            Message = "You are up to date.",
            Duration = ToastDuration.Extended
        });
        Dispatcher.UIThread.RunJobs();

        var card = context.Window.GetVisualDescendants().OfType<Border>()
            .Single(control => control.Classes.Contains("toast-card"));
        var toastId = context.ViewModel.Toasts.ActiveToasts.Single().Id;
        var cardTopLeft = card.TranslatePoint(default, context.Window);
        Assert.NotNull(cardTopLeft);
        Assert.False(context.ViewModel.Toasts.IsCountdownSuspended(toastId));

        context.Window.MouseMove(
            cardTopLeft.Value + new Point(card.Bounds.Width / 2, card.Bounds.Height / 2),
            RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.True(context.ViewModel.Toasts.IsCountdownSuspended(toastId));
        Assert.Single(context.ViewModel.Toasts.ActiveToasts);

        context.Window.MouseMove(new Point(0, 0), RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.False(context.ViewModel.Toasts.IsCountdownSuspended(toastId));
        Assert.Single(context.ViewModel.Toasts.ActiveToasts);
    }
}
