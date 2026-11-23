using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cafe.Launcher.Testing;
using Cafe.Launcher.UI.Constants;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.UI.Models;
using Microsoft.Extensions.DependencyInjection;
using FluentAvalonia.UI.Controls;

namespace Cafe.Launcher.HeadlessTests;

public sealed partial class MainWindowHeadlessTests
{
    [AvaloniaFact]
    public void Golden_ToastActions_MatchesBaseline()
    {
        using var context = CreateContext();
        PrepareGoldenWindow(context);
        context.Window.Show();
        context.Provider.GetRequiredService<ToastService>().Show(new ToastOptions
        {
            Title = "Update interrupted",
            Message = "The download was interrupted. Check your connection and try again.",
            Severity = ToastSeverity.Warning,
            Duration = ToastDuration.Extended,
            PrimaryAction = new ToastAction("Try again", _ => Task.FromResult(ToastActionResult.Success())),
            SecondaryAction = new ToastAction("View log", _ => Task.FromResult(ToastActionResult.Success()))
        });
        GoldenScreenshot.Compare(context.Window, "toast-actions");
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

        var infoBar = Assert.Single(context.Window.GetVisualDescendants().OfType<FAInfoBar>());
        Assert.True(infoBar.IsOpen);
        // 严重级别不再交给 InfoBar 换底色：卡片固定资讯级表面，等级由语义条承载。
        Assert.Equal(FAInfoBarSeverity.Informational, infoBar.Severity);
        var card = context.Window.GetVisualDescendants().OfType<Border>()
            .Single(control => control.Classes.Contains("toast-card"));
        var bar = card.GetVisualDescendants().OfType<Border>()
            .Single(control => control.Classes.Contains("toast-accent-bar"));
        Assert.True(context.Window.TryFindResource("Launcher.Color.Danger", out var dangerResource));
        Assert.Equal(
            Assert.IsType<SolidColorBrush>(dangerResource).Color,
            Assert.IsType<SolidColorBrush>(bar.Background).Color);
        var actionButtons = context.Window.GetVisualDescendants().OfType<Button>()
            .Where(control =>
                control.Classes.Contains("toast-primary-action")
                || control.Classes.Contains("toast-secondary-action"))
            .ToArray();
        var closeButton = context.Window.GetVisualDescendants().OfType<Button>()
            .Single(control => control.Name == "CloseButton" && control.GetVisualAncestors().Contains(infoBar));
        var actionProgress = context.Window.GetVisualDescendants().OfType<ProgressBar>()
            .Single(control => control.Classes.Contains("toast-progress"));

        Assert.Equal("Install failed", infoBar.Title);
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
        await executeTask.WaitAsync(TimeSpan.FromSeconds(5));
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

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void DefaultToast_UsesNeutralCardAndSemanticAccentBar(bool dark)
    {
        using var variant = ThemeVariantSnapshot.Capture(dark ? ThemeVariant.Dark : ThemeVariant.Light);
        using var context = CreateContext();
        context.Window.Show();
        // 省略 Severity 即默认资讯级。卡片底色改为中性实心，严重级别只由左缘语义条表达。
        context.Provider.GetRequiredService<ToastService>().Show(new ToastOptions
        {
            Title = "Info",
            Message = "Default severity",
            Duration = ToastDuration.Extended
        });
        Dispatcher.UIThread.RunJobs();

        var infoBar = Assert.Single(context.Window.GetVisualDescendants().OfType<FAInfoBar>());
        Assert.Equal(FAInfoBarSeverity.Informational, infoBar.Severity);

        // 模板整卡底色 = 资讯级中性表面；卡片外壳不再自带底色，严重级别只由语义条承载。
        var contentRoot = infoBar.GetVisualDescendants().OfType<Border>()
            .Single(border => border.Name == "ContentRoot");
        var card = context.Window.GetVisualDescendants().OfType<Border>()
            .Single(control => control.Classes.Contains("toast-card"));
        var contentSurface = Assert.IsAssignableFrom<ISolidColorBrush>(contentRoot.Background);
        Assert.Equal((byte)255, contentSurface.Color.A);

        var bar = card.GetVisualDescendants().OfType<Border>()
            .Single(control => control.Classes.Contains("toast-accent-bar"));
        var barBrush = Assert.IsType<SolidColorBrush>(bar.Background);
        Assert.True(context.Window.TryFindResource("Launcher.Color.Info", out var infoResource));
        var info = Assert.IsType<SolidColorBrush>(infoResource);
        Assert.Equal(info.Color, barBrush.Color);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Toast_WhileHoveredOrFocused_SuspendsDisplayCountdown(bool withKeyboardFocus)
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

        if (withKeyboardFocus)
        {
            var close = card.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "CloseButton");
            Assert.True(close.Focus(NavigationMethod.Tab));
            Dispatcher.UIThread.RunJobs();
            Assert.True(context.ViewModel.Toasts.IsCountdownSuspended(toastId));
        }

        context.Window.MouseMove(
            cardTopLeft.Value + new Point(card.Bounds.Width / 2, card.Bounds.Height / 2),
            RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.True(context.ViewModel.Toasts.IsCountdownSuspended(toastId));
        Assert.Single(context.ViewModel.Toasts.ActiveToasts);

        context.Window.MouseMove(new Point(0, 0), RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(withKeyboardFocus, context.ViewModel.Toasts.IsCountdownSuspended(toastId));
        if (withKeyboardFocus)
        {
            var launcherSettings = context.Window.GetVisualDescendants().OfType<Button>()
                .Single(button => button.Classes.Contains("rail-action")
                    && ReferenceEquals(button.Command, context.ViewModel.WindowChrome.ToggleSettingsCommand));
            Assert.True(launcherSettings.Focus(NavigationMethod.Tab));
            Dispatcher.UIThread.RunJobs();
            Assert.False(context.ViewModel.Toasts.IsCountdownSuspended(toastId));
        }

        Assert.Single(context.ViewModel.Toasts.ActiveToasts);
    }

    [AvaloniaFact]
    public void Toast_CardStrokeIsNeutralAndAccentBarCarriesSeverity()
    {
        using var context = CreateContext();
        var toastService = context.Provider.GetRequiredService<ToastService>();
        context.Window.Show();
        toastService.Show(new ToastOptions
        {
            Message = "solid surface",
            Severity = ToastSeverity.Warning,
            Duration = ToastDuration.Extended
        });
        Dispatcher.UIThread.RunJobs();

        var card = context.Window.GetVisualDescendants().OfType<Border>()
            .Single(control => control.Classes.Contains("toast-card"));
        var infoBar = context.Window.GetVisualDescendants().OfType<FAInfoBar>().Single();
        var contentRoot = infoBar.GetVisualDescendants().OfType<Border>()
            .Single(border => border.Name == "ContentRoot");

        // FAInfoBar 模板的底色与描边都退场；外框中性 1 DIP 收边，严重级别移到语义条。
        Assert.Equal(new Thickness(0), contentRoot.BorderThickness);
        Assert.Equal(new Thickness(1), card.BorderThickness);
        Assert.True(context.Window.TryFindResource("DividerStrokeColorDefaultBrush", out var strokeResource));
        var stroke = Assert.IsType<SolidColorBrush>(card.BorderBrush);
        Assert.Equal(Assert.IsType<SolidColorBrush>(strokeResource).Color, stroke.Color);

        var bar = card.GetVisualDescendants().OfType<Border>()
            .Single(control => control.Classes.Contains("toast-accent-bar"));
        var barBrush = Assert.IsType<SolidColorBrush>(bar.Background);
        Assert.True(context.Window.TryFindResource("Launcher.Color.Warning", out var warningResource));
        var warning = Assert.IsType<SolidColorBrush>(warningResource);
        Assert.Equal(warning.Color, barBrush.Color);
    }

    [AvaloniaFact]
    public void Toast_WithSameMergeKey_RendersOneCardWithCountSuffix()
    {
        using var context = CreateContext();
        var localizer = context.Provider.GetRequiredService<LocalizationService>();
        var toastService = context.Provider.GetRequiredService<ToastService>();
        context.Window.Show();
        toastService.Show(new ToastOptions
        {
            Message = "first failure",
            Key = "probe-key",
            Duration = ToastDuration.Extended
        });
        Dispatcher.UIThread.RunJobs();
        toastService.Show(new ToastOptions
        {
            Message = "second failure",
            Key = "probe-key",
            Duration = ToastDuration.Extended
        });
        Dispatcher.UIThread.RunJobs();

        var infoBar = Assert.Single(context.Window.GetVisualDescendants().OfType<FAInfoBar>());
        Assert.Equal("second failure", infoBar.Message);
        Assert.Equal(
            localizer.T(LocalizationKeys.ToastInfo)
                + string.Format(
                    CultureInfo.CurrentCulture,
                    localizer.T(LocalizationKeys.ToastMergeCountSuffix),
                    2),
            infoBar.Title);
    }

    [AvaloniaFact]
    public async Task ToastCommands_AfterHostDisposal_DoNotThrow()
    {
        using var context = CreateContext();
        var toastService = context.Provider.GetRequiredService<ToastService>();
        context.Window.Show();
        toastService.Show(new ToastOptions
        {
            Title = "Install failed",
            Message = "Offline",
            Severity = ToastSeverity.Error,
            PrimaryAction = new ToastAction(
                "Retry",
                _ => Task.FromResult(ToastActionResult.Success()))
        });
        Dispatcher.UIThread.RunJobs();

        var toasts = context.ViewModel.Toasts;
        var toastId = toasts.ActiveToasts.Single().Id;

        // 关闭顺序不保证命令都先于宿主结束：已排队的迟到命令不得再读释放后的令牌源。
        toasts.Dispose();

        await toasts.ExecutePrimaryToastActionCommand.ExecuteAsync(toastId);
        await toasts.DismissToastCommand.ExecuteAsync(toastId);
        toasts.Dispose();
    }
}
