using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Cafe.Launcher.Avalonia.Constants;

namespace Cafe.Launcher.Avalonia.Services;

/// <summary>
/// Manages the system tray icon for minimize-to-tray behavior.
/// Uses Avalonia 12 built-in TrayIcon and NativeMenu APIs.
/// </summary>
public sealed class SystemTrayService : IDisposable
{
    private TrayIcon? trayIcon;
    private NativeMenuItem? titleItem;
    private NativeMenuItem? showItem;
    private NativeMenuItem? exitItem;
    private Bitmap? menuIcon;
    private readonly Window mainWindow;
    private readonly LocalizationService localizer;

    public SystemTrayService(Window mainWindow, LocalizationService localizer)
    {
        this.mainWindow = mainWindow;
        this.localizer = localizer;
    }

    public void Initialize()
    {
        try
        {
            // Load the icon from embedded Avalonia resource via AssetLoader (replaces removed AvaloniaLocator.Current)
            using var iconStream = AssetLoader.Open(
                new Uri("avares://Cafe.Launcher.Avalonia/Assets/avalonia-logo.ico"));
            menuIcon = LoadMenuIcon();
            var menu = CreateMenu();

            trayIcon = new TrayIcon
            {
                Icon = new WindowIcon(iconStream),
                ToolTipText = LauncherConstants.ProductName,
                Menu = menu,
                IsVisible = true
            };

            trayIcon.Clicked += (_, _) => ShowWindow();
            localizer.LanguageChanged += OnLanguageChanged;
            UpdateMenuText();
        }
        catch (Exception ex)
        {
            // Tray icon is non-critical — log and continue without it
            System.Diagnostics.Debug.WriteLine($"SystemTrayService initialization failed: {ex.Message}");
        }
    }

    private NativeMenu CreateMenu()
    {
        var menu = new NativeMenu();

        titleItem = new NativeMenuItem(LauncherConstants.ProductName)
        {
            Icon = menuIcon,
            IsEnabled = false
        };
        menu.Add(titleItem);

        menu.Add(new NativeMenuItemSeparator());

        showItem = new NativeMenuItem();
        showItem.Click += (_, _) => ShowWindow();
        menu.Add(showItem);

        menu.Add(new NativeMenuItemSeparator());

        exitItem = new NativeMenuItem();
        exitItem.Click += (_, _) => ExitApplication();
        menu.Add(exitItem);

        return menu;
    }

    private static Bitmap? LoadMenuIcon()
    {
        try
        {
            using var stream = AssetLoader.Open(
                new Uri("avares://Cafe.Launcher.Avalonia/Assets/notification-8be8201c.png"));
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            UpdateMenuText();
            return;
        }

        Dispatcher.UIThread.Post(UpdateMenuText);
    }

    private void UpdateMenuText()
    {
        if (titleItem is not null)
        {
            titleItem.Header = LauncherConstants.ProductName;
        }

        if (showItem is not null)
        {
            showItem.Header = localizer.T("showLauncher");
            showItem.ToolTip = localizer.T("trayOpenLauncher");
        }

        if (exitItem is not null)
        {
            exitItem.Header = localizer.T("exitLauncher");
            exitItem.ToolTip = localizer.T("trayExitLauncher");
        }

        if (trayIcon is not null)
        {
            trayIcon.ToolTipText = LauncherConstants.ProductName;
        }
    }

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
            desktop.Shutdown();
        }
    }

    public void Dispose()
    {
        localizer.LanguageChanged -= OnLanguageChanged;
        trayIcon?.Dispose();
        trayIcon = null;
        menuIcon?.Dispose();
        menuIcon = null;
    }
}
