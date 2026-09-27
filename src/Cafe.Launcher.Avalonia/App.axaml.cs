using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Cafe.Launcher.Core.Composition;
using Cafe.Launcher.Avalonia.UI;
using Cafe.Launcher.Avalonia.UI.Composition;
using Cafe.Launcher.Avalonia.Composition;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Features.GameOperations;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.ViewModels;
using Cafe.Launcher.Avalonia.Views;

namespace Cafe.Launcher.Avalonia;

public partial class App : Application
{
    private const string SignalName = @"Local\Cafe_Launcher_SI_Show";
    private readonly CancellationTokenSource shutdownCts = new();
    private ServiceProvider? serviceProvider;
    private SystemTrayService? trayService;
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
            var launcherDataRoot = LauncherDataRoot.ForCurrentProcess();
            MainWindow? presentationWindow = null;
            MainWindowViewModel? presentationViewModel = null;
            serviceCollection.AddLauncherCore(BuildInfo.Identity, launcherDataRoot);
            serviceCollection.AddLauncherPresentation(
                new LauncherPresentationCallbacks(
                    CreateMainWindow: () => presentationWindow
                        ?? throw new InvalidOperationException("Presentation window has not been created."),
                    InitializeAsync: cancellationToken => InitializeViewModelAsync(
                        presentationWindow
                            ?? throw new InvalidOperationException("Presentation window has not been created."),
                        presentationViewModel
                            ?? throw new InvalidOperationException("Presentation view model has not been created."),
                        serviceProvider
                            ?? throw new InvalidOperationException("Presentation services have not been built."),
                        cancellationToken),
                    ShowWindow: () =>
                    {
                        if (trayService is not null)
                        {
                            trayService.ShowWindow();
                        }
                        else
                        {
                            presentationWindow?.ShowWindow();
                        }
                    },
                    LaunchGameAsync: _ =>
                    {
                        (presentationViewModel
                            ?? throw new InvalidOperationException("Presentation view model has not been created."))
                            .Operations.StartGameCommand.Execute(null);
                        return Task.CompletedTask;
                    },
                    PrepareForShutdownAsync: _ => CompleteShutdownAsync(
                        presentationWindow
                            ?? throw new InvalidOperationException("Presentation window has not been created."),
                        presentationViewModel
                            ?? throw new InvalidOperationException("Presentation view model has not been created."),
                        serviceProvider
                            ?? throw new InvalidOperationException("Presentation services have not been built.")),
                    Dispose: () => presentationViewModel?.Dispose()));
            serviceCollection.AddLauncherServices(
                existingLogger: Program.PreDiLogger,
                existingFatalCrashService: Program.PreDiFatalCrashService,
                launcherDataRoot: launcherDataRoot);
            serviceProvider = serviceCollection.BuildServiceProvider();
            Program.ServiceProvider = serviceProvider;

            // Capture OS culture before any SetLanguage call so "auto"
            // can restore the genuine startup culture later.
            _ = serviceProvider.GetRequiredService<LocalizationService>();

            // Application-started trace (best-effort, fire-and-forget)
            _ = serviceProvider.GetRequiredService<Services.Diagnostics.LocalDiagnostics>()
                .DebugAsync("Application", "Application started, DI container built", CancellationToken.None);

            var viewModel = serviceProvider.GetRequiredService<MainWindowViewModel>();
            presentationViewModel = viewModel;
            var mainWindow = new MainWindow(
                serviceProvider.GetRequiredService<WindowFilePickerService>(),
                serviceProvider.GetRequiredService<WindowMetricsService>(),
                serviceProvider.GetRequiredService<Services.Diagnostics.LocalDiagnostics>())
            {
                DataContext = viewModel,
            };
            presentationWindow = mainWindow;
            var shutdownDeferral = new ShutdownDeferral();
            var presentationSession = serviceProvider.GetRequiredService<LauncherPresentationSession>();
            var fatalShutdown = false;
            CrashReportWindow? crashReportWindow = null;

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
                    trayService?.Dispose();
                    mainWindow.Hide();

                    crashReportWindow = new CrashReportWindow(
                        report,
                        serviceProvider.GetRequiredService<LauncherDataRoot>());
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

