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
        KeyDown += OnKeyDown;
    }

    public void ConfigureViewModel(MainWindowViewModel viewModel)
    {
        viewModel.Settings.PickGameFolderAsync = PickGameFolderAsync;
        viewModel.Settings.PickBackgroundImageAsync = PickBackgroundImageAsync;
        viewModel.Settings.PickBackgroundFolderAsync = PickBackgroundFolderAsync;
        viewModel.Background.PickBackgroundImageAsync = PickBackgroundImageAsync;
        viewModel.Background.PickBackgroundFolderAsync = PickBackgroundFolderAsync;
        viewModel.Operations.MinimizeWindow = () => WindowState = WindowState.Minimized;
        viewModel.WindowChrome.MinimizeWindow = () => WindowState = WindowState.Minimized;
        viewModel.WindowChrome.CloseWindow = PerformClose;
        viewModel.WindowChrome.RestoreWindow = ShowWindow;
        viewModel.MigrationWizard.PickGameFolderAsync =
            () => PickGameFolderAsync(viewModel.MigrationWizard.Editor.Current.GamePath);
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

        var pickerTitle = (DataContext as MainWindowViewModel)?.Shell.GameFolderPickerTitle ?? "Choose install folder";
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

        var imagePickerTitle = (DataContext as MainWindowViewModel)?.Background.BackgroundImagePickerTitle ?? "Choose Background Image";
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

        var folderPickerTitle = (DataContext as MainWindowViewModel)?.Background.BackgroundFolderPickerTitle ?? "Choose Background Folder";
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = folderPickerTitle,
            AllowMultiple = false
        });

        return folders.FirstOrDefault()?.TryGetLocalPath();
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

        // Close dialogs in priority order — most-nested first.
        // Migration wizard (first-launch)
        if (vm.MigrationWizard.IsVisible)
        {
            vm.MigrationWizard.SkipMigrationCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // Confirmation dialogs
        if (vm.Dialogs.IsDownloadRunningCloseConfirmVisible)
        {
            vm.Dialogs.CancelCloseWhileDownloadingCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (vm.Dialogs.IsStopConfirmVisible)
        {
            vm.Dialogs.CancelStopCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (vm.Settings.IsUnsavedChangesVisible)
        {
            vm.WindowChrome.KeepEditingSettingsCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (vm.Dialogs.IsRepairConfirmVisible)
        {
            vm.Dialogs.CancelRepairCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (vm.Dialogs.IsResourcePanelSourceConfirmVisible)
        {
            vm.Dialogs.CancelResourcePanelSourceSwitchCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (vm.Dialogs.IsUninstallConfirmVisible)
        {
            vm.Dialogs.CancelUninstallCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (vm.Dialogs.IsNoticeDialogVisible)
        {
            vm.Dialogs.DismissNoticeCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // Overlay panels — only close if not dirty (avoids accidental data loss)
        if (vm.WindowChrome.IsSettingsVisible && !vm.Settings.IsSettingsDirty)
        {
            vm.WindowChrome.ShowSettingsCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (vm.ResourcePanel.IsResourcePanelVisible)
        {
            vm.ResourcePanel.CloseResourcePanelCommand.Execute(null);
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

    private void PerformClose()
    {
        if (DataContext is MainWindowViewModel vm
            && vm.Settings.Editor.Current.CloseBehavior == Models.CloseBehaviors.Minimize)
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
