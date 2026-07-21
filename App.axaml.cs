using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
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

            var diagnostics = serviceProvider.GetRequiredService<Services.Diagnostics.LocalDiagnostics>();
            try
            {
                LibVLCSharp.Shared.Core.Initialize();
            }
            catch (Exception ex)
            {
                _ = diagnostics.ErrorAsync("LibVlcInitialize", ex, CancellationToken.None);
            }

            // Application-started trace (best-effort, fire-and-forget)
            _ = diagnostics.DebugAsync(
                "Application", "Application started, DI container built", CancellationToken.None);

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
            mainWindow.ConfigureViewModel(viewModel);
            if (Program.PreviousSessionCrashed)
                viewModel.Dialogs.ShowCrashRecovery();

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

            // Clean up on app exit. The service provider is disposed by
            // Program.RunSession after the session-end entry is written
            // so the logger stays alive through CompleteSessionAsync.
            desktop.Exit += (_, _) =>
            {
                showWindowListener?.Dispose();
                shutdownCts.Cancel();
                viewModel.Dispose();
                trayService?.Dispose();
                // serviceProvider is disposed by Program.RunSession
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
                    Dispatcher.UIThread.Post(() => viewModel.Dialogs.ShowSetupWizard(), DispatcherPriority.Background);
                };
            }
            else
            {
                mainWindow.Opened += (_, _) =>
                {
                    _ = InitializeViewModelAsync(viewModel, serviceProvider, shutdownCts.Token);
                };
            }

            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task InitializeViewModelAsync(
        MainWindowViewModel viewModel,
        ServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        try
        {
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
                await serviceProvider
                    .GetRequiredService<Services.Diagnostics.LocalDiagnostics>()
                    .ErrorAsync("Launcher initialization failed.", exception, CancellationToken.None);
            }
            catch (Exception diagnosticsException)
            {
                Debug.WriteLine($"Initialization diagnostics failed: {diagnosticsException.Message}");
            }

            try
            {
                serviceProvider
                    .GetRequiredService<ToastService>()
                    .ShowError(exception.Message);
            }
            catch (Exception toastException)
            {
                Debug.WriteLine($"Initialization toast failed: {toastException.Message}");
            }
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
