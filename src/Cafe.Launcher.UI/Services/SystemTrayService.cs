using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Cafe.Launcher.UI.Constants;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Services.Diagnostics;

namespace Cafe.Launcher.UI.Services;

/// <summary>
/// Manages minimize-to-tray behavior while delegating native tray construction
/// to an internal platform adapter.
/// </summary>
internal sealed class SystemTrayService : IDisposable
{
    private readonly string productName;
    private readonly Window mainWindow;
    private readonly LocalizationService localizer;
    private readonly ISystemTrayPlatform platform;
    private readonly ILauncherDiagnostics? diagnostics;
    private readonly ISystemTrayActions? actions;
    private bool initialized;
    private bool disposed;

    public SystemTrayService(
        LauncherProductProfile productProfile,
        Window mainWindow,
        LocalizationService localizer,
        ILauncherDiagnostics? diagnostics = null,
        ISystemTrayActions? actions = null)
        : this(
            productProfile,
            mainWindow,
            localizer,
            new AvaloniaSystemTrayPlatform(productProfile),
            diagnostics,
            actions)
    {
    }

    internal SystemTrayService(
        LauncherProductProfile productProfile,
        Window mainWindow,
        LocalizationService localizer,
        ISystemTrayPlatform platform,
        ILauncherDiagnostics? diagnostics = null,
        ISystemTrayActions? actions = null)
    {
        productName = productProfile.ProductName;
        this.mainWindow = mainWindow;
        this.localizer = localizer;
        this.platform = platform;
        this.diagnostics = diagnostics;
        this.actions = actions;
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
                ExitApplication,
                StartGame,
                OpenSettings);
            if (!initialized)
            {
                Dispose();
                return false;
            }

            localizer.LanguageChanged += OnLanguageChanged;
            if (actions is not null)
            {
                actions.Changed += RefreshMenu;
            }
            return true;
        }
        catch (Exception ex)
        {
            _ = diagnostics?.WarningAsync("SystemTray", $"initialization failed: {ex.Message}");
            Dispose();
            return false;
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => RefreshMenu();

    private void RefreshMenu()
    {
        if (disposed)
        {
            return;
        }

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(RefreshMenu);
            return;
        }

        platform.UpdateText(CreateMenuText());
    }

    private void StartGame()
    {
        if (!disposed && actions?.CanStartGame == true)
        {
            ShowWindow();
            actions.StartGame();
        }
    }

    private void OpenSettings()
    {
        if (!disposed && actions?.CanOpenSettings == true)
        {
            ShowWindow();
            actions.OpenSettings();
        }
    }

    private SystemTrayMenuText CreateMenuText() =>
        new(
            productName,
            localizer.T(LocalizationKeys.ShowLauncher),
            localizer.T(LocalizationKeys.TrayOpenLauncher),
            localizer.T(LocalizationKeys.ExitLauncher),
            localizer.T(LocalizationKeys.TrayExitLauncher),
            localizer.T(actions?.IsGameRunning == true ? LocalizationKeys.GameSessionRunning
                : actions?.IsGameStarting == true ? LocalizationKeys.GameSessionStarting
                : LocalizationKeys.StartGame),
            actions?.CanStartGame == true,
            localizer.T(LocalizationKeys.Settings),
            actions?.CanOpenSettings == true);

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
            if (actions is not null)
            {
                actions.Changed -= RefreshMenu;
            }
        }

        platform.Dispose();
    }
}
