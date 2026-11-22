using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.UI.Features.Diagnostics;
using Cafe.Launcher.UI.Features.Games;
using Cafe.Launcher.UI.Features.GameOperations;
using Cafe.Launcher.UI.Features.ResourcePanel;
using Cafe.Launcher.UI.Features.Settings;
using Cafe.Launcher.UI.Features.Shell;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.Core.Services.Diagnostics;
using Cafe.Launcher.Core.Services.Update;
using Cafe.Launcher.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Services;

namespace Cafe.Launcher.UI.ViewModels;

internal partial class MainWindowViewModel : ObservableObject, IDisposable
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

    public ShellViewModel Shell => Games?.Active.Shell ?? defaultShell;
    public BackgroundViewModel Background { get; }
    public RemoteContentViewModel RemoteContent => Games?.Active.RemoteContent ?? defaultRemoteContent;
    public DialogsViewModel Dialogs { get; }
    public GameOperationsViewModel Operations => Games?.Active.Operations ?? defaultOperations;
    public GameLibraryViewModel? Games { get; }
    public bool HasGameLibrary => Games is not null;
    public bool IsBaseLayerInteractive => ModalHost.IsBaseLayerInteractive && Games?.IsSwitching != true;
    public bool IsAnyUninstallExecuting => Games?.IsUninstallExecuting ?? Operations.IsUninstallExecuting;
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
    private readonly ShellViewModel defaultShell;
    private readonly RemoteContentViewModel defaultRemoteContent;
    private readonly GameOperationsViewModel defaultOperations;

    public MainWindowViewModel(
        ShellPresentationFamily family,
        ShellLifecycle runtime,
        PresentationOptions? options = null,
        GameLibraryViewModel? games = null)
    {
        this.runtime = runtime;
        showHiddenSettings = options?.ShowHiddenSettings ?? false;
        Games = games;
        defaultShell = family.Shell;
        Background = family.Background;
        defaultRemoteContent = family.RemoteContent;
        Dialogs = family.Dialogs;
        defaultOperations = family.Operations;
        Toasts = family.Toasts;
        WindowChrome = family.WindowChrome;
        Settings = family.Settings;
        ResourcePanel = family.ResourcePanel;
        LogViewer = family.LogViewer;
        LogExport = family.LogExport;
        Debug = family.Debug;
        ModalHost = family.ModalHost;

        if (Games is not null)
        {
            Games.ActiveChanged += OnActiveGameChanged;
            Games.PropertyChanged += OnGameLibraryPropertyChanged;
        }
        ModalHost.PropertyChanged += OnModalHostPropertyChanged;
        runtime.PresentationChanged += OnRuntimePresentationChanged;
        runtime.StatusDetailModeChanged += OnStatusDetailModeChanged;
        OnRuntimePresentationChanged();
    }

    internal MainWindowViewModel(
        ILauncherStatusSnapshotService launcherStatusSnapshotService,
        ILauncherSettingsService settingsService,
        ISavedSettingsWriter savedSettingsWriter,
        LocalizationService localizer,
        INotificationService toastService,
        ILauncherUpdateService launcherUpdateService,
        ILauncherUpdatePresenter updatePresenter,
        ILauncherSelfUpdateService launcherSelfUpdateService,
        IWindowsLauncherUpdateApplier launcherUpdateApplier,
        ILauncherDiagnostics diagnostics,
        ShellPresentationFamily family,
        IErrorHandlingService errorHandling,
        ISystemAnimationSettingsProvider systemAnimationSettingsProvider,
        IFilePickerService filePickerService,
        LauncherBuildIdentity? buildIdentity = null,
        PresentationOptions? options = null,
        GameLibraryViewModel? games = null)
        : this(
            family,
            new ShellLifecycle(
                launcherStatusSnapshotService,
                settingsService,
                savedSettingsWriter,
                localizer,
                toastService,
                launcherUpdateService,
                updatePresenter,
                launcherSelfUpdateService,
                launcherUpdateApplier,
                diagnostics,
                errorHandling,
                systemAnimationSettingsProvider,
                family,
                filePickerService,
                ownsPresentationCollaborators: true,
                buildIdentity: buildIdentity,
                games: games),
            options,
            games)
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

    /// <summary>执行固定游戏身份的外部启动请求；不回退当前显示条目。</summary>
    public Task RequestGameLaunchAsync(string gameId) => runtime.RequestGameLaunchAsync(gameId);

    public void RefreshSystemMotionPreference() => runtime.RefreshSystemMotionPreference();

    [RelayCommand]
    private Task RefreshAsync(CancellationToken cancellationToken = default) =>
        runtime.RefreshAsync(cancellationToken);

    [RelayCommand]
    private async Task ManageGameFromSettingsAsync(string action)
    {
        if (Settings.IsSaving)
        {
            return;
        }

        var target = Games?.Get(Settings.EditingGameId).Operations ?? Operations;
        await WindowChrome.RunAfterSettingsAsync(async () =>
        {
            switch (action)
            {
                case "identify": await Settings.SelectInstalledGameCommand.ExecuteAsync(null); break;
                case "update": await target.CheckForGameUpdateCommand.ExecuteAsync(null); break;
                case "shortcut": await target.CreateGameShortcutCommand.ExecuteAsync(null); break;
                case "repair": await target.RequestRepairCommand.ExecuteAsync(null); break;
                case "uninstall": await target.RequestUninstallCommand.ExecuteAsync(null); break;
                case "folder": target.OpenGameFolderCommand.Execute(null); break;
                case "resources": await ResourcePanel.OpenResourcePanelCommand.ExecuteAsync(null); break;
            }
        });
    }

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
        if (Games is not null)
        {
            Games.ActiveChanged -= OnActiveGameChanged;
            Games.PropertyChanged -= OnGameLibraryPropertyChanged;
        }
        ModalHost.PropertyChanged -= OnModalHostPropertyChanged;
        runtime.Dispose();
    }

    private void OnGameLibraryPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(GameLibraryViewModel.IsSwitching))
        {
            OnPropertyChanged(nameof(IsBaseLayerInteractive));
        }
    }

    private void OnModalHostPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ModalHostViewModel.IsBaseLayerInteractive))
        {
            OnPropertyChanged(nameof(IsBaseLayerInteractive));
        }
    }

    private void OnActiveGameChanged()
    {
        OnPropertyChanged(nameof(Shell));
        OnPropertyChanged(nameof(RemoteContent));
        OnPropertyChanged(nameof(Operations));
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
