using System;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Features.Shell;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cafe.Launcher.Avalonia.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IDisposable, IShellLifecyclePresentation
{
    private readonly ShellLifecycle lifecycle;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMotionEnabled))]
    private bool isMotionReduced = true;

    [ObservableProperty]
    private bool isBusy;

    public bool IsMotionEnabled => !IsMotionReduced;

    public bool IsBottomPanelVisible => true;

    public bool IsStatusDetailExpanded =>
        Settings.Editor.Current.StatusDetailMode == StatusDetailModes.Detailed;

    public bool IsStatusDetailHidden =>
        Settings.Editor.Current.StatusDetailMode == StatusDetailModes.Hidden;

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

    bool IShellLifecyclePresentation.IsBusy
    {
        get => IsBusy;
        set => IsBusy = value;
    }

    bool IShellLifecyclePresentation.IsMotionReduced
    {
        get => IsMotionReduced;
        set => IsMotionReduced = value;
    }

    public MainWindowViewModel(
        ILauncherCoreService launcherCoreService,
        LauncherSettingsService settingsService,
        LocalizationService localizer,
        ToastService toastService,
        LauncherUpdateService launcherUpdateService,
        LocalDiagnostics diagnostics,
        UnifiedLogger unifiedLogger,
        ShellViewModel shell,
        BackgroundViewModel background,
        RemoteContentViewModel remoteContent,
        DialogsViewModel dialogs,
        GameOperationsViewModel operations,
        ToastHostViewModel toasts,
        WindowChromeViewModel windowChrome,
        SettingsViewModel settingsViewModel,
        ResourcePanelViewModel resourcePanelViewModel,
        IErrorHandlingService errorHandling,
        LogViewerDialogViewModel? logViewer = null,
        DebugViewModel? debug = null,
        ModalHostViewModel? modalHost = null,
        WindowsAnimationSettingsProvider? windowsAnimationSettingsProvider = null)
    {
        this.localizer = localizer;
        Shell = shell;
        Background = background;
        RemoteContent = remoteContent;
        Dialogs = dialogs;
        Operations = operations;
        Toasts = toasts;
        WindowChrome = windowChrome;
        Settings = settingsViewModel;
        ResourcePanel = resourcePanelViewModel;
        LogViewer = logViewer ?? new LogViewerDialogViewModel(
            unifiedLogger, null, null, null, null, null);
        Debug = debug ?? new DebugViewModel(
            toastService, unifiedLogger, errorHandling,
            settingsService, Operations, Shell);
        ModalHost = modalHost ?? new ModalHostViewModel();

        var animationProvider = windowsAnimationSettingsProvider ?? new WindowsAnimationSettingsProvider();
        lifecycle = new ShellLifecycle(
            launcherCoreService,
            settingsService,
            localizer,
            toastService,
            launcherUpdateService,
            diagnostics,
            errorHandling,
            animationProvider,
            this,
            Shell,
            Background,
            RemoteContent,
            Dialogs,
            Operations,
            Toasts,
            WindowChrome,
            Settings,
            ResourcePanel,
            LogViewer,
            Debug,
            ModalHost);

        lifecycle.StatusDetailModeChanged += () =>
        {
            OnPropertyChanged(nameof(IsStatusDetailExpanded));
            OnPropertyChanged(nameof(IsStatusDetailHidden));
        };
        lifecycle.Wire();
        ApplyLanguage(LauncherLanguages.Auto);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default) =>
        await lifecycle.InitializeAsync(cancellationToken);

    public void RefreshSystemMotionPreference() =>
        lifecycle.RefreshSystemMotionPreference();

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken = default) =>
        await lifecycle.RefreshAsync(cancellationToken);

    public bool TryHandleEscape() => lifecycle.TryHandleEscape();

    internal async Task HandleOperationsRefreshRequestedAsync(GameOperationsRefreshMode mode) =>
        await lifecycle.HandleOperationsRefreshRequestedAsync(mode);

    public void Dispose()
    {
        lifecycle.Dispose();
    }

    private void ApplyLanguage(string language)
    {
        Shell.ApplyLanguage(language, Settings, ResourcePanel, false);
        Background.BackgroundImagePickerTitle = localizer.T("chooseBackgroundImageTitle");
        Background.BackgroundFolderPickerTitle = localizer.T("chooseBackgroundFolderTitle");
        RemoteContent.ApplyLanguage();
        Dialogs.ApplyLanguage();
        Operations.ApplyLanguage();
        Debug.ApplyLanguage();
    }

    private readonly LocalizationService localizer;
}