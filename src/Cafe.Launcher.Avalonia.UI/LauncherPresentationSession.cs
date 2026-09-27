using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.ViewModels;
using Cafe.Launcher.Avalonia.Views;
using Cafe.Launcher.Core.Services.Diagnostics;

namespace Cafe.Launcher.Avalonia.UI;

/// <summary>
/// 表现层的生命周期门面，也是宿主进入表现层的唯一入口。窗口、窗口 ViewModel、托盘与
/// 首启/初始化流程都由本类自己从容器解析并组装；宿主只处理它才知道的事——应用生命周期、
/// 进程间转发信号、崩溃窗口替换与关闭延迟。
/// </summary>
/// <remarks>
/// 逆序依赖：本类由 <c>AddLauncherPresentation</c> 登记，容器因此在本类之后释放它依赖的
/// 表现层服务；<see cref="Dispose"/> 只负责它自己构造的窗口级协作者。
/// </remarks>
public sealed class LauncherPresentationSession : IDisposable
{
    private readonly IServiceProvider services;
    private MainWindow? mainWindow;
    private MainWindowViewModel? viewModel;
    private SystemTrayService? trayService;
    private bool startupAttached;
    private bool disposed;

    public LauncherPresentationSession(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        this.services = services;
    }

    /// <summary>创建并配置本会话唯一的桌面窗口（含托盘），重复调用返回同一实例。</summary>
    public Window CreateMainWindow()
    {
        ThrowIfDisposed();
        if (mainWindow is not null)
        {
            return mainWindow;
        }

        var resolvedViewModel = services.GetRequiredService<MainWindowViewModel>();
        var window = new MainWindow(
            services.GetRequiredService<WindowFilePickerService>(),
            services.GetRequiredService<WindowMetricsService>(),
            services.GetRequiredService<LocalDiagnostics>())
        {
            DataContext = resolvedViewModel,
        };

        viewModel = resolvedViewModel;
        mainWindow = window;
        window.ConfigureViewModel(resolvedViewModel);
        InitializeTray(window);
        return window;
    }

    /// <summary>
    /// 挂上窗口打开后的启动行为：首启走向导（先应用动效偏好），否则跑完整初始化，
    /// 并在命令行要求时用与转发请求相同的入口自动启动游戏。
    /// </summary>
    /// <param name="firstLaunch">本次是否为首次启动。</param>
    /// <param name="launchGameRequested">本进程是否带 <c>--launch-game</c> 启动。</param>
    /// <param name="shutdownToken">宿主关闭时取消初始化/启动流程的令牌。</param>
    public void AttachStartupBehavior(bool firstLaunch, bool launchGameRequested, CancellationToken shutdownToken)
    {
        ThrowIfDisposed();
        var window = mainWindow ?? throw new InvalidOperationException("Presentation window has not been created.");
        var resolvedViewModel = viewModel ?? throw new InvalidOperationException("Presentation view model has not been created.");
        if (startupAttached)
        {
            throw new InvalidOperationException("Startup behavior has already been attached.");
        }

        startupAttached = true;

        // 首启不做完整初始化（快照由向导驱动后再加载），但动效偏好必须先行应用，
        // 否则 IsMotionReduced 停留在字段默认 true，首启向导全程瞬切。
        if (firstLaunch)
        {
            WindowOpenedOnce.Subscribe(window, (_, _) =>
            {
                // 放到布局/渲染/绑定都完成的优先级之后再切换可见性。
                Dispatcher.UIThread.Post(() =>
                {
                    resolvedViewModel.ApplyFirstLaunchMotionPreference();
                    resolvedViewModel.Dialogs.ShowSetupWizard();
                }, DispatcherPriority.Background);
            });
            return;
        }

        WindowOpenedOnce.Subscribe(window, (_, _) =>
        {
            _ = InitializeThenMaybeLaunchAsync(launchGameRequested, shutdownToken);
        });
    }

    /// <summary>窗口打开后的完整初始化（读已保存设置 → 恢复窗口几何 → 初始化 VM）。</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var window = mainWindow ?? throw new InvalidOperationException("Presentation window has not been created.");
        var resolvedViewModel = viewModel ?? throw new InvalidOperationException("Presentation view model has not been created.");

