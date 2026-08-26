using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.ViewModels;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Cafe.Launcher.Avalonia.Views;

public partial class MainWindow : Window
{
    private SystemTrayService? systemTray;
    private MainWindowViewModel? configuredViewModel;
    private readonly Func<string, Task<string?>> pickGameFolderAsync;
    private readonly Func<Task<string?>> pickBackgroundImageAsync;
    private readonly Func<Task<string?>> pickBackgroundFolderAsync;
    private readonly Func<string, Task<string?>> pickLogExportDirectoryAsync;
    private readonly Action<string> openDirectory;

    public MainWindow()
    {
        InitializeComponent();
        pickGameFolderAsync = PickGameFolderAsync;
        pickBackgroundImageAsync = PickBackgroundImageAsync;
        pickBackgroundFolderAsync = PickBackgroundFolderAsync;
        pickLogExportDirectoryAsync = PickLogExportDirectoryAsync;
        openDirectory = OpenDirectory;
        PointerPressed += OnPointerPressed;
        KeyDown += OnKeyDown;
        Activated += OnActivated;
    }

    public void ConfigureViewModel(MainWindowViewModel viewModel)
    {
        UnconfigureViewModel();
        configuredViewModel = viewModel;
        viewModel.Settings.PickGameFolderAsync = pickGameFolderAsync;
        viewModel.Settings.PickBackgroundImageAsync = pickBackgroundImageAsync;
        viewModel.Settings.PickBackgroundFolderAsync = pickBackgroundFolderAsync;
        viewModel.Background.PickBackgroundImageAsync = pickBackgroundImageAsync;
        viewModel.Background.PickBackgroundFolderAsync = pickBackgroundFolderAsync;
        viewModel.LogViewer.PickExportDirectoryAsync = pickLogExportDirectoryAsync;
        viewModel.LogViewer.OpenExportDirectory = openDirectory;
        viewModel.Debug.PickExportDirectoryAsync = pickLogExportDirectoryAsync;
        viewModel.Debug.OpenDirectory = openDirectory;
        viewModel.Operations.MinimizeRequested += MinimizeWindow;
        viewModel.WindowChrome.MinimizeRequested += MinimizeWindow;
        viewModel.WindowChrome.CloseRequested += PerformClose;
        viewModel.WindowChrome.RestoreRequested += ShowWindow;
        viewModel.Dialogs.ErrorCopyDetailsRequested += CopyErrorDetailsToClipboard;
    }

    protected override void OnClosed(EventArgs e)
    {
        UnconfigureViewModel();
        base.OnClosed(e);
    }

    private void UnconfigureViewModel()
    {
        if (configuredViewModel is not { } viewModel)
        {
            return;
        }

        viewModel.Operations.MinimizeRequested -= MinimizeWindow;
        viewModel.WindowChrome.MinimizeRequested -= MinimizeWindow;
        viewModel.WindowChrome.CloseRequested -= PerformClose;
        viewModel.WindowChrome.RestoreRequested -= ShowWindow;
        viewModel.Dialogs.ErrorCopyDetailsRequested -= CopyErrorDetailsToClipboard;
        viewModel.RemoteContent.SetBannerPointerOver(false);
        viewModel.RemoteContent.SetBannerFocusWithin(false);

        if (viewModel.Settings.PickGameFolderAsync == pickGameFolderAsync)
        {
            viewModel.Settings.PickGameFolderAsync = null;
        }

        if (viewModel.Settings.PickBackgroundImageAsync == pickBackgroundImageAsync)
        {
            viewModel.Settings.PickBackgroundImageAsync = null;
        }

        if (viewModel.Settings.PickBackgroundFolderAsync == pickBackgroundFolderAsync)
        {
            viewModel.Settings.PickBackgroundFolderAsync = null;
        }

        if (viewModel.Background.PickBackgroundImageAsync == pickBackgroundImageAsync)
        {
            viewModel.Background.PickBackgroundImageAsync = null;
        }

        if (viewModel.Background.PickBackgroundFolderAsync == pickBackgroundFolderAsync)
        {
            viewModel.Background.PickBackgroundFolderAsync = null;
        }

        if (viewModel.LogViewer.PickExportDirectoryAsync == pickLogExportDirectoryAsync)
        {
            viewModel.LogViewer.PickExportDirectoryAsync = null;
        }

        if (viewModel.LogViewer.OpenExportDirectory == openDirectory)
        {
            viewModel.LogViewer.OpenExportDirectory = null;
        }

        if (viewModel.Debug.PickExportDirectoryAsync == pickLogExportDirectoryAsync)
        {
            viewModel.Debug.PickExportDirectoryAsync = null;
        }

        if (viewModel.Debug.OpenDirectory == openDirectory)
        {
            viewModel.Debug.OpenDirectory = null;
        }

        configuredViewModel = null;
    }

    private void OnBannerPointerEntered(object? sender, PointerEventArgs e) =>
        configuredViewModel?.RemoteContent.SetBannerPointerOver(true);

