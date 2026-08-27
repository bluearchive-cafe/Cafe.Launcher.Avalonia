using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Cafe.Launcher.Avalonia.Composition;
using Cafe.Launcher.Avalonia.Services;
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
            serviceCollection.AddLauncherServices(existingLogger: Program.PreDiLogger);
            serviceProvider = serviceCollection.BuildServiceProvider();
            Program.ServiceProvider = serviceProvider;

            // Capture OS culture before any SetLanguage call so "auto"
            // can restore the genuine startup culture later.
            _ = serviceProvider.GetRequiredService<LocalizationService>();

            // Application-started trace (best-effort, fire-and-forget)
            _ = serviceProvider.GetRequiredService<Services.Diagnostics.LocalDiagnostics>()
                .DebugAsync("Application", "Application started, DI container built", CancellationToken.None);

            // Track install attribution (non-critical, best-effort)
            try
            {
                var clickCodeService = serviceProvider.GetRequiredService<ClickCodeService>();
                clickCodeService.SaveClickCode();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ClickCodeService.SaveClickCode failed: {ex.Message}");
            }

            var viewModel = serviceProvider.GetRequiredService<MainWindowViewModel>();
            var mainWindow = new MainWindow
            {
                DataContext = viewModel,
            };
            var shutdownDeferred = false;

            async void HandleShutdownRequested(object? _, ShutdownRequestedEventArgs eventArgs)
            {
                if (shutdownDeferred)
                {
                    eventArgs.Cancel = true;
                    return;
                }

                shutdownCts.Cancel();
                Task shutdownTask = CompleteShutdownAsync(
                    mainWindow,
                    viewModel,
                    serviceProvider);
                if (shutdownTask.IsCompletedSuccessfully)
                {
                    return;
                }

                eventArgs.Cancel = true;
                shutdownDeferred = true;
                try
                {
                    await shutdownTask;
                }
                catch (Exception exception)
                {
                    Debug.WriteLine($"Launcher shutdown coordination failed: {exception}");
                }
                finally
                {
                    desktop.Shutdown();
                }
            }

            desktop.ShutdownRequested += HandleShutdownRequested;
            mainWindow.ConfigureViewModel(viewModel);

            // Initialize system tray (depends on Window — kept outside DI)
            try
            {
                var localizationService = serviceProvider.GetRequiredService<LocalizationService>();
                trayService = new SystemTrayService(mainWindow, localizationService);
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
                Debug.WriteLine($"SystemTrayService init failed: {ex.Message}");
            }

            // Clean up on app exit. The service provider is disposed by Program.RunSession.
            desktop.Exit += (_, _) =>
            {
                desktop.ShutdownRequested -= HandleShutdownRequested;
                showWindowListener?.Dispose();
                shutdownCts.Cancel();
                viewModel.Dispose();
                trayService?.Dispose();
                shutdownCts.Dispose();
            };

            // Listen for show-window signal from second instances
            showWindowListener = ShowWindowSignalListener.Start(mainWindow, trayService, SignalName);

            // Register Opened handler BEFORE desktop.MainWindow is set — that assignment
            // may trigger the window to show and fire Opened synchronously.
            if (Program.FirstLaunch)
            {
                mainWindow.Opened += (_, _) =>
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
                };
            }
            else
            {
                mainWindow.Opened += (_, _) =>
                {
                    _ = InitializeViewModelAsync(mainWindow, viewModel, serviceProvider, shutdownCts.Token);
                };
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
            Debug.WriteLine($"MainWindowViewModel initialization failed: {exception}");
            try
            {
                // Initialization itself failed, so localization may be unavailable; keep an
                // English fallback so the toast never shows the raw "Localization unavailable." text.
                var toastMessage = "Launcher initialization failed.";
                try
                {
                    toastMessage = serviceProvider
                        .GetRequiredService<LocalizationService>()
                        .F("launcherInitFailed", exception.Message);
                }
                catch (Exception localizationException)
                {
                    Debug.WriteLine($"Failure-toast localization unavailable: {localizationException.Message}");
                }

                await serviceProvider
                    .GetRequiredService<IErrorHandlingService>()
                    .HandleErrorAsync("Launcher initialization failed.", exception,
                        new ErrorHandlingOptions { ToastMessage = toastMessage });
            }
            catch (Exception diagnosticsException)
            {
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

    private static async Task CompleteShutdownAsync(
        MainWindow mainWindow,
        MainWindowViewModel viewModel,
        ServiceProvider serviceProvider)
    {
        try
        {
            await viewModel.PrepareForShutdownAsync();

            var settings = viewModel.Settings.Editor.GetSavedSnapshot();
            if (settings.RememberWindowPositionAndSize)
            {
                mainWindow.CaptureWindowState(settings);
                await serviceProvider.GetRequiredService<LauncherSettingsService>().SaveAsync(settings);
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Launcher shutdown persistence failed: {exception}");
        }
    }

    private sealed class ShowWindowSignalListener : IDisposable
    {
        private readonly MainWindow mainWindow;
        private readonly SystemTrayService? trayService;
        private readonly EventWaitHandle signal;
        private readonly CancellationTokenSource cancellationTokenSource = new();
        private readonly CancellationToken cancellationToken;
        private readonly Task listenerTask;
        private bool disposed;

        private ShowWindowSignalListener(MainWindow mainWindow, SystemTrayService? trayService, string signalName)
        {
            this.mainWindow = mainWindow;
            this.trayService = trayService;
            cancellationToken = cancellationTokenSource.Token;
            signal = new EventWaitHandle(false, EventResetMode.AutoReset, signalName);
            listenerTask = Task.Run(Listen, cancellationToken);
        }

        public static ShowWindowSignalListener? Start(
            MainWindow mainWindow,
            SystemTrayService? trayService,
            string signalName)
        {
            return OperatingSystem.IsWindows()
                ? new ShowWindowSignalListener(mainWindow, trayService, signalName)
                : null;
        }

        private void Listen()
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (!signal.WaitOne(TimeSpan.FromMilliseconds(250)))
                    {
                        continue;
                    }

                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            return;
                        }

                        try
                        {
                            if (trayService is not null)
                                trayService.ShowWindow();
                            else
                                mainWindow.ShowWindow();
                        }
                        catch (Exception ex)
                        {
                            // Restore is best-effort.
                            Debug.WriteLine($"Window restore dispatch failed: {ex.Message}");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                // Listener stopped — non-critical.
                Debug.WriteLine($"ShowWindowSignalListener loop exited: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            cancellationTokenSource.Cancel();

            try
            {
                // The listener polls every 250ms; after cancellation it exits within ~250ms.
                // Using a matching timeout avoids needlessly blocking the cleanup thread.
                listenerTask.Wait(TimeSpan.FromMilliseconds(300));
            }
            catch
            {
                // Exit cleanup is best-effort.
            }

            signal.Dispose();
            cancellationTokenSource.Dispose();
        }
    }
}
