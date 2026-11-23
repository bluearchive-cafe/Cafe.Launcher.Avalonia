using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.ViewModels;

namespace Cafe.Launcher.UI.Views;

internal partial class MainWindowToastOverlay : UserControl
{
    public MainWindowToastOverlay()
    {
        InitializeComponent();
    }

    private void OnToastCardPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if ((e.Property == InputElement.IsPointerOverProperty || e.Property == InputElement.IsKeyboardFocusWithinProperty)
            && sender is Control { DataContext: ToastNotification toast } card
            && DataContext is MainWindowViewModel viewModel)
        {
            viewModel.Toasts.SetToastInteractionActive(toast.Id, card.IsPointerOver || card.IsKeyboardFocusWithin);
        }
    }
}
