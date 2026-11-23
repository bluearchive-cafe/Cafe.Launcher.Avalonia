using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.Views;

namespace Cafe.Launcher.HeadlessTests;

public sealed partial class MainWindowHeadlessTests
{
    /// <summary>
    /// 半透明表面 token 直接由 Border 承载（玻璃采样层已移除）：侧栏、资讯整组与任务卡
    /// 在运行期解析到同一族 Glass.* 资源。
    /// </summary>
    [AvaloniaFact]
    public void ShellSurfaces_UseTranslucentTokensDirectly()
    {
        using var context = CreateContext();
        context.ViewModel.Operations.ApplySnapshot(
            new LauncherStatusSnapshot { RuntimeState = LauncherRuntimeState.UpdateAvailable });
        context.ViewModel.Operations.ApplyProgress(
            new GameOperationProgress { OperationKind = GameOperationKind.Download });
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        Border Surface(string name) => context.Window.FindControl<Border>(name)!;
        var variant = context.Window.ActualThemeVariant;

        Assert.Same(
            context.Window.FindResource(variant, "Launcher.Color.Glass.Rail.Background"),
            Surface("GameSidebar").Background);
        Assert.Same(
            context.Window.FindResource(variant, "Launcher.Color.Glass.Edge"),
            Surface("GameSidebar").BorderBrush);

        var news = Surface("HomeNews");
        Assert.Same(
            context.Window.FindResource(variant, "Launcher.Color.Glass.Strong.Background"),
            news.Background);
        Assert.Same(
            context.Window.FindResource(variant, "Launcher.Color.Glass.Edge"),
            news.BorderBrush);

        var card = context.Window.GetVisualDescendants().OfType<GameTaskCard>().Single()
            .FindControl<Border>("FloatingTaskCard")!;
        Assert.Same(
            context.Window.FindResource(variant, "Launcher.Color.Glass.Strong.Background"),
            card.Background);
        Assert.Same(
            context.Window.FindResource(variant, "Launcher.Color.Glass.Edge"),
            card.BorderBrush);
    }
}
