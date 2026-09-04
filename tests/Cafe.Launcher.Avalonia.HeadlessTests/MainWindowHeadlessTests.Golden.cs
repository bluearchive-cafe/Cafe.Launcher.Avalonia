using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.HeadlessTests;

public sealed partial class MainWindowHeadlessTests
{
    [AvaloniaFact]
    public void Golden_ShellDefault_MatchesBaseline()
    {
        using var context = CreateContext();
        PrepareGoldenWindow(context);
        context.Window.Show();
        GoldenScreenshot.Compare(context.Window, "shell-default");
    }

    [AvaloniaFact]
    public void Golden_ProgressPanel_MatchesBaseline()
    {
        using var context = CreateContext();
        PrepareGoldenWindow(context);
        context.ViewModel.Operations.PanelMode = GameOperationPanelMode.Progress;
        context.Window.Show();
        GoldenScreenshot.Compare(context.Window, "progress-panel");
    }

    [AvaloniaFact]
    public void Golden_SettingsOverlay_MatchesBaseline()
    {
        using var context = CreateContext();
        PrepareGoldenWindow(context);
        context.Window.Show();
        OpenSettings(context);
        GoldenScreenshot.Compare(context.Window, "settings-overlay");
    }

    [AvaloniaFact]
    public void Golden_ConfirmDialog_MatchesBaseline()
    {
        using var context = CreateContext();
        PrepareGoldenWindow(context);
        context.Window.Show();
        context.ViewModel.Dialogs.ShowRepairConfirm("golden repair confirmation");
        Dispatcher.UIThread.RunJobs();
        GoldenScreenshot.Compare(context.Window, "confirm-dialog");
    }

    [AvaloniaFact]
    public void Golden_Toast_MatchesBaseline()
    {
        using var context = CreateContext();
        PrepareGoldenWindow(context);
        context.Window.Show();
        context.ViewModel.Debug.TestToastCommand.Execute("Info");
        Dispatcher.UIThread.RunJobs();
        GoldenScreenshot.Compare(context.Window, "toast");
    }

    private static void PrepareGoldenWindow(TestContext context)
    {
        context.ViewModel.IsMotionReduced = true;
        context.Window.FontFamily = new FontFamily("Segoe UI");
    }
}
