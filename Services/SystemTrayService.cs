using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Cafe.Launcher.Avalonia.Constants;

namespace Cafe.Launcher.Avalonia.Services;

/// <summary>
/// Manages minimize-to-tray behavior while delegating native tray construction
/// to an internal platform adapter.
/// </summary>
public sealed class SystemTrayService : IDisposable
{
    private readonly Window mainWindow;
    private readonly LocalizationService localizer;
    private readonly ISystemTrayPlatform platform;
    private bool initialized;
    private bool disposed;

    public SystemTrayService(Window mainWindow, LocalizationService localizer)
        : this(mainWindow, localizer, new AvaloniaSystemTrayPlatform())
    {
    }

    internal SystemTrayService(
        Window mainWindow,
        LocalizationService localizer,
        ISystemTrayPlatform platform)
    {
        this.mainWindow = mainWindow;
        this.localizer = localizer;
        this.platform = platform;
    }

    public bool Initialize()
    {
        if (disposed)
        {
            return false;
        }

        if (initialized)
        {
            return true;
        }

        try
        {
            initialized = platform.Initialize(
                CreateMenuText(),
                ShowWindow,
                ExitApplication);
            if (!initialized)
            {
                Dispose();
                return false;
            }

            localizer.LanguageChanged += OnLanguageChanged;
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"SystemTrayService initialization failed: {ex.Message}");
            Dispose();
            return false;
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            platform.UpdateText(CreateMenuText());
            return;
        }

        Dispatcher.UIThread.Post(() => platform.UpdateText(CreateMenuText()));
    }

    private SystemTrayMenuText CreateMenuText() =>
        new(
            LauncherConstants.ProductName,
            localizer.T("showLauncher"),
            localizer.T("trayOpenLauncher"),
            localizer.T("exitLauncher"),
            localizer.T("trayExitLauncher"));

    public void ShowWindow()
    {
        mainWindow.Show();
        mainWindow.WindowState = WindowState.Normal;
        mainWindow.Activate();
    }

    public void HideWindow()
    {
        mainWindow.Hide();
    }

    private void ExitApplication()
    {
        Dispose();

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.TryShutdown();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        if (initialized)
        {
            localizer.LanguageChanged -= OnLanguageChanged;
        }

        platform.Dispose();
    }
}
