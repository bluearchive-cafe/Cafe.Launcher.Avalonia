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
using System.Linq;
using System.Threading.Tasks;

namespace Cafe.Launcher.Avalonia.Views;

public partial class MainWindow : Window
{
    private SystemTrayService? systemTray;

    public MainWindow()
    {
        InitializeComponent();
        PointerPressed += OnPointerPressed;
    }

    public void ConfigureViewModel(MainWindowViewModel viewModel)
    {
        viewModel.PickGameFolderAsync = PickGameFolderAsync;
        viewModel.PickBackgroundImageAsync = PickBackgroundImageAsync;
        viewModel.MinimizeWindow = () => WindowState = WindowState.Minimized;
        viewModel.CloseWindow = PerformClose;
        viewModel.RestoreWindow = ShowWindow;
    }

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

        var pickerTitle = (DataContext as MainWindowViewModel)?.GameFolderPickerTitle ?? "Choose install folder";
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

        var imagePickerTitle = (DataContext as MainWindowViewModel)?.BackgroundImagePickerTitle ?? "Choose Background Image";
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

    private void PerformClose()
    {
        if (DataContext is MainWindowViewModel vm
            && vm.SelectedCloseBehavior == Models.CloseBehaviors.Minimize)
        {
            if (systemTray is not null)
            {
                systemTray.HideWindow();
            }
            else
            {
                WindowState = WindowState.Minimized;
                Hide();
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
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }
}
