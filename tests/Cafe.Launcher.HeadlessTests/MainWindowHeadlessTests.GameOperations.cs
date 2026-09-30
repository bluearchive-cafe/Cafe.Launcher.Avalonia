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
        context.ViewModel.Operations.ApplyProgress(new GameOperationProgress
        {
            OperationKind = GameOperationKind.Uninstall,
            Stage = GameOperationStage.UninstallScanning,
            IsRunning = true
        });
        Dispatcher.UIThread.RunJobs();
        var panel = context.Window.FindControl<Border>("OperationProgressState")!;
        var bar = panel.GetVisualDescendants().OfType<ProgressBar>().Single();
        var percentage = panel.GetVisualDescendants().OfType<TextBlock>()
            .Single(text => text.Text == "0%" && text.Classes.Contains("progress-title"));

        Assert.True(panel.IsVisible);
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
            text => text.Text == context.ViewModel.Operations.ProgressDetail && text.IsVisible);
    }
}