    private void OnBannerPointerExited(object? sender, PointerEventArgs e) =>
        configuredViewModel?.RemoteContent.SetBannerPointerOver(false, hideControls: true);

    private void OnBannerGotFocus(object? sender, FocusChangedEventArgs e) =>
        configuredViewModel?.RemoteContent.SetBannerFocusWithin(true);

    private void OnBannerLostFocus(object? sender, FocusChangedEventArgs e) =>
        configuredViewModel?.RemoteContent.SetBannerFocusWithin(false);

    private void OnActivated(object? sender, EventArgs e)
    {
        configuredViewModel?.RefreshSystemMotionPreference();
    }

    private void MinimizeWindow() => WindowState = WindowState.Minimized;

    public void SetSystemTray(SystemTrayService trayService)
    {
        systemTray = trayService;
    }

    private async Task<string?> PickGameFolderAsync(string currentPath)
    {
        if (!StorageProvider.CanPickFolder)
        {
            return null;
        }

        var startLocation = string.IsNullOrWhiteSpace(currentPath)
            ? null
            : await StorageProvider.TryGetFolderFromPathAsync(currentPath);

        var pickerTitle = (DataContext as MainWindowViewModel)?.Shell.GameFolderPickerTitle ?? "";
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = pickerTitle,
            AllowMultiple = false,
            SuggestedStartLocation = startLocation
        });

        return folders.FirstOrDefault()?.TryGetLocalPath();
    }

    private async Task<string?> PickBackgroundImageAsync()
    {
        if (!StorageProvider.CanOpen)
        {
            return null;
        }

        var imagePickerTitle = (DataContext as MainWindowViewModel)?.Background.BackgroundImagePickerTitle ?? "";
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = imagePickerTitle,
            AllowMultiple = false,
            FileTypeFilter = new List<FilePickerFileType>
            {
                new("Images")
                {
                    Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.webp" },
                }
            }
        });

        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    private async Task<string?> PickBackgroundFolderAsync()
    {
        if (!StorageProvider.CanPickFolder)
        {
            return null;
        }

        var folderPickerTitle = (DataContext as MainWindowViewModel)?.Background.BackgroundFolderPickerTitle ?? "";
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = folderPickerTitle,
            AllowMultiple = false
        });

        return folders.FirstOrDefault()?.TryGetLocalPath();
    }

    private async Task<string?> PickLogExportDirectoryAsync(string defaultPath)
    {
        Directory.CreateDirectory(defaultPath);
        if (!StorageProvider.CanPickFolder)
        {
            return defaultPath;
        }

        var startLocation = await StorageProvider.TryGetFolderFromPathAsync(defaultPath);
        var pickerTitle = (DataContext as MainWindowViewModel)?.Shell.I18n["logExportFolderPickerTitle"] ?? "";
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = pickerTitle,
            AllowMultiple = false,
            SuggestedStartLocation = startLocation
        });

        return folders.FirstOrDefault()?.TryGetLocalPath();
    }

    private static void OpenDirectory(string path)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsWithinTitleBar(e.Source as Control)
            || IsInteractive(e.Source as Control))
        {
            return;
        }

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private bool IsWithinTitleBar(Control? control)
    {
        while (control is not null)
        {
            if (ReferenceEquals(control, TitleBar))
            {
                return true;
            }

            control = control.Parent as Control;
        }

        return false;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        if (vm.TryHandleEscape())
        {
            e.Handled = true;
        }
    }

    private static bool IsInteractive(Control? control)
    {
        while (control is not null)
        {
            // Controls that can receive keyboard focus are interactive even when
            // their concrete type is a composite control (for example ColorPicker
            // or ToggleSwitch). Keep ScrollViewer as an explicit exception because
            // it is a pointer-interactive surface but is not normally focusable.
            if (control.Focusable || control is ScrollViewer)
            {
                return true;
            }

            control = control.Parent as Control;
        }

        return false;
    }

    private void PerformClose()
    {
        if (DataContext is MainWindowViewModel vm
            && vm.Settings.Editor.GetSavedSnapshot().CloseBehavior == Models.CloseBehaviors.Minimize)
        {
            if (systemTray is not null)
            {
                systemTray.HideWindow();
            }
            else
            {
                // No tray available — minimize to taskbar instead of calling Hide(),
                // which would make the window unrecoverable without a tray icon.
                WindowState = WindowState.Minimized;
            }

            return;
        }

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.TryShutdown();
            return;
        }

        Close();
    }

    public void ShowWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private async void CopyErrorDetailsToClipboard(string details)
    {
        if (Clipboard is not null)
        {
            try
            {
                await Clipboard.SetTextAsync(details);
            }
            catch (Exception ex)
            {
                LocalDiagnostics.LogSync(
                    LogEntrySeverity.Warn,
                    "ClipboardCopyFailed",
                    $"Failed to copy error details to clipboard: {ex.Message}");
            }
        }
    }
}
