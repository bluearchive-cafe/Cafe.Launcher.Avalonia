using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cafe.Launcher.Avalonia.Controls;

namespace Cafe.Launcher.Avalonia.HeadlessTests;

public sealed class DialogSurfaceHeadlessTests
{
    private sealed class RecorderCommand : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public int ExecuteCount { get; private set; }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => ExecuteCount++;
    }

    [AvaloniaFact]
    public void DefaultForm_ShowsBasicChromeOnly()
    {
        var content = new TextBlock { Text = "Decision copy" };
        var surface = new DialogSurface { Content = content };
        var window = new Window { Content = surface };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var basicHead = FindPart<Border>(surface, "PART_BasicHead");
        var panelHead = FindPart<Border>(surface, "PART_PanelHead");
        var closeButton = FindPart<Button>(surface, "PART_CloseButton");
        var scrollViewer = FindPart<ScrollViewer>(surface, "PART_ScrollViewer");
        var scrollContent = FindPart<ContentPresenter>(surface, "PART_ScrollContentPresenter");
        var directContent = FindPart<ContentPresenter>(surface, "PART_DirectContentPresenter");
        var surfaceBorder = FindPart<Border>(surface, "PART_SurfaceBorder");

        Assert.True(basicHead.IsVisible);
        Assert.False(panelHead.IsVisible);
        // 无命令出口时关闭钮必须隐藏；Basic 形态的动作即出口。
        Assert.False(closeButton.IsVisible);
        Assert.DoesNotContain(":panel", surface.Classes);
        Assert.True(scrollViewer.IsVisible);
        Assert.False(directContent.IsVisible);
        Assert.Same(content, scrollContent.Content);
        Assert.Equal(new Thickness(28, 0, 28, 0), scrollViewer.Padding);
        Assert.True(surfaceBorder.ClipToBounds);
        window.Close();
    }

    [AvaloniaFact]
    public void PanelForm_WithCloseCommand_ShowsHeaderChromeAndBindsCommand()
    {
        var closeCommand = new RecorderCommand();
        var content = new TextBlock { Text = "Panel copy" };
        var surface = new DialogSurface
        {
            Form = DialogSurfaceForm.Panel,
            Title = "Session log",
            Subtitle = "launcher.log · 214 entries",
            CloseCommand = closeCommand,
            CloseAutomationName = "Close log viewer",
            Content = content
        };
        var window = new Window { Content = surface };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var panelHead = FindPart<Border>(surface, "PART_PanelHead");
        var basicHead = FindPart<Border>(surface, "PART_BasicHead");
        var closeButton = FindPart<Button>(surface, "PART_CloseButton");
        var titleText = FindPart<TextBlock>(surface, "PART_TitleTextBlock");
        var scrollViewer = FindPart<ScrollViewer>(surface, "PART_ScrollViewer");
        var scrollContent = FindPart<ContentPresenter>(surface, "PART_ScrollContentPresenter");

        Assert.True(panelHead.IsVisible);
        Assert.Contains(":panel", surface.Classes);
        Assert.False(basicHead.IsVisible);
        Assert.True(closeButton.IsVisible);
        Assert.Same(closeCommand, closeButton.Command);
        var titleTopLeft = titleText.TranslatePoint(default, panelHead);
        Assert.NotNull(titleTopLeft);
        Assert.Equal(panelHead.Padding.Left, titleTopLeft.Value.X);
        Assert.True(scrollViewer.IsVisible);
        Assert.Same(content, scrollContent.Content);
        Assert.Equal(new Thickness(24, 18, 24, 18), scrollViewer.Padding);

        closeButton.Command!.Execute(null);
        Assert.Equal(1, closeCommand.ExecuteCount);
        window.Close();
    }

    [AvaloniaFact]
    public void PanelForm_FooterSlots_RenderLeadingOnlyWhenProvided()
    {
        var copyButton = new Button { Width = 140 };
        var leading = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Children =
            {
                new Button { Width = 114 },
                copyButton
            }
        };
        var primaryButton = new Button { Width = 108 };
        var actions = new StackPanel { Children = { primaryButton } };
        var surface = new DialogSurface
        {
            Form = DialogSurfaceForm.Panel,
            Width = 424,
            Footer = actions
        };
        var window = new Window { Content = surface };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var leadingPresenter = FindPart<ContentPresenter>(surface, "PART_FooterLeadingPresenter");
        var footerPresenter = FindPart<ContentPresenter>(surface, "PART_FooterPresenter");

        Assert.Same(actions, footerPresenter.Content);
        Assert.False(leadingPresenter.IsVisible);

        surface.FooterLeading = leading;
        Dispatcher.UIThread.RunJobs();
        Assert.True(leadingPresenter.IsVisible);
        Assert.Same(leading, leadingPresenter.Content);

        var copyTopLeft = copyButton.TranslatePoint(default, surface);
        var primaryTopLeft = primaryButton.TranslatePoint(default, surface);
        Assert.NotNull(copyTopLeft);
        Assert.NotNull(primaryTopLeft);
        var crossSlotGap = primaryTopLeft.Value.X - (copyTopLeft.Value.X + copyButton.Bounds.Width);
        Assert.True(crossSlotGap >= 12, $"Expected a 12px footer slot gap, got {crossSlotGap}px.");
        window.Close();
    }

    [AvaloniaFact]
    public void BadgeAndStatus_Modifiers_ToggleVisibilityAndPseudoClasses()
    {
        var badgeIcon = new TextBlock { Text = "≡" };
        var surface = new DialogSurface
        {
            Form = DialogSurfaceForm.Panel,
            HeaderIcon = badgeIcon
        };
        var window = new Window { Content = surface };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var badgePresenter = FindPart<ContentPresenter>(surface, "PART_BadgePresenter");
        Assert.Same(badgeIcon, badgePresenter.Content);
        Assert.True(badgePresenter.IsVisible);
        Assert.DoesNotContain(":danger", surface.Classes);

        surface.Status = DialogSurfaceStatus.Danger;
        Assert.Contains(":danger", surface.Classes);

        surface.Status = DialogSurfaceStatus.Info;
        Assert.Contains(":info", surface.Classes);
        Assert.DoesNotContain(":danger", surface.Classes);

        surface.HeaderIcon = null;
        Dispatcher.UIThread.RunJobs();
        Assert.False(badgePresenter.IsVisible);
        window.Close();
    }

    private static T FindPart<T>(DialogSurface surface, string name)
        where T : Control =>
        surface.GetVisualDescendants()
            .OfType<T>()
            .Single(control => control.Name == name);
}
