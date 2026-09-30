using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cafe.Launcher.UI.Models;

namespace Cafe.Launcher.HeadlessTests;

public sealed partial class MainWindowHeadlessTests
{
    [AvaloniaFact]
    public void UninstallProgress_WhenScanFinishes_SwitchesToDeterminateProgressAndShowsPercentage()
    {
        using var context = CreateContext();
        context.Window.Show();
        context.ViewModel.Operations.Uninstall.Open(new Cafe.Launcher.Core.Models.LauncherStatusSnapshot());
        context.ViewModel.Operations.Uninstall.BeginExecution();
        context.ViewModel.Operations.ApplyProgress(new GameOperationProgress
        {
            OperationKind = GameOperationKind.Uninstall,
            Stage = GameOperationStage.UninstallScanning,
            IsRunning = true
        });
        Dispatcher.UIThread.RunJobs();
        var panel = context.Window.GetVisualDescendants().OfType<Cafe.Launcher.UI.Controls.DialogSurface>()
            .Single(surface => surface.Name == "UninstallSurface");
        var bar = panel.GetVisualDescendants().OfType<ProgressBar>().Single();
        var percentage = panel.GetVisualDescendants().OfType<TextBlock>()
            .Single(text => text.Name == "UninstallPercentage");

        Assert.True(panel.IsEffectivelyVisible);
        Assert.True(bar.IsIndeterminate);
        Assert.False(percentage.IsVisible);

        context.ViewModel.Operations.ApplyProgress(new GameOperationProgress
        {
            OperationKind = GameOperationKind.Uninstall,
            Stage = GameOperationStage.Uninstalling,
            Progress = 47,
            ProcessedEntryCount = 50,
            TotalEntryCount = 100,
            IsRunning = true
        });
        Dispatcher.UIThread.RunJobs();

        Assert.False(bar.IsIndeterminate);
        Assert.Equal(47, bar.Value);
        Assert.True(percentage.IsVisible);
        Assert.Equal("47%", percentage.Text);
        Assert.Contains(panel.GetVisualDescendants().OfType<TextBlock>(),
            text => text.Text == context.ViewModel.Operations.Uninstall.ProgressDetail && text.IsVisible);
        context.ViewModel.Operations.Uninstall.Complete(new GameOperationResult { Success = true });
    }
}
