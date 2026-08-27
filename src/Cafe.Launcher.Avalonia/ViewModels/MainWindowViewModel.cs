using System;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Features.Diagnostics;
using Cafe.Launcher.Avalonia.Features.GameOperations;
using Cafe.Launcher.Avalonia.Features.ResourcePanel;
using Cafe.Launcher.Avalonia.Features.Settings;
using Cafe.Launcher.Avalonia.Features.Shell;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cafe.Launcher.Avalonia.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly IShellRuntime runtime;
    private bool disposed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMotionEnabled))]
    private bool isMotionReduced = true;

    [ObservableProperty]
    private bool isBusy;

    public bool IsMotionEnabled => !IsMotionReduced;
    public bool IsStatusDetailHidden => Settings.Editor.Current.StatusDetailMode == StatusDetailModes.Hidden;

    public ShellViewModel Shell { get; }
    public BackgroundViewModel Background { get; }
    public RemoteContentViewModel RemoteContent { get; }
    public DialogsViewModel Dialogs { get; }
    public GameOperationsViewModel Operations { get; }
    public ToastHostViewModel Toasts { get; }
    public WindowChromeViewModel WindowChrome { get; }
    public SettingsViewModel Settings { get; }
    public ResourcePanelViewModel ResourcePanel { get; }
    public LogViewerDialogViewModel LogViewer { get; }
    public DebugViewModel Debug { get; }
    public ModalHostViewModel ModalHost { get; }

    public bool IsDebugFeaturesEnabled
    {
        get
        {
#if DEBUG
            return true;
#else
            return false;
#endif
        }
    }

    internal Task PendingStartupUpdateCheck => runtime.PendingStartupUpdateCheck;

    public MainWindowViewModel(
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
        IShellRuntime runtime)
    {
        this.runtime = runtime;
        Shell = shell;
        Background = background;
        RemoteContent = remoteContent;
        Dialogs = dialogs;
        Operations = operations;
        Toasts = toasts;
        WindowChrome = windowChrome;
        Settings = settings;
        ResourcePanel = resourcePanel;
        LogViewer = logViewer;
        Debug = debug;
        ModalHost = modalHost;

        runtime.PresentationChanged += OnRuntimePresentationChanged;
        runtime.StatusDetailModeChanged += OnStatusDetailModeChanged;
        OnRuntimePresentationChanged();
    }

    internal MainWindowViewModel(
        ILauncherCoreService launcherCoreService,
        LauncherSettingsService settingsService,
        LocalizationService localizer,
        ToastService toastService,
        LauncherUpdateService launcherUpdateService,
        LocalDiagnostics diagnostics,
        ShellViewModel shell,
        BackgroundViewModel background,
        RemoteContentViewModel remoteContent,
        DialogsViewModel dialogs,
        GameOperationsViewModel operations,
        ToastHostViewModel toasts,
        WindowChromeViewModel windowChrome,
        SettingsViewModel settings,
        ResourcePanelViewModel resourcePanel,
        IErrorHandlingService errorHandling,
        LogViewerDialogViewModel logViewer,
        DebugViewModel debug,
        ModalHostViewModel modalHost,
        WindowsAnimationSettingsProvider windowsAnimationSettingsProvider)
        : this(
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
            new ShellRuntime(
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
                ownsPresentationCollaborators: true))
    {
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        runtime.InitializeAsync(cancellationToken);

    /// <inheritdoc cref="IShellRuntime.ApplyFirstLaunchMotionPreference" />
    public void ApplyFirstLaunchMotionPreference() =>
        runtime.ApplyFirstLaunchMotionPreference();

    internal Task PrepareForShutdownAsync() => runtime.PrepareForShutdownAsync();

    public void RefreshSystemMotionPreference() => runtime.RefreshSystemMotionPreference();

    [RelayCommand]
    private Task RefreshAsync(CancellationToken cancellationToken = default) =>
        runtime.RefreshAsync(cancellationToken);

    public bool TryHandleEscape() => runtime.TryHandleEscape();

    internal Task HandleOperationsRefreshRequestedAsync(GameOperationsRefreshMode mode) =>
        runtime.HandleOperationsRefreshRequestedAsync(mode);

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        runtime.PresentationChanged -= OnRuntimePresentationChanged;
        runtime.StatusDetailModeChanged -= OnStatusDetailModeChanged;
        runtime.Dispose();
    }

    private void OnRuntimePresentationChanged()
    {
        IsBusy = runtime.IsBusy;
        IsMotionReduced = runtime.IsMotionReduced;
    }

    private void OnStatusDetailModeChanged()
    {
        OnPropertyChanged(nameof(IsStatusDetailHidden));
    }
}
