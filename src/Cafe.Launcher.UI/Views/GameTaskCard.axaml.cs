using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Cafe.Launcher.UI.ViewModels;

namespace Cafe.Launcher.UI.Views;

/// <summary>Non-modal task surface; window visibility and the modal owner determine result notification delivery.</summary>
internal partial class GameTaskCard : UserControl
{
    private Window? window;
    private MainWindowViewModel? owner;

    public GameTaskCard() => InitializeComponent();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != DataContextProperty || window is null) return;
        if (owner is not null) owner.ModalHost.PropertyChanged -= OnModalChanged;
        owner = DataContext as MainWindowViewModel;
        if (owner is not null) owner.ModalHost.PropertyChanged += OnModalChanged;
        UpdateVisibility();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        window = TopLevel.GetTopLevel(this) as Window;
        owner = DataContext as MainWindowViewModel;
        if (window is not null) window.PropertyChanged += OnWindowChanged;
        if (owner is not null) owner.ModalHost.PropertyChanged += OnModalChanged;
        UpdateVisibility();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (window is not null) window.PropertyChanged -= OnWindowChanged;
        if (owner is not null) owner.ModalHost.PropertyChanged -= OnModalChanged;
        window = null;
        owner = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnWindowChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.IsActiveProperty || e.Property == IsVisibleProperty || e.Property == Window.WindowStateProperty) UpdateVisibility();
    }

    private void OnModalChanged(object? sender, PropertyChangedEventArgs e) => UpdateVisibility();

    private void UpdateVisibility() => owner?.Operations.SetTaskSurfaceVisibility(
        window is { IsVisible: true, IsActive: true } && window.WindowState != WindowState.Minimized,
        owner.ModalHost.IsBaseLayerInteractive);
}
