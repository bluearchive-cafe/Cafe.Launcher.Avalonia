using System;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Cafe.Launcher.Avalonia.Constants;

namespace Cafe.Launcher.Avalonia.Services;

internal sealed record SystemTrayMenuText(
    string Title,
    string Show,
    string ShowToolTip,
    string Exit,
    string ExitToolTip);

internal interface ISystemTrayPlatform : IDisposable
{
    bool Initialize(
        SystemTrayMenuText text,
        Action showWindow,
        Action exitApplication);

    void UpdateText(SystemTrayMenuText text);
}

internal sealed class AvaloniaSystemTrayPlatform : ISystemTrayPlatform
{
    private TrayIcon? trayIcon;
    private NativeMenuItem? titleItem;
    private NativeMenuItem? showItem;
    private NativeMenuItem? exitItem;
    private Bitmap? menuIcon;
    private Action? showWindow;
    private Action? exitApplication;
    private bool disposed;

    public bool Initialize(
        SystemTrayMenuText text,
        Action showWindow,
        Action exitApplication)
    {
        this.showWindow = showWindow;
        this.exitApplication = exitApplication;

        using var iconStream = AssetLoader.Open(
            new Uri("avares://Cafe.Launcher.Avalonia/Assets/app-icon.ico"));
        menuIcon = LoadMenuIcon();
        var menu = CreateMenu();

        trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(iconStream),
            ToolTipText = LauncherConstants.ProductName,
            Menu = menu,
            IsVisible = true
        };
        trayIcon.Clicked += OnTrayClicked;
        UpdateText(text);
        return true;
    }

    public void UpdateText(SystemTrayMenuText text)
    {
        if (titleItem is not null)
        {
            titleItem.Header = text.Title;
        }

        if (showItem is not null)
        {
            showItem.Header = text.Show;
            showItem.ToolTip = text.ShowToolTip;
        }

        if (exitItem is not null)
        {
            exitItem.Header = text.Exit;
            exitItem.ToolTip = text.ExitToolTip;
        }

        if (trayIcon is not null)
        {
            trayIcon.ToolTipText = text.Title;
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
        showItem.Click += OnShowClicked;
        menu.Add(showItem);
        menu.Add(new NativeMenuItemSeparator());

        exitItem = new NativeMenuItem();
        exitItem.Click += OnExitClicked;
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

    private void OnTrayClicked(object? sender, EventArgs e) => showWindow?.Invoke();

    private void OnShowClicked(object? sender, EventArgs e) => showWindow?.Invoke();

    private void OnExitClicked(object? sender, EventArgs e) => exitApplication?.Invoke();

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        if (trayIcon is not null)
        {
            trayIcon.Clicked -= OnTrayClicked;
        }

        if (showItem is not null)
        {
            showItem.Click -= OnShowClicked;
        }

        if (exitItem is not null)
        {
            exitItem.Click -= OnExitClicked;
        }

        trayIcon?.Dispose();
        trayIcon = null;
        menuIcon?.Dispose();
        menuIcon = null;
        showWindow = null;
        exitApplication = null;
    }
}
