using System;
using Avalonia.Controls;
using Avalonia.Platform;
using Cafe.Launcher.Avalonia.Constants;

namespace Cafe.Launcher.Avalonia.Services;

internal sealed record SystemTrayMenuText(
    string Title,
    string Show,
    string ShowToolTip,
    string Exit,
    string ExitToolTip,
    string StartGame,
    bool CanStartGame,
    string Settings,
    bool CanOpenSettings);

internal interface ISystemTrayPlatform : IDisposable
{
    bool Initialize(
        SystemTrayMenuText text,
        Action showWindow,
        Action exitApplication,
        Action startGame,
        Action openSettings);

    void UpdateText(SystemTrayMenuText text);
}

internal sealed class AvaloniaSystemTrayPlatform : ISystemTrayPlatform
{
    private TrayIcon? trayIcon;
    private NativeMenuItem? titleItem;
    private NativeMenuItem? showItem;
    private NativeMenuItem? exitItem;
    private NativeMenuItem? startItem;
    private NativeMenuItem? settingsItem;
    private Action? showWindow;
    private Action? exitApplication;
    private Action? startGame;
    private Action? openSettings;
    private bool disposed;

    public bool Initialize(
        SystemTrayMenuText text,
        Action showWindow,
        Action exitApplication,
        Action startGame,
        Action openSettings)
    {
        this.showWindow = showWindow;
        this.exitApplication = exitApplication;
        this.startGame = startGame;
        this.openSettings = openSettings;

        using var iconStream = AssetLoader.Open(
            new Uri("avares://Cafe.Launcher.Avalonia/Assets/app-icon.ico"));
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

        if (startItem is not null)
        {
            startItem.Header = text.StartGame;
            startItem.IsEnabled = text.CanStartGame;
        }

        if (settingsItem is not null)
        {
            settingsItem.Header = text.Settings;
            settingsItem.IsEnabled = text.CanOpenSettings;
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
            IsEnabled = false
        };
        menu.Add(titleItem);
        menu.Add(new NativeMenuItemSeparator());

        showItem = new NativeMenuItem();
        showItem.Click += OnShowClicked;
        menu.Add(showItem);

        startItem = new NativeMenuItem();
        startItem.Click += OnStartClicked;
        menu.Add(startItem);
        menu.Add(new NativeMenuItemSeparator());

        settingsItem = new NativeMenuItem();
        settingsItem.Click += OnSettingsClicked;
        menu.Add(settingsItem);
        menu.Add(new NativeMenuItemSeparator());

        exitItem = new NativeMenuItem();
        exitItem.Click += OnExitClicked;
        menu.Add(exitItem);

        return menu;
    }

    private void OnTrayClicked(object? sender, EventArgs e) => showWindow?.Invoke();

    private void OnShowClicked(object? sender, EventArgs e) => showWindow?.Invoke();

    private void OnExitClicked(object? sender, EventArgs e) => exitApplication?.Invoke();

    private void OnStartClicked(object? sender, EventArgs e) => startGame?.Invoke();

    private void OnSettingsClicked(object? sender, EventArgs e) => openSettings?.Invoke();

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

        if (startItem is not null)
        {
            startItem.Click -= OnStartClicked;
        }

        if (settingsItem is not null)
        {
            settingsItem.Click -= OnSettingsClicked;
        }

        trayIcon?.Dispose();
        trayIcon = null;
        showWindow = null;
        exitApplication = null;
        startGame = null;
        openSettings = null;
    }
}
