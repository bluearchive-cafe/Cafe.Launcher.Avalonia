using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
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
        context.ViewModel.Dialogs.RepairConfirm.Show("golden repair confirmation");
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

    [AvaloniaFact]
    public async Task Golden_LogExportDialog_MatchesBaseline()
    {
        using var context = CreateContext();
        PrepareGoldenWindow(context);
        context.Window.Show();
        // Refresh the log export dialog's localized display names directly; the shell's
        // names only; feature dialogs are refreshed by ShellLifecycle during real startup.
        context.ViewModel.LogExport.RefreshLocalizedText();
        context.ViewModel.LogExport.OpenCommand.Execute(null);
        await context.ViewModel.LogExport.PendingRangeProbeTask;
        Dispatcher.UIThread.RunJobs();
        GoldenScreenshot.Compare(context.Window, "log-export");
    }

    /// <summary>
    /// AUD-TEST-013 回归守卫：golden 的渲染输入必须与用例顺序无关。前一个用例把变体留在暗色时，
    /// <see cref="PrepareGoldenWindow"/> 仍须把 golden 拉回基线变体——少了这条钉住，golden 截到
    /// 亮色还是暗色就取决于同批次谁先跑（「偶然绿」）。哨兵法：先造出泄漏态，再断言基线胜出。
    /// </summary>
    [AvaloniaFact]
    public void GoldenPrep_WhenTheAmbientVariantWasLeftDark_StillPinsTheBaselineVariant()
    {
        var application = Application.Current
            ?? throw new InvalidOperationException("Headless application is not initialised.");
        using var leakedVariant = ThemeVariantSnapshot.Capture(ThemeVariant.Dark);
        using var context = CreateContext();

        PrepareGoldenWindow(context);

        Assert.Equal(ThemeVariant.Light, application.RequestedThemeVariant);
    }

    /// <summary>
    /// 固定 golden 渲染的全部确定性输入：动效、字体与主题变体。
    /// </summary>
    /// <remarks>
    /// 变体必须每次在这里显式钉住，不能听任环境：无头套件共享一个 Application，任何改过
    /// <c>RequestedThemeVariant</c> 的用例都会把值留给后续用例，于是 golden 截到亮色还是暗色
    /// 取决于谁先跑（AUD-TEST-013）。这里设定后**不还原**——它是整套 golden 的基线值，不是某个
    /// 用例的临时覆盖；临时覆盖的用例自己用 <see cref="ThemeVariantSnapshot"/> 还原。
    /// 默认亮色：无头平台的 <c>ThemeVariant.Default</c> 就解析为亮色，显式写出来是为了不再依赖解析。
    /// </remarks>
    private static void PrepareGoldenWindow(
        TestContext context,
        ThemeVariant? variant = null)
    {
        if (Application.Current is { } application)
        {
            application.RequestedThemeVariant = variant ?? ThemeVariant.Light;
        }

        context.ViewModel.IsMotionReduced = true;
        context.Window.FontFamily = new FontFamily("Segoe UI");
    }
}