        try
        {
            var settingsService = services.GetRequiredService<ILauncherSettingsService>();
            var savedSettings = await settingsService.ReadAsync(cancellationToken);
            window.ApplySavedWindowState(savedSettings);
            await resolvedViewModel.InitializeAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            // 初始化失败本身由下方 HandleErrorAsync 记录，不在此重复打点。
            try
            {
                // Initialization itself failed, so localization may be unavailable; keep an
                // English fallback so the toast never shows the raw "Localization unavailable." text.
                var toastMessage = "Launcher initialization failed.";
                try
                {
                    toastMessage = services
                        .GetRequiredService<LocalizationService>()
                        .F(LocalizationKeys.LauncherInitFailed, exception.Message);
                }
                catch (Exception localizationException)
                {
                    // 豁免：本地化失败的兜底路径——此时诊断/本地化本身不可用，
                    // Debug 输出是最后一级无依赖通道。
                    Debug.WriteLine($"Failure-toast localization unavailable: {localizationException.Message}");
                }

                await services
                    .GetRequiredService<IErrorHandlingService>()
                    .HandleErrorAsync("Launcher initialization failed.", exception,
                        new ErrorHandlingOptions { ToastMessage = toastMessage });
            }
            catch (Exception diagnosticsException)
            {
                // 豁免：诊断管道自身失败的兜底路径，不得再回调诊断。
                Debug.WriteLine($"Initialization diagnostics failed: {diagnosticsException.Message}");
            }
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                window.ApplySavedWindowState(resolvedViewModel.Settings.Editor.GetSavedSnapshot());
            }
        }
    }

    /// <summary>把已有窗口带到前台（托盘存在时走托盘，保证恢复路径与托盘一致）。</summary>
    public void ShowWindow()
    {
        ThrowIfDisposed();
        if (trayService is not null)
        {
            trayService.ShowWindow();
        }
        else
        {
            mainWindow?.ShowWindow();
        }
    }

    /// <summary>把转发来的启动游戏请求送进常规 UI 旅程。</summary>
    public Task LaunchGameAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        _ = cancellationToken;
        var resolvedViewModel = viewModel ?? throw new InvalidOperationException("Presentation view model has not been created.");
        resolvedViewModel.Operations.StartGameCommand.Execute(null);
        return Task.CompletedTask;
    }

    /// <summary>宿主释放容器前的表现层收尾：结束会话并（按设置）记住窗口几何。</summary>
    public async Task PrepareForShutdownAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var window = mainWindow ?? throw new InvalidOperationException("Presentation window has not been created.");
        var resolvedViewModel = viewModel ?? throw new InvalidOperationException("Presentation view model has not been created.");

        try
        {
            await resolvedViewModel.PrepareForShutdownAsync();

            if (resolvedViewModel.Settings.Editor.GetSavedSnapshot().RememberWindowPositionAndSize)
            {
                // 关窗写入显式不传取消令牌：宿主在调用本方法前已经 Cancel 了关闭令牌，
                // 转发它会让「记住窗口位置」在每次正常退出时静默失效。
                await services.GetRequiredService<ISavedSettingsWriter>()
                    .UpdateAsync(window.CaptureWindowState, CancellationToken.None);
            }
        }
        catch (Exception exception)
        {
            // 关窗持久化失败发生在每次正常退出路径上：必须写入本地日志，
            // 否则用户报告「窗口位置记不住」时无任何诊断线索（Debug 输出在
            // Release 构建不可见）。此刻容器尚未 Dispose，
            // LocalDiagnostics.ErrorAsync 自身全量吞异常，不会反向影响退出流程。
            await services.GetRequiredService<LocalDiagnostics>().ErrorAsync(
                "Shutdown persistence failed.",
                exception,
                CancellationToken.None);
        }
    }

    /// <summary>宿主替换主窗口为崩溃报告窗口前，先把主窗口藏起来。</summary>
    public void HideMainWindow()
    {
        ThrowIfDisposed();
        mainWindow?.Hide();
    }

    /// <summary>构造独立崩溃报告窗口：窗口内容归表现层，替换 <c>desktop.MainWindow</c> 与退出码归宿主。</summary>
    public Window CreateCrashReportWindow(CrashReport report)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(report);
        return new CrashReportWindow(report, services.GetRequiredService<LauncherDataRoot>());
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        trayService?.Dispose();
        trayService = null;
        viewModel?.Dispose();
        viewModel = null;
        mainWindow = null;
    }

    private async Task InitializeThenMaybeLaunchAsync(bool launchGameRequested, CancellationToken shutdownToken)
    {
        await InitializeAsync(shutdownToken);
        if (launchGameRequested)
        {
            // 初始刷新已结束，自动启动与转发的 --launch-game 请求走同一入口。
            await Dispatcher.UIThread.InvokeAsync(() => LaunchGameAsync(shutdownToken));
        }
    }

    private void InitializeTray(MainWindow window)
    {
        // 托盘依赖窗口，且不属于 DI（窗口本身不是容器服务）。
        try
        {
            var tray = new SystemTrayService(
                window,
                services.GetRequiredService<LocalizationService>(),
                services.GetRequiredService<LocalDiagnostics>(),
                services.GetRequiredService<ISystemTrayActions>());
            if (tray.Initialize())
            {
                window.SetSystemTray(tray);
                trayService = tray;
            }
            else
            {
                tray.Dispose();
            }
        }
        catch (Exception ex)
        {
            LocalDiagnostics.LogSync(LogEntrySeverity.Warn, "App", $"SystemTrayService init failed: {ex.Message}");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }
}
