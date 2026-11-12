using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Cafe.Launcher.UI;
using Cafe.Launcher.Composition;
using Cafe.Launcher.Constants;
using Cafe.Launcher.Services;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.Core.Services.Diagnostics;
using Cafe.Launcher.Services.Diagnostics;
using Cafe.Launcher.UI.Services.Diagnostics;

namespace Cafe.Launcher;

public partial class App : Application
{
    private readonly CancellationTokenSource shutdownCts = new();
    private ServiceProvider? serviceProvider;
    private ShowWindowSignalListener? showWindowListener;
    private LaunchGameSignalListener? launchGameListener;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
#if DEBUG
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime)
        {
            this.AttachDeveloperTools();
        }
#endif
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Build DI container, reusing the pre-DI UnifiedLogger so there is
            // a single Serilog pipeline for the entire process.
            var serviceCollection = new ServiceCollection();
            var shutdownDeferral = new ShutdownDeferral();
            // 组合根解析唯一的数据根并先注册 Core；表现层随后注册，容器反向释放时先释放 UI。
            // 宿主不再自己解析数据根，也不再单独调用 AddLauncherCore（那会造成两处注册顺序契约）。
            serviceCollection.AddLauncherServices(
                existingLogger: Program.PreDiLogger,
                existingFatalCrashService: Program.PreDiFatalCrashService);
            serviceProvider = serviceCollection.BuildServiceProvider();
            Program.ServiceProvider = serviceProvider;

            // Capture OS culture before any SetLanguage call so "auto"
            // can restore the genuine startup culture later.
            _ = serviceProvider.GetRequiredService<LocalizationService>();

            // Application-started trace (best-effort, fire-and-forget)
            _ = serviceProvider.GetRequiredService<Cafe.Launcher.Core.Services.Diagnostics.ILauncherDiagnostics>()
                .DebugAsync("Application", "Application started, DI container built", CancellationToken.None);

            // 表现层自己构造窗口、ViewModel 与托盘：宿主只拿到一个 Window 交回应用生命周期。
            var presentationSession = serviceProvider.GetRequiredService<LauncherPresentationSession>();
            var mainWindow = presentationSession.CreateMainWindow();
            var fatalShutdown = false;
            Window? crashReportWindow = null;

            void HandleFatalCrashRequested(CrashReport report)
            {
                void ShowCrashWindow()
                {
                    if (crashReportWindow is not null)
                    {
                        return;
                    }

                    fatalShutdown = true;
                    Program.FatalCrashExitRequested = true;
                    shutdownCts.Cancel();
                    showWindowListener?.Dispose();
                    launchGameListener?.Dispose();
                    presentationSession.HideMainWindow();

                    crashReportWindow = presentationSession.CreateCrashReportWindow(report);
                    crashReportWindow.Closed += (_, _) => desktop.Shutdown(1);
                    desktop.MainWindow = crashReportWindow;
                    crashReportWindow.Show();
                    crashReportWindow.Activate();
                }

                if (Dispatcher.UIThread.CheckAccess())
                {
                    ShowCrashWindow();
                }
                else
                {
                    Dispatcher.UIThread.Post(ShowCrashWindow, DispatcherPriority.Send);
                }
            }

            var fatalCrashService = serviceProvider.GetRequiredService<IFatalCrashService>();
            fatalCrashService.FatalCrashRequested += HandleFatalCrashRequested;

            async void HandleShutdownRequested(object? _, ShutdownRequestedEventArgs eventArgs)
            {
                if (fatalShutdown)
                {
                    return;
                }

                if (!presentationSession.CanShutdown)
                {
                    eventArgs.Cancel = true;
                    return;
                }

                if (shutdownDeferral.ShouldCancelRequest)
                {
                    eventArgs.Cancel = true;
                    return;
                }

                // 关闭请求阶段先停听：此后不再派发转发信号，退出清理也就不需要等轮询循环
                // （审计 AUD-PERF-010）；资源释放在 desktop.Exit 里做且不在 UI 线程上等待。
                showWindowListener?.StopListening();
                launchGameListener?.StopListening();
                shutdownCts.Cancel();
                Task shutdownTask = presentationSession.PrepareForShutdownAsync();
                if (shutdownTask.IsCompletedSuccessfully)
                {
                    return;
                }

                eventArgs.Cancel = true;
                shutdownDeferral.Defer();
                try
                {
                    await shutdownTask;
                }
                catch (Exception exception)
                {
                    LauncherLog.LogSync(LogEntrySeverity.Error, "App", $"Launcher shutdown coordination failed: {exception}");
                }
                finally
                {
                    shutdownDeferral.Commit();
                    desktop.Shutdown();
                }
            }

            desktop.ShutdownRequested += HandleShutdownRequested;

            // 清理：容器由 Program.RunSession 释放。Avalonia 对同一次关闭可能触发多次 Exit，
            // 因此清理只跑一次（致命崩溃路径会走强制的 Shutdown(1)，随后生命周期再补一次）。
            var exited = false;
            desktop.Exit += (_, _) =>
            {
                if (exited)
                {
                    return;
                }

                exited = true;
                desktop.ShutdownRequested -= HandleShutdownRequested;
                fatalCrashService.FatalCrashRequested -= HandleFatalCrashRequested;
                // 监听器释放只取消并把收尾排到线程池，不在 UI 线程上等待轮询/接收循环
                // （审计 AUD-PERF-010）；正常退出路径已在关闭请求阶段先行停听。
                showWindowListener?.Dispose();
                launchGameListener?.Dispose();
                shutdownCts.Cancel();

                // 启动续体结束后才释放令牌源：它仍可能在 ReadAsync(shutdownToken) 等调用里
                // 注册令牌，此时释放会把正常收尾变成未观察的 ObjectDisposedException
                // （审计 AUD-ARCH-019）。未结束时宁可留下一个令牌源（进程正在退出）。
                var startupFinished = presentationSession.IsStartupFinished;
                presentationSession.Dispose();
                if (startupFinished)
                {
                    shutdownCts.Dispose();
                }
            };

            // Listen for show-window signal from second instances (cross-platform:
            // a plain second start or a forwarded launch brings this window up).
            showWindowListener = new ShowWindowSignalListener(presentationSession, Program.ShowWindowSignal!);

            // Listen for --launch-game forwards from second instances (cross-platform:
            // the Linux .desktop shortcut relies on it; on Unix the transport is a
            // local socket, since .NET has no named events outside Windows).
            launchGameListener = new LaunchGameSignalListener(presentationSession, Program.LaunchGameSignal!);

            // 启动行为由表现层挂载：首启走向导，否则完整初始化（两者都只跑一次）。
            presentationSession.AttachStartupBehavior(
                Program.FirstLaunch,
                Program.LaunchGameRequested,
                shutdownCts.Token);

            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Base for cross-process signal listeners: owns the polling loop
    /// (<see cref="CrossProcessPollingListener"/>) and marshals every consumed
    /// signal onto the UI thread, dropping signals that arrive after disposal
    /// so a late dispatch never acts on a stopped listener.
    /// </summary>
    private abstract class CrossProcessSignalListener : IDisposable
    {
        private readonly CrossProcessPollingListener pollingListener;
        private readonly Action onSignalRaised;

        protected CrossProcessSignalListener(Func<TimeSpan, bool> waitForSignal, Action onSignalRaised)
        {
            this.onSignalRaised = onSignalRaised;
            pollingListener = new CrossProcessPollingListener(waitForSignal, OnSignalConsumed);
        }

        /// <summary>
        /// 关闭请求阶段的先行停听：不再接受新的转发信号，但不做任何等待，
        /// 因此可以安全地从 UI 线程调用（审计 AUD-PERF-010）。
        /// </summary>
        public void StopListening() => pollingListener.StopListening();

        public virtual void Dispose() => pollingListener.Dispose();

        private void OnSignalConsumed()
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (pollingListener.IsCancellationRequested)
                {
                    return;
                }

                onSignalRaised();
            });
        }
    }

    /// <summary>
    /// Receives the show-window signal from second instances so a forwarded launch
    /// (or a plain second start) brings the running launcher up: restored from the
    /// system tray when present, otherwise shown via the main window. On Windows
    /// the transport is a named EventWaitHandle; on Unix it is a local socket
    /// (CrossProcessLaunchSignal), because .NET has no named events there.
    /// The polling loop and UI marshaling are delegated to
    /// <see cref="CrossProcessSignalListener"/>.
    /// </summary>
    private sealed class ShowWindowSignalListener : CrossProcessSignalListener
    {
        public ShowWindowSignalListener(LauncherPresentationSession presentationSession, CrossProcessLaunchSignal signal)
            : base(signal.WaitOne, () =>
        {
            try
            {
                presentationSession.ShowWindow();
            }
            catch (Exception ex)
            {
                // Restore is best-effort.
                LauncherLog.LogSync(LogEntrySeverity.Warn, "App", $"Window restore dispatch failed: {ex.Message}");
            }
        })
        {
        }
    }

    /// <summary>
    /// Receives the --launch-game forward from a second process and starts the game
    /// through the regular UI command, so busy-state, validation, and toasts all apply.
    /// On Windows the transport is a named EventWaitHandle; on Unix it is a local
    /// socket (CrossProcessLaunchSignal), because .NET has no named events there.
    /// The polling loop and UI marshaling are delegated to
    /// <see cref="CrossProcessSignalListener"/>.
    /// </summary>
    private sealed class LaunchGameSignalListener : CrossProcessSignalListener
    {
        public LaunchGameSignalListener(LauncherPresentationSession presentationSession, CrossProcessLaunchSignal signal)
            : base(signal.WaitOne, () =>
            {
                try
                {
                _ = presentationSession.LaunchGameAsync();
                }
                catch (Exception ex)
                {
                    // The launch journey reports its own failures; this only guards dispatch.
                    LauncherLog.LogSync(LogEntrySeverity.Warn, "App", $"Launch-game signal dispatch failed: {ex.Message}");
                }
            })
        {
        }
    }
}
