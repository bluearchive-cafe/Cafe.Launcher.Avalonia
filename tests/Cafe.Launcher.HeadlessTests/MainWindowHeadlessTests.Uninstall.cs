using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.UI;
using Cafe.Launcher.UI.Controls;
using Cafe.Launcher.UI.Features.GameOperations;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Cafe.Launcher.HeadlessTests;

public sealed partial class MainWindowHeadlessTests
{
    [AvaloniaFact]
    public void UninstallDialog_WhenOpened_FocusesCancelAndEnterDoesNotDelete()
    {
        using var context = CreateContext();
        context.ViewModel.IsMotionReduced = true;
        context.Window.Show();
        context.ViewModel.Operations.Uninstall.Open(new LauncherStatusSnapshot());
        Dispatcher.UIThread.RunJobs();
        var focused = Assert.IsType<Button>(context.Window.FocusManager!.GetFocusedElement());
        Assert.Equal("UninstallCancelButton", focused.Name);
        context.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "");
        context.Window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "");
        Dispatcher.UIThread.RunJobs();
        Assert.False(context.ViewModel.Operations.Uninstall.IsVisible);
        Assert.False(context.ViewModel.Operations.IsUninstallExecuting);
    }

    [AvaloniaFact]
    public void UninstallDialog_TabNavigationCyclesWithinTheVisibleModal()
    {
        using var context = CreateContext();
        context.ViewModel.IsMotionReduced = true;
        context.Window.Show();
        context.ViewModel.Operations.Uninstall.Open(UninstallSnapshot());
        Dispatcher.UIThread.RunJobs();
        var surface = context.Window.GetVisualDescendants().OfType<DialogSurface>().Single(control => control.Name == "UninstallSurface");
        for (int index = 0; index < 16; index++)
        {
            var modifiers = index < 8 ? RawInputModifiers.None : RawInputModifiers.Shift;
            context.Window.KeyPress(Key.Tab, modifiers, PhysicalKey.Tab, "");
            context.Window.KeyRelease(Key.Tab, modifiers, PhysicalKey.Tab, "");
            Dispatcher.UIThread.RunJobs();
            var focused = Assert.IsAssignableFrom<Control>(context.Window.FocusManager!.GetFocusedElement());
            Assert.True(focused.IsEffectivelyVisible);
            Assert.True(focused == surface || focused.GetVisualAncestors().Contains(surface));
        }
        context.Window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "");
        context.Window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "");
        Dispatcher.UIThread.RunJobs();
        Assert.False(context.ViewModel.Operations.Uninstall.IsVisible);
    }

    [AvaloniaFact]
    public void UninstallDialog_WhileExecuting_BlocksEscapeAndWindowCloseButAllowsMinimize()
    {
        using var context = CreateContext();
        context.ViewModel.IsMotionReduced = true;
        context.Window.Show();
        var uninstall = context.ViewModel.Operations.Uninstall;
        uninstall.Open(new LauncherStatusSnapshot());
        uninstall.BeginExecution();
        Dispatcher.UIThread.RunJobs();
        Assert.True(context.ViewModel.TryHandleEscape());
        Assert.True(uninstall.IsVisible);
        Assert.False(context.Provider.GetRequiredService<LauncherPresentationSession>().CanShutdown);
        var closed = false;
        context.Window.Closed += (_, _) => closed = true;
        context.ViewModel.WindowChrome.CloseCommand.Execute(null);
        context.ViewModel.WindowChrome.RequestShutdown();
        context.Window.Close();
        Assert.False(closed);
        context.ViewModel.WindowChrome.MinimizeCommand.Execute(null);
        Assert.Equal(WindowState.Minimized, context.Window.WindowState);
        uninstall.Complete(new GameOperationResult { Success = true });
        Assert.True(context.Provider.GetRequiredService<LauncherPresentationSession>().CanShutdown);
        context.Window.Close();
        Assert.True(closed);
    }

    [AvaloniaTheory]
    [InlineData("en")]
    [InlineData("zh-Hans")]
    [InlineData("zh-Hant")]
    [InlineData("ja")]
    public void UninstallDialog_AtMinimumWindowSize_KeepsActionsInsideWindow(string language)
    {
        using var context = CreateContext();
        context.ViewModel.IsMotionReduced = true;
        context.ViewModel.Shell.ApplyLanguage(language, hasSnapshot: false);
        context.Window.Width = 1024;
        context.Window.Height = 640;
        context.Window.Show();
        var vm = context.ViewModel.Operations.Uninstall;
        var version = vm.Open(UninstallSnapshot());
        vm.ApplyFootprint(version, new UninstallFootprint(18_000_000_000, 2_000_000_000));
        Dispatcher.UIThread.RunJobs();
        var cancel = context.Window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "UninstallCancelButton");
        var confirm = context.Window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "UninstallConfirmButton");
        AssertControlInsideWindow(cancel, context.Window);
        AssertControlInsideWindow(confirm, context.Window);
        Assert.False(context.ViewModel.Operations.IsProgressPanelVisible);
        vm.BeginExecution();
        vm.Complete(new GameOperationResult
        {
            Success = true,
            UninstallDetails = new UninstallResultDetails(Enumerable.Range(0, 20).Select(index => @"D:\YostarGames\BlueArchive_JP\Resources\long-directory-name\" + index + ".bundle").ToArray(), @"E:\CustomPrefix")
        });
        Dispatcher.UIThread.RunJobs();
        var done = context.Window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "UninstallDoneButton");
        AssertControlInsideWindow(done, context.Window);
        Assert.True(done.IsEffectivelyVisible);
        Assert.Equal(done, context.Window.FocusManager!.GetFocusedElement());
    }

    [AvaloniaFact]
    public void UninstallDialog_WhileExecuting_GreysOutTheCloseButtonAndSaysWhy()
    {
        // 关闭钮此前只「点了没反应」：命令不可执行，但外观仍活跃、也没有任何解释。
        using var context = CreateContext();
        context.ViewModel.IsMotionReduced = true;
        context.Window.Show();
        var uninstall = context.ViewModel.Operations.Uninstall;
        uninstall.Open(UninstallSnapshot());
        Dispatcher.UIThread.RunJobs();

        var surface = context.Window.GetVisualDescendants()
            .OfType<DialogSurface>()
            .Single(control => control.Name == "UninstallSurface");
        var close = surface.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "PART_CloseButton");
        Assert.True(close.IsEnabled);
        Assert.Equal(context.ViewModel.Shell.I18n["close"], uninstall.CloseToolTip);

        uninstall.BeginExecution();
        Dispatcher.UIThread.RunJobs();

        Assert.False(close.IsEnabled);
        Assert.Equal(context.ViewModel.Shell.I18n["uninstallCloseBusy"], uninstall.CloseToolTip);
        // 执行结束后必须回到可用：置灰是阶段性的，不是一次性状态。
        uninstall.Complete(new GameOperationResult { Success = true });
        Dispatcher.UIThread.RunJobs();
        Assert.True(close.IsEnabled);
    }

    [AvaloniaFact]
    public void Golden_UninstallConfirmation_MatchesBaseline()
    {
        using var context = CreateContext();
        PrepareUninstallGolden(context);
        GoldenScreenshot.Compare(context.Window, "uninstall-confirmation");
    }

    [AvaloniaFact]
    public void Golden_UninstallProgress_MatchesBaseline()
    {
        using var context = CreateContext();
        PrepareUninstallGolden(context);
        context.ViewModel.Operations.Uninstall.BeginExecution();
        context.ViewModel.Operations.Uninstall.ApplyProgress(new GameOperationProgress { Stage = GameOperationStage.Uninstalling, Progress = 47, ProcessedEntryCount = 19000, TotalEntryCount = 38472 });
        GoldenScreenshot.Compare(context.Window, "uninstall-progress");
        context.ViewModel.Operations.Uninstall.Complete(new GameOperationResult { Success = true });
    }

    [AvaloniaFact]
    public void Golden_UninstallCompleted_MatchesBaseline()
    {
        using var context = CreateContext();
        PrepareUninstallGolden(context);
        context.ViewModel.Operations.Uninstall.BeginExecution();
        context.ViewModel.Operations.Uninstall.Complete(new GameOperationResult { Success = true, AffectedFileCount = 37804, AffectedBytes = 19_595_789_312 });
        GoldenScreenshot.Compare(context.Window, "uninstall-completed");
    }

    [AvaloniaFact]
    public void Golden_UninstallLeftoversDark_MatchesBaseline()
    {
        using var theme = ThemeVariantSnapshot.Capture(ThemeVariant.Dark);
        using var context = CreateContext();
        PrepareUninstallGolden(context, ThemeVariant.Dark);
        context.ViewModel.Operations.Uninstall.BeginExecution();
        context.ViewModel.Operations.Uninstall.Complete(new GameOperationResult
        {
            Success = true, AffectedFileCount = 37802, AffectedBytes = 19_590_000_000,
            UninstallDetails = new UninstallResultDetails([@"D:\YostarGames\BlueArchive_JP\BlueArchive_Data\StreamingAssets\Audio\Japanese\Voice\Event_Archive_2026_09\voice_000218.bundle", @"D:\YostarGames\BlueArchive_JP\Screenshots\2026-09-30.png"], @"E:\Games\Wine\BlueArchive_CustomPrefix")
        });
        GoldenScreenshot.Compare(context.Window, "uninstall-leftovers-dark");
    }

    [AvaloniaFact]
    public void Golden_UninstallFailed_MatchesBaseline()
    {
        using var context = CreateContext();
        PrepareUninstallGolden(context);
        context.ViewModel.Operations.Uninstall.BeginExecution();
        context.ViewModel.Operations.Uninstall.Complete(new GameOperationResult { Message = "Access denied: D:\\YostarGames\\BlueArchive_JP\\BlueArchive_Data" });
        GoldenScreenshot.Compare(context.Window, "uninstall-failed");
    }

    private static LauncherStatusSnapshot UninstallSnapshot() => new()
    {
        RuntimeState = LauncherRuntimeState.Ready,
        LocalGame = new LocalInstallationState { GamePath = @"D:\YostarGames\BlueArchive_JP" }
    };

    private static void PrepareUninstallGolden(TestContext context, ThemeVariant? variant = null)
    {
        PrepareGoldenWindow(context, variant);
        context.Provider.GetRequiredService<ThemeApplier>().ApplyThemeMode(
            variant == ThemeVariant.Dark ? ThemeModes.Dark : ThemeModes.Light);
        context.Window.Width = 1024;
        context.Window.Height = 640;
        context.Window.Show();
        var version = context.ViewModel.Operations.Uninstall.Open(UninstallSnapshot());
        context.ViewModel.Operations.Uninstall.ApplyFootprint(version, new UninstallFootprint(19_864_223_744, 2_330_019_758));
    }
}
