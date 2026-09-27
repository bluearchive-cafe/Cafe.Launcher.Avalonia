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
using Cafe.Launcher.Core.Services.Diagnostics;
using Cafe.Launcher.Core.Services.Update;
using Cafe.Launcher.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cafe.Launcher.Avalonia.ViewModels;

internal partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly ShellLifecycle runtime;
    private bool disposed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMotionEnabled))]
    private bool isMotionReduced = true;

    [ObservableProperty]
    private bool isBusy;

    public bool IsMotionEnabled => !IsMotionReduced;
    public bool IsStatusDetailHidden => Settings.Editor.Current.StatusDetailMode == StatusDetailModes.Hidden;
    public bool IsPlatformSpecificSettingsVisible =>
        Shell.IsLinuxPlatform || showHiddenSettings;

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
    public LogExportDialogViewModel LogExport { get; }
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

    private readonly bool showHiddenSettings;

    public MainWindowViewModel(
        ShellPresentationFamily family,
        ShellLifecycle runtime,
        PresentationOptions? options = null)
    {
        this.runtime = runtime;
        showHiddenSettings = options?.ShowHiddenSettings ?? false;
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
        LogExport = family.LogExport;
        Debug = family.Debug;
        ModalHost = family.ModalHost;

        runtime.PresentationChanged += OnRuntimePresentationChanged;
        runtime.StatusDetailModeChanged += OnStatusDetailModeChanged;
        OnRuntimePresentationChanged();
    }

    internal MainWindowViewModel(
        ILauncherCoreService launcherCoreService,
        ILauncherSettingsService settingsService,
        ISavedSettingsWriter savedSettingsWriter,
        LocalizationService localizer,
        ToastService toastService,
        ILauncherUpdateService launcherUpdateService,
        ILauncherSelfUpdateService launcherSelfUpdateService,
        IWindowsLauncherUpdateApplier launcherUpdateApplier,
        ILauncherDiagnostics diagnostics,
        ShellPresentationFamily family,
        IErrorHandlingService errorHandling,
        SystemAnimationSettingsProvider systemAnimationSettingsProvider,
        IFilePickerService filePickerService,
        LauncherBuildIdentity? buildIdentity = null,
        PresentationOptions? options = null)
        : this(
            family,
            new ShellLifecycle(
                launcherCoreService,
                settingsService,
                savedSettingsWriter,
                localizer,
                toastService,
                launcherUpdateService,
                launcherSelfUpdateService,
                launcherUpdateApplier,
                diagnostics,
                errorHandling,
                systemAnimationSettingsProvider,
                family,
                filePickerService,
                ownsPresentationCollaborators: true,
                buildIdentity: buildIdentity),
            options)
    {
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        runtime.InitializeAsync(cancellationToken);

    /// <summary>
    /// 首启分支不执行完整初始化（快照由向导驱动后再加载）；动效偏好需在向导显示前
    /// 按默认配置先行应用，否则首启向导全程处于降动效。
    /// </summary>
    public void ApplyFirstLaunchMotionPreference() =>
        runtime.ApplyFirstLaunchMotionPreference();

    public Task PrepareForShutdownAsync() => runtime.PrepareForShutdownAsync();

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
