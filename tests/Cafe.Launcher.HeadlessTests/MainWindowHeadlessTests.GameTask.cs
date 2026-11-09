using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Testing;
using Cafe.Launcher.UI.Controls;
using Cafe.Launcher.UI.Features.GameOperations;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.ViewModels;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.UI.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Cafe.Launcher.HeadlessTests;

public sealed partial class MainWindowHeadlessTests
{
    [AvaloniaFact]
    public void Golden_InstallPreparationChineseDark_MatchesBaseline()
    {
        using var theme = ThemeVariantSnapshot.Capture(Avalonia.Styling.ThemeVariant.Dark);
        using var context = CreateContext(new StubGameOperationExecutor { InstallDownloadBytes = 3758096384 });
        PrepareGoldenWindow(context, Avalonia.Styling.ThemeVariant.Dark);
        context.Provider.GetRequiredService<ThemeApplier>().ApplyScheme(Avalonia.Media.Color.Parse("#FF2E7DF6"), isDark: true);
        context.ViewModel.Shell.ApplyLanguage("zh-Hans", hasSnapshot: false);
        context.ViewModel.Shell.IsBusy = false;
        context.ViewModel.Operations.ApplySnapshot(new LauncherStatusSnapshot { RuntimeState = LauncherRuntimeState.NotInstalled,
            LocalGame = new LocalInstallationState { GamePath = @"C:\YostarGames\BlueArchive_JP" },
            Remote = new LauncherRemoteState { GameConfig = new GameConfigResponse { DecompressionSize = "18.5 GB" } } });
        context.ViewModel.Operations.OpenInstallPreparationCommand.Execute(null);
        context.Window.Show();
        GoldenScreenshot.Compare(context.Window, "game-task-preparation-dark-zh");
    }

    [AvaloniaFact]
    public void InstallPreparation_UsesCompactPathLinkAndRightAlignedActions()
    {
        using var context = CreateContext();
        PrepareGoldenWindow(context);
        context.ViewModel.Shell.IsBusy = false;
        context.ViewModel.Operations.ApplySnapshot(new LauncherStatusSnapshot { RuntimeState = LauncherRuntimeState.NotInstalled,
            LocalGame = new LocalInstallationState { GamePath = @"D:\Games\BlueArchive_JP" } });
        context.ViewModel.Operations.OpenInstallPreparationCommand.Execute(null);
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();
        var surface = context.Window.GetVisualDescendants().OfType<DialogSurface>().Single(item => item.Name == "GameTaskSurface");
        var path = surface.GetVisualDescendants().OfType<TextBlock>().Single(item => item.Name == "InstallPathLabel");
        var change = surface.GetVisualDescendants().OfType<Button>().Single(item => item.Command == context.ViewModel.Operations.ChooseInstallPathCommand);
        Assert.Same(path.Parent, change.Parent);
        Assert.IsType<HyperlinkButton>(change);
        var field = surface.GetVisualDescendants().OfType<Border>().Single(item => item.Classes.Contains("task-install-path"));
        Assert.InRange(field.Bounds.Height, 40, 42);
        var checkbox = surface.GetVisualDescendants().OfType<CheckBox>().Single(item => item.Name == "InstallShortcutCheckBox");
        Assert.True(checkbox.IsChecked);
        var shortcutLabel = Assert.IsType<TextBlock>(checkbox.Content);
        Assert.False(string.IsNullOrWhiteSpace(shortcutLabel.Text));
        Assert.True(shortcutLabel.Bounds.Width > 0);
        checkbox.IsChecked = false;
        Assert.False(context.ViewModel.Operations.CreateInstallDesktopShortcut);
        var visibleActions = surface.GetVisualDescendants().OfType<Button>().Where(item => item.IsEffectivelyVisible).ToArray();
        Assert.Single(visibleActions, item => item.Command == context.ViewModel.Operations.CloseTaskCommand);
        var locate = Assert.Single(visibleActions, item => item.Command == context.ViewModel.Operations.IdentifyInstalledGameCommand);
        var install = Assert.Single(visibleActions, item => item.Command == context.ViewModel.Operations.ConfirmInstallCommand);
        var actions = Assert.IsType<StackPanel>(locate.Parent);
        Assert.Same(actions, install.Parent);
        Assert.Equal(Avalonia.Layout.HorizontalAlignment.Right, actions.HorizontalAlignment);
        context.ViewModel.Operations.InstallPathDraft = "";
        Dispatcher.UIThread.RunJobs();
        Assert.True(locate.IsEnabled);
        Assert.False(install.IsEnabled);
    }

