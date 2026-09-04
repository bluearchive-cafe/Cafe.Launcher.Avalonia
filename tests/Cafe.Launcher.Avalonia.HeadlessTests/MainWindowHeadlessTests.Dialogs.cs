using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Cafe.Launcher.Avalonia.HeadlessTests;

public sealed partial class MainWindowHeadlessTests
{
    [AvaloniaFact]
    public void LogViewer_EmptyState_KeepsConfiguredHeight()
    {
        using var context = CreateContext();
        context.Window.Show();
        ShowLogViewer(context);

        var dialog = context.Window
            .GetVisualDescendants()
            .OfType<global::Cafe.Launcher.Avalonia.Controls.DialogSurface>()
            .Single(surface => ReferenceEquals(
                surface.CloseCommand,
                context.ViewModel.LogViewer.CloseCommand));
        var application = Application.Current
            ?? throw new InvalidOperationException("Headless application is not initialised.");
        Assert.True(application.TryGetResource(
            "Launcher.Layout.LogViewer.Height",
            application.ActualThemeVariant,
            out var configuredHeight));
        Assert.Equal(Assert.IsType<double>(configuredHeight), dialog.Bounds.Height);
    }

    [AvaloniaTheory]
    [InlineData("resource-panel")]
    [InlineData("log-viewer")]
    [InlineData("confirmation")]
    [InlineData("setup-wizard")]
    public void SecondaryOverlay_AtMinimumWindowSize_KeepsCriticalActionsReachable(string overlay)
    {
        using var context = CreateContext();
        context.Window.Width = 1024;
        context.Window.Height = 640;
        context.Window.Show();

        Button[] actions = overlay switch
        {
            "resource-panel" => ShowResourcePanel(context),
            "log-viewer" => ShowLogViewer(context),
            "confirmation" => ShowLongConfirmation(context),
            "setup-wizard" => ShowSetupWizard(context),
            _ => throw new ArgumentOutOfRangeException(nameof(overlay))
        };
        Dispatcher.UIThread.RunJobs();

        Assert.NotEmpty(actions);
        Assert.All(actions, action =>
        {
            Assert.True(action.IsEffectivelyVisible);
            Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(action)));
            AssertControlInsideWindow(action, context.Window);
        });
    }

    [AvaloniaFact]
    public void ModalIsolation_WhenConfirmationIsVisible_DisablesBackgroundAndRestoresItAfterClose()
    {
        using var context = CreateContext();
        context.Window.Show();
        context.ViewModel.Dialogs.ShowResourcePanelSourceConfirm("switch source");
        Dispatcher.UIThread.RunJobs();
        var settingsButton = context.Window
            .GetVisualDescendants()
            .OfType<Button>()
            .Single(button =>
                button.Classes.Contains("settings")
                && ReferenceEquals(
                    button.Command,
                    context.ViewModel.WindowChrome.ShowSettingsCommand));
        var startButton = context.Window
            .GetVisualDescendants()
            .OfType<Button>()
            .First(button =>
                button.Classes.Contains("primary-operation")
                && ReferenceEquals(
                    button.Command,
                    context.ViewModel.Operations.StartGameCommand));
        var cancelButton = context.Window
            .GetVisualDescendants()
            .OfType<Button>()
            .First(button =>
                button.IsEffectivelyVisible
                && ReferenceEquals(
                    button.Command,
                    context.ViewModel.Dialogs.CancelResourcePanelSourceSwitchCommand));

        Assert.False(settingsButton.IsEffectivelyEnabled);
        Assert.False(startButton.IsEffectivelyEnabled);
        Assert.True(cancelButton.IsEffectivelyEnabled);

        cancelButton.Command!.Execute(cancelButton.CommandParameter);
        Dispatcher.UIThread.RunJobs();

        Assert.True(settingsButton.IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public void ModalIsolation_WhenConfirmationCoversSettings_DisablesSettingsLayer()
    {
        using var context = CreateContext();
        context.Window.Show();
        context.ViewModel.WindowChrome.IsSettingsVisible = true;
        context.ViewModel.Dialogs.ShowRepairConfirm("repair confirmation");
        Dispatcher.UIThread.RunJobs();
        var settingsCancelButton = context.Window
            .GetVisualDescendants()
            .OfType<Button>()
            .Single(button =>
                button.Classes.Contains("dialog-action")
                && ReferenceEquals(
                    button.Command,
                    context.ViewModel.WindowChrome.ShowSettingsCommand));
        var confirmDialog = context.Window
            .GetVisualDescendants()
            .OfType<global::Cafe.Launcher.Avalonia.Controls.ConfirmDialog>()
            .Single(control => control.IsOpen);

        Assert.False(settingsCancelButton.IsEffectivelyEnabled);
        Assert.True(confirmDialog.IsEffectivelyEnabled);
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
        var surface = dialog
            .GetVisualDescendants()
            .OfType<global::Cafe.Launcher.Avalonia.Controls.DialogSurface>()
            .Single();

        Assert.True(surface.Bounds.Width <= 540);
        Assert.True(surface.Bounds.Height < 480);

        var supportText = surface
            .GetVisualDescendants()
            .OfType<TextBlock>()
            .Single(text => text.Name == "PART_BasicSupportTextBlock");
        Assert.False(supportText.IsVisible);
    }

    [AvaloniaFact]
    public void DesignGallery_WhenOpened_EnumeratesTokenGroupsFromResources()
    {
        using var context = CreateContext();
        context.Window.Show();

        context.ViewModel.Dialogs.Gallery.OpenCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.True(context.ViewModel.Dialogs.Gallery.IsVisible);
        Assert.True(context.ViewModel.Dialogs.Gallery.Groups.Count >= 12, $"Expected 12+ families, got {context.ViewModel.Dialogs.Gallery.Groups.Count}.");
        var totalItems = context.ViewModel.Dialogs.Gallery.Groups.Sum(group => group.Items.Count);
        Assert.True(totalItems >= 130, $"Expected 130+ tokens, got {totalItems}.");
        Assert.Contains(context.ViewModel.Dialogs.Gallery.Groups, group => group.Family == "Color");
        Assert.Contains(context.ViewModel.Dialogs.Gallery.Groups, group => group.Family == "Component");
        Assert.Contains(
            context.ViewModel.Dialogs.Gallery.Groups.SelectMany(group => group.Items),
            item => item.Key == "Launcher.Text.Primary");

        var gallerySurface = context.Window
            .GetVisualDescendants()
            .OfType<global::Cafe.Launcher.Avalonia.Controls.DialogSurface>()
            .Single(surface => ReferenceEquals(
                surface.CloseCommand,
                context.ViewModel.Dialogs.Gallery.CloseCommand));
        var scrollViewer = gallerySurface
            .GetVisualDescendants()
            .OfType<ScrollViewer>()
            .Single(control => control.Name == "PART_ScrollViewer");
        var contentPresenter = gallerySurface
            .GetVisualDescendants()
            .OfType<ContentPresenter>()
            .Single(control => control.Name == "PART_ScrollContentPresenter");
        var galleryContent = Assert.IsType<StackPanel>(contentPresenter.Content);
        var scrollTopLeft = scrollViewer.TranslatePoint(default, gallerySurface);
        var contentTopLeft = galleryContent.TranslatePoint(default, gallerySurface);
        Assert.NotNull(scrollTopLeft);
        Assert.NotNull(contentTopLeft);
        Assert.Equal(
            scrollTopLeft.Value.X + scrollViewer.Padding.Left,
            contentTopLeft.Value.X);

        context.ViewModel.Dialogs.Gallery.CloseCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(context.ViewModel.Dialogs.Gallery.IsVisible);
    }

    private static Button[] ShowResourcePanel(TestContext context)
    {
        context.ViewModel.ResourcePanel.IsResourcePanelVisible = true;
        Dispatcher.UIThread.RunJobs();
        return context.Window.GetVisualDescendants().OfType<Button>()
            .Where(button =>
                ReferenceEquals(button.Command, context.ViewModel.ResourcePanel.CloseResourcePanelCommand)
                || ReferenceEquals(button.Command, context.ViewModel.ResourcePanel.RefreshResourcePanelCommand)
                || ReferenceEquals(button.Command, context.ViewModel.ResourcePanel.SaveResourcePanelCommand))
            .ToArray();
    }

    private static Button[] ShowLogViewer(TestContext context)
    {
        context.ViewModel.LogViewer.IsVisible = true;
        Dispatcher.UIThread.RunJobs();
        return context.Window.GetVisualDescendants().OfType<Button>()
            .Where(button =>
                ReferenceEquals(button.Command, context.ViewModel.LogViewer.CloseCommand)
                || ReferenceEquals(button.Command, context.ViewModel.LogViewer.ExportCommand))
            .ToArray();
    }

    private static Button[] ShowLongConfirmation(TestContext context)
    {
        context.ViewModel.Dialogs.ShowRepairConfirm(string.Concat(Enumerable.Repeat(
            "下载源已切换，修复前需要重新确认本地文件状态。",
            30)));
        Dispatcher.UIThread.RunJobs();
        return context.Window.GetVisualDescendants().OfType<Button>()
            .Where(button =>
                (ReferenceEquals(button.Command, context.ViewModel.Dialogs.CancelRepairCommand)
                    || ReferenceEquals(button.Command, context.ViewModel.Dialogs.ConfirmRepairCommand))
                && button.IsEffectivelyVisible)
            .ToArray();
    }

    private static Button[] ShowSetupWizard(TestContext context)
    {
        context.ViewModel.Dialogs.ShowSetupWizard();
        Dispatcher.UIThread.RunJobs();
        return context.Window.GetVisualDescendants().OfType<Button>()
            .Where(button =>
                ReferenceEquals(button.Command, context.ViewModel.Dialogs.RequestSetupWizardExitCommand)
                || ReferenceEquals(button.Command, context.ViewModel.Dialogs.SetupWizard.NextCommand))
            .ToArray();
    }
}
