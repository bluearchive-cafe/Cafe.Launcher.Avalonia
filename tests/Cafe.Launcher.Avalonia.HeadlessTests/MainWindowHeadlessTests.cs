using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.ViewModels;
using Cafe.Launcher.Avalonia.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Cafe.Launcher.Avalonia.HeadlessTests;

public sealed class MainWindowHeadlessTests
{
    [AvaloniaFact]
    public void MainWindow_WhenShown_LoadsRealXamlAndOverlayBindings()
    {
        using var context = CreateContext();
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var settingsOverlay = context.Window
            .GetVisualDescendants()
            .OfType<Grid>()
            .Single(control => control.Classes.Contains("settings-overlay"));
        Assert.False(settingsOverlay.IsVisible);

        context.ViewModel.WindowChrome.IsSettingsVisible = true;
        Dispatcher.UIThread.RunJobs();

        Assert.True(settingsOverlay.IsVisible);
        Assert.Single(context.Window.GetVisualDescendants().OfType<MainWindowSettingsOverlay>());
        Assert.Single(context.Window.GetVisualDescendants().OfType<MainWindowDialogsOverlay>());
        Assert.Single(context.Window.GetVisualDescendants().OfType<MainWindowToastOverlay>());
    }

    [AvaloniaFact]
    public void MainWindow_SettingsButtonHasBoundCommandAndAutomationName()
    {
        using var context = CreateContext();
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();
        var button = context.Window
            .GetVisualDescendants()
            .OfType<Button>()
            .Single(control => control.Classes.Contains("settings"));

        Assert.Same(context.ViewModel.WindowChrome.ShowSettingsCommand, button.Command);
        Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)));

        button.Command!.Execute(button.CommandParameter);
        Dispatcher.UIThread.RunJobs();

        Assert.True(context.ViewModel.WindowChrome.IsSettingsVisible);
    }

    [AvaloniaFact]
    public void SettingsFooter_BindsTransactionalSaveAndCancelCommands()
    {
        using var context = CreateContext();
        context.Window.Show();
        context.ViewModel.WindowChrome.IsSettingsVisible = true;
        context.ViewModel.Settings.Editor.Current.Language = LauncherLanguages.Japanese;
        Dispatcher.UIThread.RunJobs();
        var footerButtons = context.Window
            .GetVisualDescendants()
            .OfType<Button>()
            .Where(control => control.Classes.Contains("settings-footer-action"))
            .ToArray();
        Assert.Equal(2, footerButtons.Length);
        var save = footerButtons.Single(button =>
            ReferenceEquals(button.Command, context.ViewModel.Settings.SaveSettingsCommand));
        var cancel = footerButtons.Single(button =>
            ReferenceEquals(button.Command, context.ViewModel.WindowChrome.ShowSettingsCommand));

        Assert.True(save.IsEnabled);
        cancel.Command!.Execute(cancel.CommandParameter);
        Dispatcher.UIThread.RunJobs();

        Assert.True(context.ViewModel.WindowChrome.IsSettingsVisible);
        Assert.True(context.ViewModel.Settings.IsUnsavedChangesVisible);
    }

    [AvaloniaFact]
    public void DialogOverlay_WhenRepairIsRequested_BecomesVisible()
    {
        using var context = CreateContext();
        context.Window.Show();
        context.ViewModel.Dialogs.ShowRepairConfirm("repair confirmation");
        Dispatcher.UIThread.RunJobs();

        Assert.Contains(
            context.Window.GetVisualDescendants().OfType<Grid>(),
            grid => grid.Classes.Contains("dialog-overlay")
                && grid.IsEffectivelyVisible);
        Assert.Contains(
            context.Window.GetVisualDescendants().OfType<TextBlock>(),
            text => text.Text == "repair confirmation");
    }

    [AvaloniaFact]
    public void MainWindow_WhenEscapeRouteIsInvoked_ClosesSettingsOverlay()
    {
        using var context = CreateContext();
        context.Window.Show();
        context.ViewModel.Dialogs.IsDownloadRunningCloseConfirmVisible = false;
        context.ViewModel.Dialogs.IsStopConfirmVisible = false;
        context.ViewModel.Settings.IsUnsavedChangesVisible = false;
        context.ViewModel.Dialogs.IsRepairConfirmVisible = false;
        context.ViewModel.Dialogs.IsResourcePanelSourceConfirmVisible = false;
        context.ViewModel.Dialogs.IsUninstallConfirmVisible = false;
        context.ViewModel.Dialogs.IsNoticeDialogVisible = false;
        context.ViewModel.ResourcePanel.IsResourcePanelVisible = false;
        context.ViewModel.Settings.Editor.ApplySnapshot(
            context.ViewModel.Settings.Editor.GetSnapshot());
        context.ViewModel.WindowChrome.IsSettingsVisible = true;
        Dispatcher.UIThread.RunJobs();

        var handled = context.ViewModel.TryHandleEscape();
        Dispatcher.UIThread.RunJobs();

        Assert.True(handled);
        Assert.False(context.ViewModel.WindowChrome.IsSettingsVisible);
    }

    [AvaloniaFact]
    public void MainWindow_CloseCommand_WhenConfiguredToMinimize_UsesWindowFallback()
    {
        using var context = CreateContext();
        context.ViewModel.Settings.Editor.ApplySnapshot(new LauncherSettings
        {
            CloseBehavior = CloseBehaviors.Minimize
        });
        context.Window.Show();

        context.ViewModel.WindowChrome.CloseCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(WindowState.Minimized, context.Window.WindowState);
    }

    [AvaloniaFact]
    public void ConfigureViewModel_WhenCalledAgain_UnsubscribesPreviousViewModel()
    {
        using var first = CreateContext();
        using var second = CreateContext();
        first.Window.ConfigureViewModel(second.ViewModel);
        first.Window.WindowState = WindowState.Normal;

        first.ViewModel.WindowChrome.MinimizeCommand.Execute(null);
        Assert.Equal(WindowState.Normal, first.Window.WindowState);

        second.ViewModel.WindowChrome.MinimizeCommand.Execute(null);
        Assert.Equal(WindowState.Minimized, first.Window.WindowState);
    }

    private static TestContext CreateContext()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var services = new ServiceCollection();
        services.AddLauncherServices();
        services.AddSingleton(_ => new UnifiedLogger(Path.Combine(tempDir, "logs")));
        var provider = services.BuildServiceProvider();
        var viewModel = provider.GetRequiredService<MainWindowViewModel>();
        viewModel.Shell.ApplyLanguage(
            LauncherLanguages.English,
            viewModel.Settings,
            viewModel.ResourcePanel,
            hasSnapshot: false);
        viewModel.Settings.Editor.ApplySnapshot(
            viewModel.Settings.Editor.GetSnapshot());
        var window = new MainWindow { DataContext = viewModel };
        window.ConfigureViewModel(viewModel);
        return new TestContext(tempDir, provider, window, viewModel);
    }

    private sealed record TestContext(
        string TempDir,
        ServiceProvider Provider,
        MainWindow Window,
        MainWindowViewModel ViewModel) : IDisposable
    {
        public void Dispose()
        {
            Window.Close();
            Provider.Dispose();
            if (Directory.Exists(TempDir))
            {
                Directory.Delete(TempDir, recursive: true);
            }
        }
    }
}
