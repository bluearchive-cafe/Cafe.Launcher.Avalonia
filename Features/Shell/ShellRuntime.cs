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
    private bool disposed;

    /// <summary>Initializes and wires the shell lifecycle with its presentation collaborators.</summary>
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
        : this(
            launcherCoreService,
            settingsService,
            localizer,
            toastService,
            launcherUpdateService,
            diagnostics,
            errorHandling,
            windowsAnimationSettingsProvider,
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
            modalHost,
            ownsPresentationCollaborators: false)
    {
    }

    internal ShellRuntime(
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
        ModalHostViewModel modalHost,
        bool ownsPresentationCollaborators)
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
            modalHost,
            ownsPresentationCollaborators);
        lifecycle.StatusDetailModeChanged += OnStatusDetailModeChanged;
        lifecycle.Wire();
        lifecycle.ApplyInitialLanguage();
    }

    /// <summary>Raised when shell presentation state changes.</summary>
    public event Action? PresentationChanged;

    /// <summary>Raised when the configured status-detail mode changes.</summary>
    public event Action? StatusDetailModeChanged;

    /// <summary>Gets whether the shell is currently processing an operation.</summary>
    public bool IsBusy => isBusy;

    /// <summary>Gets whether reduced motion is currently effective.</summary>
    public bool IsMotionReduced => isMotionReduced;

    /// <summary>Gets the startup update-check task, if one was scheduled.</summary>
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

    /// <summary>Initializes the shell state after the main window opens.</summary>
    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        lifecycle.InitializeAsync(cancellationToken);

    /// <summary>Refreshes launcher state and its shell presentation.</summary>
    public Task RefreshAsync(CancellationToken cancellationToken = default) =>
        lifecycle.RefreshAsync(cancellationToken);

    /// <summary>Cancels lifecycle work and waits for active refreshes to finish before shutdown.</summary>
    public Task PrepareForShutdownAsync() => lifecycle.PrepareForShutdownAsync();

    /// <summary>Re-evaluates the system motion preference and updates presentation state.</summary>
    public void RefreshSystemMotionPreference() => lifecycle.RefreshSystemMotionPreference();

    /// <summary>Refreshes shell state after a completed game operation.</summary>
    public Task HandleOperationsRefreshRequestedAsync(GameOperationsRefreshMode mode) =>
        lifecycle.HandleOperationsRefreshRequestedAsync(mode);

    /// <summary>Attempts to handle Escape through the active shell surface.</summary>
    public bool TryHandleEscape() => lifecycle.TryHandleEscape();

    /// <summary>Unsubscribes shell events and releases lifecycle-owned resources.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
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
