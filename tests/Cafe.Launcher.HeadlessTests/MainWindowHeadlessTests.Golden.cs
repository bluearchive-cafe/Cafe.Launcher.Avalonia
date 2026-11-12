using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Services.Update;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Cafe.Launcher.HeadlessTests;

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
    public void Golden_LaunchSplitButton_MatchesBaseline()
    {
        using var context = CreateContext();
        PrepareGoldenWindow(context);
        ShowLaunchSplitButton(context);
        GoldenScreenshot.Compare(context.Window, "launch-split-button");
    }

    [AvaloniaFact]
    public void Golden_LaunchSplitMenu_MatchesBaseline()
    {
        using var context = CreateContext();
        PrepareGoldenWindow(context);
        var split = ShowLaunchSplitButton(context);
        var menu = Assert.IsType<MenuFlyout>(split.Flyout);
        // 将弹窗放入同一渲染树，截图才能同时捕获按钮、间距与菜单。
        menu.Popup.ShouldUseOverlayLayer = true;
        try
        {
            context.ViewModel.Shell.ApplyLanguage(LauncherLanguages.SimplifiedChinese, hasSnapshot: false);
            menu.ShowAt(split);
            GoldenScreenshot.Compare(context.Window, "launch-split-menu");
        }
        finally
        {
            menu.Hide();
            context.ViewModel.Shell.ApplyLanguage(LauncherLanguages.English, hasSnapshot: false);
        }
    }

    [AvaloniaFact]
    public void Golden_LaunchSplitButtonDarkChinese_MatchesBaseline()
    {
        using var variant = ThemeVariantSnapshot.Capture(ThemeVariant.Dark);
        using var context = CreateContext();
        PrepareGoldenWindow(context, ThemeVariant.Dark);
        try
        {
            context.ViewModel.Shell.ApplyLanguage(LauncherLanguages.SimplifiedChinese, hasSnapshot: false);
            context.Provider.GetRequiredService<ThemeApplier>().ApplyScheme(Color.Parse("#FF2E7DF6"), isDark: true);
            ShowLaunchSplitButton(context, hideDetails: true);
            GoldenScreenshot.Compare(context.Window, "launch-split-button-dark-zh");
        }
        finally
        {
            context.ViewModel.Shell.ApplyLanguage(LauncherLanguages.English, hasSnapshot: false);
        }
    }

    [AvaloniaFact]
    public void Golden_ProgressPanel_MatchesBaseline()
    {
        using var context = CreateContext();
        PrepareGoldenWindow(context);
        context.ViewModel.Operations.ApplyProgress(new GameOperationProgress
        { OperationKind = GameOperationKind.Download, Stage = GameOperationStage.Downloading, Progress = 42,
          DownloadedSize = 1024 * 1024, TotalSize = 10 * 1024 * 1024, BytesPerSecond = 2048, CanPause = true });
        context.ViewModel.Operations.ShowTaskCommand.Execute(null);
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
        await context.ViewModel.LogExport.PendingRangeProbeTask.WaitAsync(TimeSpan.FromSeconds(5));
        Dispatcher.UIThread.RunJobs();
        GoldenScreenshot.Compare(context.Window, "log-export");
    }

    [AvaloniaFact]
    public void Golden_UpdateDialog_MatchesBaseline()
    {
        using var context = CreateContext();
        PrepareGoldenWindow(context);
        context.Window.Show();
        context.ViewModel.Dialogs.LauncherUpdate.Show(
            "1.2.0",
            [],
            LauncherUpdateInAppAvailability.PlatformUnsupported,
            releaseNotes: "## What's new\n\n- Fixed several issues\n- Improved download stability");
        Dispatcher.UIThread.RunJobs();
        GoldenScreenshot.Compare(context.Window, "update-dialog");
    }

    [AvaloniaFact]
    public async Task Golden_UpdateDialog_LoadingAndFailure_MatchBaselines()
    {
        using var context = CreateContext();
        PrepareGoldenWindow(context);
        context.Window.Show();
        context.ViewModel.Dialogs.LauncherUpdate.Show("1.2.0", [], LauncherUpdateInAppAvailability.Available);
        var service = new ControlledReleaseNotesService();
        var loading = context.ViewModel.Dialogs.LauncherUpdate.LoadReleaseNotesAsync(service);
        GoldenScreenshot.Compare(context.Window, "update-dialog-loading");

        service.Result.SetResult("");
        await loading;
        GoldenScreenshot.Compare(context.Window, "update-dialog-failed");
    }

    [AvaloniaFact]
    public void Golden_ErrorDialog_MatchesBaseline()
    {
        using var context = CreateContext();
        PrepareGoldenWindow(context);
        context.Window.Show();
        context.ViewModel.Dialogs.ShowCriticalError(
            "The launcher could not start the game.",
            "Exit code 1 while launching BlueArchive.exe.");
        Dispatcher.UIThread.RunJobs();
        GoldenScreenshot.Compare(context.Window, "error-dialog");
    }

    [AvaloniaFact]
    public void Golden_SetupWizard_MatchesBaseline()
    {
        using var context = CreateContext();
        PrepareGoldenWindow(context);
        context.Window.Show();
        context.ViewModel.Dialogs.ShowSetupWizard();
        Dispatcher.UIThread.RunJobs();
        // 向导的「自动」会恢复 DI 创建时捕获的语言，截图仍须钉住英文。
        // 欢迎页是首步，截图取欢迎页；语言页的 ComboBox 在后续步骤完成首次绑定。
        context.ViewModel.Shell.ApplyLanguage(LauncherLanguages.English, hasSnapshot: false);
        Dispatcher.UIThread.RunJobs();
        GoldenScreenshot.Compare(context.Window, "setup-wizard");
    }

    [AvaloniaFact]
    public void Golden_SetupWizardAgreement_MatchesBaseline()
    {
        using var context = CreateContext();
        PrepareGoldenWindow(context);
        context.Window.Show();
        context.ViewModel.Dialogs.ShowSetupWizard();
        Dispatcher.UIThread.RunJobs();
        // 与向导总览同序：先让「自动」完成首次绑定，再钉住英文，最后进入协议页。
        // 语言必须早于文档首次排版，否则 Markdown 表面的字体族会停在「自动」时的
        // 系统语言上，截图取决于本机区域设置与重排时机。
        context.ViewModel.Shell.ApplyLanguage(LauncherLanguages.English, hasSnapshot: false);
        Dispatcher.UIThread.RunJobs();
        context.ViewModel.Dialogs.SetupWizard.NextCommand.Execute(null);
        context.ViewModel.Dialogs.SetupWizard.NextCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        GoldenScreenshot.Compare(context.Window, "setup-wizard-agreement");
    }

    [AvaloniaFact]
    public void Golden_SetupWizardAgreementReadDarkChinese_MatchesBaseline()
    {
        using var variant = ThemeVariantSnapshot.Capture(ThemeVariant.Dark);
        using var context = CreateContext();
        PrepareGoldenWindow(context, ThemeVariant.Dark);
        context.Window.Show();
        context.ViewModel.Dialogs.ShowSetupWizard();
        Dispatcher.UIThread.RunJobs();
        try
        {
            // 向导的语言预览是唯一会把界面切到所选语言的路径；直调 Shell.ApplyLanguage
            // 会在向导语言仍为「自动」时被下一次刷新覆盖回系统语言。先切语言再进入
            // 协议页，文档按中文排版，不受宿主区域设置影响。
            context.ViewModel.Dialogs.SetupWizard.Language = LauncherLanguages.SimplifiedChinese;
            context.Provider.GetRequiredService<ThemeApplier>().ApplyScheme(Color.Parse("#FF2E7DF6"), isDark: true);
            Dispatcher.UIThread.RunJobs();
            context.ViewModel.Dialogs.SetupWizard.NextCommand.Execute(null);
            context.ViewModel.Dialogs.SetupWizard.NextCommand.Execute(null);
            context.ViewModel.Dialogs.SetupWizard.MarkAgreementRead();
            context.ViewModel.Dialogs.SetupWizard.IsAgreementAccepted = true;
            Dispatcher.UIThread.RunJobs();
            GoldenScreenshot.Compare(context.Window, "setup-wizard-agreement-read-dark-zh");
        }
        finally
        {
            context.ViewModel.Shell.ApplyLanguage(LauncherLanguages.English, hasSnapshot: false);
        }
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
