using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.HeadlessTests;

public sealed partial class MainWindowHeadlessTests
{
    [AvaloniaFact]
    public async Task Golden_ShellDefault_MatchesBaseline()
    {
        using var context = CreateContext();
        await ShowGoldenWindowAsync(context);
        GoldenScreenshot.Compare(context.Window, "shell-default");
    }

    [AvaloniaFact]
    public async Task Golden_ProgressPanel_MatchesBaseline()
    {
        using var context = CreateContext();
        context.ViewModel.Operations.PanelMode = GameOperationPanelMode.Progress;
        await ShowGoldenWindowAsync(context);
        GoldenScreenshot.Compare(context.Window, "progress-panel");
    }

    [AvaloniaFact]
    public async Task Golden_SettingsOverlay_MatchesBaseline()
    {
        using var context = CreateContext();
        await ShowGoldenWindowAsync(context);
        OpenSettings(context);
        GoldenScreenshot.Compare(context.Window, "settings-overlay");
    }

    [AvaloniaFact]
    public async Task Golden_ConfirmDialog_MatchesBaseline()
    {
        using var context = CreateContext();
        await ShowGoldenWindowAsync(context);
        context.ViewModel.Dialogs.ShowRepairConfirm("golden repair confirmation");
        Dispatcher.UIThread.RunJobs();
        GoldenScreenshot.Compare(context.Window, "confirm-dialog");
    }

    [AvaloniaFact]
    public async Task Golden_Toast_MatchesBaseline()
    {
        using var context = CreateContext();
        await ShowGoldenWindowAsync(context);
        context.ViewModel.Debug.TestToastCommand.Execute("Info");
        Dispatcher.UIThread.RunJobs();
        GoldenScreenshot.Compare(context.Window, "toast");
    }

    /// <summary>
    /// Shows the golden window and loads the initial wallpaper the way the app does.
    /// The bundled image is no longer decoded in the view-model constructor (that ran
    /// synchronously on the UI thread before the first paint), so the golden drives the
    /// first refresh explicitly to capture the same post-startup state.
    /// </summary>
    private static async Task ShowGoldenWindowAsync(TestContext context)
    {
        context.ViewModel.IsMotionReduced = true;
        context.Window.FontFamily = new FontFamily("Segoe UI");
        context.Window.Show();
        await context.ViewModel.Background.UpdateBackgroundImageAsync(
            new LauncherSettings { BackgroundSource = BackgroundSources.Bundled },
            snapshot: null,
            CancellationToken.None);
        Dispatcher.UIThread.RunJobs();
    }
}
