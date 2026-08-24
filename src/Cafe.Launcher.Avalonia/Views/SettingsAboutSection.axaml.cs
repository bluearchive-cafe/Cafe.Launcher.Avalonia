using Avalonia.Controls;
using Avalonia.Input;
using Cafe.Launcher.Avalonia.ViewModels;

namespace Cafe.Launcher.Avalonia.Views;

public partial class SettingsAboutSection : UserControl
{
    public SettingsAboutSection()
    {
        InitializeComponent();
    }

    private void LauncherVersionChip_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.Shell.RegisterLauncherVersionClick();
        }
    }
}