                if (shutdownDeferral.ShouldCancelRequest)
                {
                    eventArgs.Cancel = true;
                    return;
                }

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
                    LocalDiagnostics.LogSync(LogEntrySeverity.Error, "App", $"Launcher shutdown coordination failed: {exception}");
                }
                finally
                {
                    shutdownDeferral.Commit();
                    desktop.Shutdown();
                }
            }

            desktop.ShutdownRequested += HandleShutdownRequested;
            mainWindow.ConfigureViewModel(viewModel);

            // Initialize system tray (depends on Window — kept outside DI)
            try
            {
                var localizationService = serviceProvider.GetRequiredService<LocalizationService>();
                trayService = new SystemTrayService(
                    mainWindow,
                    localizationService,
                    serviceProvider.GetRequiredService<Services.Diagnostics.LocalDiagnostics>(),
                    serviceProvider.GetRequiredService<ISystemTrayActions>());
                if (trayService.Initialize())
                {
                    mainWindow.SetSystemTray(trayService);
                }
                else
                {
                    trayService = null;
                }
            }
            catch (Exception ex)
            {
                LocalDiagnostics.LogSync(LogEntrySeverity.Warn, "App", $"SystemTrayService init failed: {ex.Message}");
            }

            // Clean up on app exit. The service provider is disposed by Program.RunSession.
            // Avalonia can raise Exit more than once for a single shutdown: the fatal crash
            // path calls the forced Shutdown(1), and the lifetime then replays its own
            // window-close shutdown. Cleanup disposes the CTS, so it must run exactly once.
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
                showWindowListener?.Dispose();
                launchGameListener?.Dispose();
                shutdownCts.Cancel();
                presentationSession.Dispose();
                trayService?.Dispose();
                shutdownCts.Dispose();
            };

            // Listen for show-window signal from second instances (cross-platform:
            // a plain second start or a forwarded launch brings this window up).
            showWindowListener = new ShowWindowSignalListener(presentationSession, Program.ShowWindowSignal!);

            // Listen for --launch-game forwards from second instances (cross-platform:
            // the Linux .desktop shortcut relies on it; on Unix the transport is a
            // local socket, since .NET has no named events outside Windows).
            launchGameListener = new LaunchGameSignalListener(presentationSession, Program.LaunchGameSignal!);

            // Register Opened handler BEFORE desktop.MainWindow is set — that assignment
            // may trigger the window to show and fire Opened synchronously.
            // Both handlers are one-shot (WindowOpenedOnce): Avalonia re-raises Opened on
            // every Show after a Hide, and a tray restore goes through Show, so a handler
            // left attached re-ran the launch flow on every restore-from-tray — popping the
            // "launcher minimized to tray" toast a second time.
            if (Program.FirstLaunch)
            {
                WindowOpenedOnce.Subscribe(mainWindow, (_, _) =>
                {
                    // Post at a priority that ensures layout/render/bindings are complete
                    // before we toggle visibility.
                    Dispatcher.UIThread.Post(() =>
                    {
                        // 首启不做完整初始化（快照由向导驱动后再加载），但动效偏好必须先行
                        // 应用，否则 IsMotionReduced 停留在字段默认 true，首启向导全程瞬切。
                        viewModel.ApplyFirstLaunchMotionPreference();
                        viewModel.Dialogs.ShowSetupWizard();
                    }, DispatcherPriority.Background);
                });
            }
            else
            {
                WindowOpenedOnce.Subscribe(mainWindow, (_, _) =>
                {
                    _ = InitializePresentationAsync(presentationSession, shutdownCts.Token);
                });
            }

            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task InitializeViewModelAsync(
        MainWindow mainWindow,
        MainWindowViewModel viewModel,
        ServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        try
        {
            var settingsService = serviceProvider.GetRequiredService<LauncherSettingsService>();
            var savedSettings = await settingsService.ReadAsync(cancellationToken);
            mainWindow.ApplySavedWindowState(savedSettings);
            await viewModel.InitializeAsync(cancellationToken);
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
                    toastMessage = serviceProvider
                        .GetRequiredService<LocalizationService>()
                        .F(LocalizationKeys.LauncherInitFailed, exception.Message);
                }
                catch (Exception localizationException)
                {
                    // 豁免：本地化失败的兜底路径——此时诊断/本地化本身不可用，
                    // Debug 输出是最后一级无依赖通道。
                    Debug.WriteLine($"Failure-toast localization unavailable: {localizationException.Message}");
                }

                await serviceProvider
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
                mainWindow.ApplySavedWindowState(viewModel.Settings.Editor.GetSavedSnapshot());
            }
        }
    }

    private static async Task InitializePresentationAsync(
        LauncherPresentationSession presentationSession,
        CancellationToken cancellationToken)
    {
        await presentationSession.InitializeAsync(cancellationToken);
        if (Program.LaunchGameRequested && !Program.FirstLaunch)
        {
            // The initial refresh has finished, so auto-launch uses the same
            // presentation entry point as a forwarded --launch-game request.
            await Dispatcher.UIThread.InvokeAsync(
                () => presentationSession.LaunchGameAsync(cancellationToken));
        }
    }

    private static async Task CompleteShutdownAsync(
        MainWindow mainWindow,
        MainWindowViewModel viewModel,
        ServiceProvider serviceProvider)
    {
        try
        {
            await viewModel.PrepareForShutdownAsync();

            if (viewModel.Settings.Editor.GetSavedSnapshot().RememberWindowPositionAndSize)
            {
                await serviceProvider.GetRequiredService<ISavedSettingsWriter>()
                    .UpdateAsync(mainWindow.CaptureWindowState);
            }
        }
        catch (Exception exception)
        {
            // 关窗持久化失败发生在每次正常退出路径上：必须写入本地日志，
            // 否则用户报告「窗口位置记不住」时无任何诊断线索（Debug 输出在
            // Release 构建不可见）。此刻 serviceProvider 尚未 Dispose，
            // LocalDiagnostics.ErrorAsync 自身全量吞异常，不会反向影响退出流程。
            await serviceProvider.GetRequiredService<LocalDiagnostics>().ErrorAsync(
                "Shutdown persistence failed.",
                exception,
                CancellationToken.None);
        }
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
                LocalDiagnostics.LogSync(LogEntrySeverity.Warn, "App", $"Window restore dispatch failed: {ex.Message}");
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
                    LocalDiagnostics.LogSync(LogEntrySeverity.Warn, "App", $"Launch-game signal dispatch failed: {ex.Message}");
                }
            })
        {
        }
    }
}
