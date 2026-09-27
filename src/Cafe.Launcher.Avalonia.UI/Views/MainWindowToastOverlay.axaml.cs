using Avalonia.Controls;
using Avalonia.Input;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.ViewModels;

namespace Cafe.Launcher.Avalonia.Views;

public partial class MainWindowToastOverlay : UserControl
{
    public MainWindowToastOverlay()
    {
        InitializeComponent();
    }

    // 悬停即暂停消失计时：判定只看卡片自己，父层不必知道哪条 Toast 被指向。
    private void OnToastCardPointerEntered(object? sender, PointerEventArgs e) =>
        SetToastPointerOver(sender, isPointerOver: true);

    private void OnToastCardPointerExited(object? sender, PointerEventArgs e) =>
        SetToastPointerOver(sender, isPointerOver: false);

    private void SetToastPointerOver(object? sender, bool isPointerOver)
    {
        if (sender is Control { DataContext: ToastNotification toast }
            && DataContext is MainWindowViewModel viewModel)
        {
            viewModel.Toasts.SetToastPointerOver(toast.Id, isPointerOver);
        }
    }
}
