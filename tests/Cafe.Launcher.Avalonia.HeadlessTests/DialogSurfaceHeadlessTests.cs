using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
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
        var surface = new DialogSurface();
        var window = new Window { Content = surface };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var basicHead = FindPart<Border>(surface, "PART_BasicHead");
        var panelHead = FindPart<Border>(surface, "PART_PanelHead");
        var closeButton = FindPart<Button>(surface, "PART_CloseButton");

        Assert.True(basicHead.IsVisible);
        Assert.False(panelHead.IsVisible);
        // 无命令出口时关闭钮必须隐藏；Basic 形态的动作即出口。
        Assert.False(closeButton.IsVisible);
        Assert.DoesNotContain(":panel", surface.Classes);
        window.Close();
    }

    [AvaloniaFact]
    public void PanelForm_WithCloseCommand_ShowsHeaderChromeAndBindsCommand()
    {
        var closeCommand = new RecorderCommand();
        var surface = new DialogSurface
        {
            Form = DialogSurfaceForm.Panel,
            Title = "Session log",
            Subtitle = "launcher.log · 214 entries",
            CloseCommand = closeCommand,
            CloseAutomationName = "Close log viewer"
        };
        var window = new Window { Content = surface };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var panelHead = FindPart<Border>(surface, "PART_PanelHead");
        var basicHead = FindPart<Border>(surface, "PART_BasicHead");
        var closeButton = FindPart<Button>(surface, "PART_CloseButton");

        Assert.True(panelHead.IsVisible);
        Assert.Contains(":panel", surface.Classes);
        Assert.False(basicHead.IsVisible);
        Assert.True(closeButton.IsVisible);
        Assert.Same(closeCommand, closeButton.Command);

        closeButton.Command!.Execute(null);
        Assert.Equal(1, closeCommand.ExecuteCount);
        window.Close();
    }

    [AvaloniaFact]
    public void PanelForm_FooterSlots_RenderLeadingOnlyWhenProvided()
    {
        var leading = new StackPanel();
        var actions = new StackPanel();
        var surface = new DialogSurface
        {
            Form = DialogSurfaceForm.Panel,
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
