using System;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Features.Diagnostics;
using Cafe.Launcher.Avalonia.Features.GameOperations;
using Cafe.Launcher.Avalonia.Features.ResourcePanel;
using Cafe.Launcher.Avalonia.Features.Settings;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.ViewModels;

namespace Cafe.Launcher.Avalonia.Features.Shell;

/// <summary>
/// Deep shell module: owns lifecycle assembly and presents a small runtime interface.
/// </summary>
public sealed class ShellRuntime : IShellRuntime, IShellLifecyclePresentation
{
    private readonly ShellLifecycle lifecycle;
    private bool isBusy;
    private bool isMotionReduced = true;

    public ShellRuntime(
        ILauncherCoreService launcherCoreService,
        LauncherSettingsService settingsService,
        LocalizationService localizer,
        ToastService toastService,
        LauncherUpdateService launcherUpdateService,
        LocalDiagnostics diagnostics,
        IErrorHandlingService errorHandling,
        WindowsAnimationSettingsProvider windowsAnimationSettingsProvider,
        ShellViewModel shell,
        BackgroundViewModel background,
        RemoteContentViewModel remoteContent,
        DialogsViewModel dialogs,
        GameOperationsViewModel operations,
        ToastHostViewModel toasts,
        WindowChromeViewModel windowChrome,
        SettingsViewModel settings,
        ResourcePanelViewModel resourcePanel,
        LogViewerDialogViewModel logViewer,
        DebugViewModel debug,
        ModalHostViewModel modalHost)
    {
        lifecycle = new ShellLifecycle(
            launcherCoreService,
            settingsService,
            localizer,
            toastService,
            launcherUpdateService,
            diagnostics,
            errorHandling,
            windowsAnimationSettingsProvider,
            this,
            shell,
            background,
            remoteContent,
            dialogs,
            operations,
            toasts,
            windowChrome,
            settings,
            resourcePanel,
            logViewer,
            debug,
            modalHost);
        lifecycle.StatusDetailModeChanged += OnStatusDetailModeChanged;
        lifecycle.Wire();
        lifecycle.ApplyInitialLanguage();
    }

    public event Action? PresentationChanged;
    public event Action? StatusDetailModeChanged;

    public bool IsBusy => isBusy;
    public bool IsMotionReduced => isMotionReduced;
    public Task PendingStartupUpdateCheck => lifecycle.PendingStartupUpdateCheck;

    bool IShellLifecyclePresentation.IsBusy
    {
        get => isBusy;
        set => SetPresentationState(ref isBusy, value);
    }

    bool IShellLifecyclePresentation.IsMotionReduced
    {
        get => isMotionReduced;
        set => SetPresentationState(ref isMotionReduced, value);
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        lifecycle.InitializeAsync(cancellationToken);

    public Task RefreshAsync(CancellationToken cancellationToken = default) =>
        lifecycle.RefreshAsync(cancellationToken);

    public void RefreshSystemMotionPreference() => lifecycle.RefreshSystemMotionPreference();

    public Task HandleOperationsRefreshRequestedAsync(GameOperationsRefreshMode mode) =>
        lifecycle.HandleOperationsRefreshRequestedAsync(mode);

    public bool TryHandleEscape() => lifecycle.TryHandleEscape();

    public void Dispose()
    {
        lifecycle.StatusDetailModeChanged -= OnStatusDetailModeChanged;
        lifecycle.Dispose();
    }

    private void OnStatusDetailModeChanged() => StatusDetailModeChanged?.Invoke();

    private void SetPresentationState(ref bool field, bool value)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        PresentationChanged?.Invoke();
    }
}
