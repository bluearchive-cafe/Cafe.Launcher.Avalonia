using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Cafe.Launcher.UI.Constants;
using Cafe.Launcher.Core.Constants;

namespace Cafe.Launcher.UI.Services;

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

        // 托盘必须挂在 Application 上：Avalonia 12.1.3 起原生图标（ITrayIconImpl）的建与销
        // 都发生在 TrayIcon 进入 Application 级 TrayIcons 集合时触发的 Attach/Detach 里，
        // 构造一个 TrayIcon 再设属性、设 IsVisible 都是空操作——通知区域不会有图标，
        // 而调用方拿到的仍是「成功」，于是关窗隐藏后无法找回窗口。
        if (Application.Current is not { } application)
        {
            return false;
        }

        using var iconStream = AssetLoader.Open(
            new Uri("avares://Cafe.Launcher.UI/Assets/app-icon.ico"));
        var menu = CreateMenu();

        trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(iconStream),
            ToolTipText = LauncherConstants.ProductName,
            Menu = menu,
            IsVisible = true
        };

        var icons = TrayIcon.GetIcons(application);
        if (icons is null)
        {
            icons = new TrayIcons();
            TrayIcon.SetIcons(application, icons);
        }

        // Attach 在这一次 Add 里读取上面已经设好的图标、菜单与可见性。
        icons.Add(trayIcon);
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

        if (trayIcon is not null)
        {
            // 集合移除才会走 Detach 销毁原生图标；留在集合里既会让图标残留在通知区域，
            // 也会让下一次初始化在同一集合上重复累积。
            if (Application.Current is { } application)
            {
                TrayIcon.GetIcons(application)?.Remove(trayIcon);
            }

            trayIcon.Dispose();
            trayIcon = null;
        }

        showWindow = null;
        exitApplication = null;
        startGame = null;
        openSettings = null;
    }
}
