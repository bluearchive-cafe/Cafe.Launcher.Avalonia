using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cafe.Launcher.Avalonia.Features.GameOperations;
using Cafe.Launcher.Avalonia.Features.Settings;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.ViewModels;
using Cafe.Launcher.Avalonia.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Cafe.Launcher.Avalonia.HeadlessTests;

// MainWindowHeadlessTests 的共享核心（上下文构造与跨域 helper）。
// 各职责域的分卷见 MainWindowHeadlessTests.<域>.cs：Threading / Settings /
// SetupWizard / Banner / Toast / Motion / RemoteContent / Dialogs / WindowState / Golden。
public sealed partial class MainWindowHeadlessTests
{
    private static readonly (string Code, Type SectionType)[] SettingsSections =
    [
        (SettingsCategoryCodes.General, typeof(SettingsGeneralSection)),
        (SettingsCategoryCodes.Game, typeof(SettingsGameSection)),
        (SettingsCategoryCodes.DownloadNetwork, typeof(SettingsDownloadNetworkSection)),
        (SettingsCategoryCodes.Appearance, typeof(SettingsAppearanceSection)),
        (SettingsCategoryCodes.Advanced, typeof(SettingsAdvancedSection)),
        (SettingsCategoryCodes.About, typeof(SettingsAboutSection))
    ];

    private static TestContext CreateContext(IGameOperationExecutor? executor = null)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        // 与 HeadlessTestHost.CreateContext 共用 DI 构造；executor 在日志注册前追加。
        var provider = HeadlessTestHost.CreateServiceProvider(tempDir, services =>
        {
            if (executor is not null)
            {
                services.AddSingleton(executor);
            }
        });
        var viewModel = provider.GetRequiredService<MainWindowViewModel>();
        viewModel.Shell.ApplyLanguage(
            LauncherLanguages.English,
            viewModel.Settings,
            viewModel.ResourcePanel,
            hasSnapshot: false);
        viewModel.Settings.Editor.ApplySnapshot(
            viewModel.Settings.Editor.GetSnapshot());
        // Apply the default M3 dynamic scheme so navigation selection visual
        // matches the real app's initialization behavior.
        SettingsAppearanceViewModel.ApplyScheme(
            Color.Parse("#FF2E7DF6"));
        var window = new MainWindow { DataContext = viewModel };
        window.ConfigureViewModel(viewModel);
        return new TestContext(tempDir, provider, window, viewModel);
    }

    private static void OpenSettings(TestContext context)
    {
        context.Window.Show();
        context.ViewModel.WindowChrome.ShowSettingsCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
    }

    private static void AssertControlInsideWindow(Control control, Window window)
    {
        var topLeft = control.TranslatePoint(default, window);
        Assert.NotNull(topLeft);
        Assert.True(control.Bounds.Width > 0);
        Assert.True(control.Bounds.Height > 0);
        Assert.True(topLeft.Value.X >= 0);
        Assert.True(topLeft.Value.Y >= 0);
        Assert.True(topLeft.Value.X + control.Bounds.Width <= window.ClientSize.Width);
        Assert.True(topLeft.Value.Y + control.Bounds.Height <= window.ClientSize.Height);
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
            if (!Directory.Exists(TempDir))
            {
                return;
            }

            try
            {
                // 与 HeadlessTestHost 的上下文清理一致：句柄延迟释放导致的删除失败
                // 只残留临时目录，不让清理问题掩盖测试结果。
                Directory.Delete(TempDir, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
