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
    public bool IsPlatformSpecificSettingsVisible =>
        Shell.IsLinuxPlatform || Program.ShowHiddenSettings;

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
        ShellPresentationFamily family,
        IShellRuntime runtime)
    {
        this.runtime = runtime;
        Shell = family.Shell;
        Background = family.Background;
        RemoteContent = family.RemoteContent;
        Dialogs = family.Dialogs;
        Operations = family.Operations;
        Toasts = family.Toasts;
        WindowChrome = family.WindowChrome;
        Settings = family.Settings;
        ResourcePanel = family.ResourcePanel;
        LogViewer = family.LogViewer;
        Debug = family.Debug;
        ModalHost = family.ModalHost;

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
        ShellPresentationFamily family,
        IErrorHandlingService errorHandling,
        WindowsAnimationSettingsProvider windowsAnimationSettingsProvider,
        IFilePickerService filePickerService)
        : this(
            family,
            new ShellLifecycle(
                launcherCoreService,
                settingsService,
                localizer,
                toastService,
                launcherUpdateService,
                diagnostics,
                errorHandling,
                windowsAnimationSettingsProvider,
                family,
                filePickerService,
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
