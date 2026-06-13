using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Threading;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Markup.Xaml;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.ViewModels;
using Cafe.Launcher.Avalonia.Views;

namespace Cafe.Launcher.Avalonia;

public partial class App : Application
{
    private const string SignalName = @"Global\Cafe_Launcher_SI_Show";
    private readonly CancellationTokenSource shutdownCts = new();
    private LauncherApplicationServices? services;
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
            services = new LauncherApplicationServices();

            // Track install attribution (non-critical, best-effort)
            try
            {
                services.ClickCodeService.SaveClickCode();
            }
            catch
            {
                // Non-critical — continue without click code
            }

            var viewModel = services.CreateMainWindowViewModel();
            var mainWindow = new MainWindow
            {
                DataContext = viewModel,
            };
            mainWindow.ConfigureViewModel(viewModel);

            // Initialize system tray (non-critical, best-effort)
            try
            {
                trayService = new SystemTrayService(mainWindow, services.LocalizationService);
                trayService.Initialize();
                mainWindow.SetSystemTray(trayService);
            }
            catch
            {
                // Non-critical — continue without system tray
            }

            // Destroy tray icon on app exit
            desktop.Exit += (_, _) =>
            {
                showWindowListener?.Dispose();
                shutdownCts.Cancel();
                viewModel.Dispose();
                trayService?.Dispose();
                services?.Dispose();
                shutdownCts.Dispose();
            };

            // Listen for show-window signal from second instances
            showWindowListener = ShowWindowSignalListener.Start(mainWindow, trayService, SignalName);

            desktop.MainWindow = mainWindow;
            _ = viewModel.InitializeAsync(shutdownCts.Token);
        }

        base.OnFrameworkInitializationCompleted();
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
                        catch
                        {
                            // Restore is best-effort.
                        }
                    });
                }
            }
            catch
            {
                // Listener stopped — non-critical.
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