    [AvaloniaFact]
    public void GameTask_FloatingCardDoesNotOwnAModalAndStopConfirmationOwnsTheTopLayer()
    {
        using var context = CreateContext();
        context.ViewModel.IsMotionReduced = true;
        context.Window.Show();
        context.ViewModel.Operations.ApplyProgress(new GameOperationProgress
        { OperationKind = GameOperationKind.Download, Stage = GameOperationStage.Downloading, Progress = 42 });
        context.ViewModel.Operations.ShowTaskCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Null(context.ViewModel.ModalHost.Top);
        Assert.False(context.ViewModel.TryHandleEscape());
        Assert.False(context.ViewModel.Operations.IsTaskDialogVisible);
        Assert.True(context.ViewModel.Operations.IsTaskExecuting);
        context.ViewModel.Operations.ShowTaskCommand.Execute(null);
        var card = context.Window.GetVisualDescendants().OfType<GameTaskCard>().Single();
        var buttons = card.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible).ToArray();
        Assert.Single(buttons, button => button.Command == context.ViewModel.Operations.ViewTaskLogCommand);
        var stop = Assert.Single(buttons, button => button.Command == context.ViewModel.Operations.RequestStopCommand);
        stop.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(ModalKind.StopConfirmation, context.ViewModel.ModalHost.Top?.Kind);
        Assert.True(context.ViewModel.TryHandleEscape());
        Assert.False(context.ViewModel.Dialogs.StopConfirm.IsVisible);
        Assert.True(context.ViewModel.Operations.IsTaskCardVisible);
        Assert.False(context.ViewModel.Operations.IsTaskDialogVisible);
        Assert.Null(context.ViewModel.ModalHost.Top);
    }

    [AvaloniaTheory]
    [InlineData("en", false, 1d)]
    [InlineData("zh-Hans", true, 1.5)]
    [InlineData("zh-Hans", false, 1.5)]
    [InlineData("zh-Hant", false, 1.5)]
    [InlineData("ja", false, 1.5)]
    public void GameTask_AtMinimumWindow_KeepsActionsAndWrappedContentInside(string language, bool failed, double scale)
    {
        using var context = CreateContext();
        PrepareGoldenWindow(context);
        context.Window.Width = 1024;
        context.Window.Height = 640;
        context.ViewModel.Shell.ApplyLanguage(language, hasSnapshot: false);
        context.ViewModel.Shell.IsBusy = false;
        context.ViewModel.Operations.ApplySnapshot(new LauncherStatusSnapshot
        { RuntimeState = LauncherRuntimeState.NotInstalled, LocalGame = new LocalInstallationState { GamePath = @"D:\Games\YostarGames\BlueArchive_JP" } });
        if (failed)
        {
            var host = (IGameOperationJourneyHost)context.ViewModel.Operations;
            host.Present(new GameOperationPresentation.Preparing());
            host.Present(new GameOperationPresentation.Busy(false));
            host.Present(new GameOperationPresentation.Completed(new GameOperationResult
            { Message = "Connection interrupted. Please check your network and try again.", ErrorCode = GameOperationErrorCode.Network }));
        }
        else context.ViewModel.Operations.OpenInstallPreparationCommand.Execute(null);
        context.Window.Show();
        using var typography = new TypographyScaleSnapshot(context.Window, scale);
        Dispatcher.UIThread.RunJobs();
        Control surface = failed
            ? context.Window.GetVisualDescendants().OfType<GameTaskCard>().Single()
            : context.Window.GetVisualDescendants().OfType<DialogSurface>().Single(item => item.Name == "GameTaskSurface");
        Assert.True(surface.IsEffectivelyVisible);
        AssertControlInsideWindow(surface, context.Window);
        var buttons = surface.GetVisualDescendants().OfType<Button>().Where(item => item.IsEffectivelyVisible).ToArray();
        Assert.NotEmpty(buttons);
        foreach (var button in buttons) AssertControlInsideWindow(button, context.Window);
        foreach (var label in surface.GetVisualDescendants().OfType<TextBlock>().Where(item => item.IsEffectivelyVisible))
            Assert.True(label.TextLayout.Width <= label.Bounds.Width + 1, label.Text);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Golden_GameTaskPreparationAndFailure_MatchesBaseline(bool failed)
    {
        using var context = CreateContext(new StubGameOperationExecutor { InstallDownloadBytes = 3758096384 });
        PrepareGoldenWindow(context);
        context.ViewModel.Shell.IsBusy = false;
        context.ViewModel.Operations.ApplySnapshot(new LauncherStatusSnapshot
        { RuntimeState = LauncherRuntimeState.NotInstalled, LocalGame = new LocalInstallationState { GamePath = @"D:\Games\BlueArchive_JP" },
            Remote = new LauncherRemoteState { GameConfig = new GameConfigResponse { DecompressionSize = "18.5 GB" } } });
        if (failed)
        {
            var host = (IGameOperationJourneyHost)context.ViewModel.Operations;
            host.Present(new GameOperationPresentation.Preparing());
            host.Present(new GameOperationPresentation.Busy(false));
            host.Present(new GameOperationPresentation.Completed(new GameOperationResult
            { Message = "Connection interrupted. Please check your network and try again.", ErrorCode = GameOperationErrorCode.Network }));
        }
        else context.ViewModel.Operations.OpenInstallPreparationCommand.Execute(null);
        context.Window.Show();
        if (failed) GoldenScreenshot.Compare(context.Window, "game-task-failed");
        else GoldenScreenshot.Compare(context.Window, "game-task-preparation");
    }
}
