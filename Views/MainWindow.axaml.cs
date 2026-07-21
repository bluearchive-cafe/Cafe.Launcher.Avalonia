using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
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

    public MainWindow()
    {
        InitializeComponent();
        PointerPressed += OnPointerPressed;
        KeyDown += OnKeyDown;
        Activated += OnActivated;
    }

    public void ConfigureViewModel(MainWindowViewModel viewModel)
    {
        UnconfigureViewModel();
        configuredViewModel = viewModel;
        viewModel.Settings.PickGameFolderAsync = PickGameFolderAsync;
        viewModel.Settings.PickBackgroundImageAsync = PickBackgroundImageAsync;
        viewModel.Settings.PickBackgroundFolderAsync = PickBackgroundFolderAsync;
        viewModel.Settings.PickBackgroundVideoAsync = PickBackgroundVideoAsync;
        viewModel.Background.PickBackgroundImageAsync = PickBackgroundImageAsync;
        viewModel.Background.PickBackgroundFolderAsync = PickBackgroundFolderAsync;
        viewModel.LogViewer.PickExportDirectoryAsync = PickLogExportDirectoryAsync;
        viewModel.LogViewer.OpenExportDirectory = OpenDirectory;
        viewModel.Operations.MinimizeRequested += MinimizeWindow;
        viewModel.WindowChrome.MinimizeRequested += MinimizeWindow;
        viewModel.WindowChrome.CloseRequested += PerformClose;
        viewModel.WindowChrome.RestoreRequested += ShowWindow;
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
        configuredViewModel = null;
    }

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

    private async Task<string?> PickBackgroundVideoAsync()
    {
        if (!StorageProvider.CanOpen)
        {
            return null;
        }

        var videoPickerTitle = (DataContext as MainWindowViewModel)?.Background.BackgroundVideoPickerTitle ?? "";
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = videoPickerTitle,
            AllowMultiple = false,
            FileTypeFilter = new List<FilePickerFileType>
            {
                new("Video")
                {
                    Patterns = new[] { "*.mp4", "*.webm", "*.mkv", "*.mov" },
                }
            }
        });

        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    private async Task<string?> PickLogExportDirectoryAsync(string defaultPath)
    {
        Directory.CreateDirectory(defaultPath);
        if (!StorageProvider.CanPickFolder)
        {
            return defaultPath;
        }

        var startLocation = await StorageProvider.TryGetFolderFromPathAsync(defaultPath);
        var pickerTitle = (DataContext as MainWindowViewModel)?.Shell.I18n.LogExportFolderPickerTitle ?? "";
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
        if (IsInteractive(e.Source as Control))
        {
            return;
        }

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
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
            if (control is Button or TextBox or ComboBox or ScrollViewer)
            {
                return true;
            }

            control = control.Parent as Control;
        }

        return false;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        // Pause the video wallpaper whenever the window is not actually visible to the user: minimized
        // to the taskbar (WindowState) or hidden to the tray (IsVisible, set by Window.Hide()/Show()).
        // Driving this off the resulting state — rather than each individual hide/restore call site —
        // covers every path, including SystemTrayService.ShowWindow which bypasses MainWindow.ShowWindow.
        if ((change.Property == WindowStateProperty || change.Property == IsVisibleProperty)
            && DataContext is MainWindowViewModel vm)
        {
            var active = IsVisible && WindowState != WindowState.Minimized;
            vm.Background.SetPlaybackActive(active);
        }
    }

    private void PerformClose()
    {
        if (DataContext is MainWindowViewModel vm
            && vm.Settings.Editor.GetSavedSnapshot().CloseBehavior == Models.CloseBehaviors.Minimize)
        {
            if (systemTray is not null)
            {
                // Hide() flips IsVisible, which OnPropertyChanged turns into a playback pause.
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
            desktop.Shutdown();
            return;
        }

        Close();
    }

    public void ShowWindow()
    {
        // Show()/WindowState changes flip IsVisible/WindowState, which OnPropertyChanged turns into a
        // playback resume — so this covers SystemTrayService.ShowWindow's path too.
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }
}
