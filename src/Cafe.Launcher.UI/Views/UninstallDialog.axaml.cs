using System;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cafe.Launcher.UI.Features.GameOperations;
using Cafe.Launcher.UI.ViewModels;

namespace Cafe.Launcher.UI.Views;

/// <summary>卸载表面只负责阶段焦点；退出权限和 Escape 均由特性命令判定。</summary>
internal partial class UninstallDialog : UserControl
{
    private UninstallDialogViewModel? presentation;

    public UninstallDialog()
    {
        InitializeComponent();
        DataContextChanged += OnContextChanged;
        DetachedFromVisualTree += (_, _) => Subscribe(null);
        AttachedToVisualTree += (_, _) => OnContextChanged(this, EventArgs.Empty);
    }

    private void OnContextChanged(object? sender, EventArgs e) =>
        Subscribe((DataContext as MainWindowViewModel)?.Operations.Uninstall);

    private void Subscribe(UninstallDialogViewModel? next)
    {
        if (ReferenceEquals(presentation, next))
        {
            return;
        }
        if (presentation is not null)
        {
            presentation.PropertyChanged -= OnPresentationChanged;
        }
        presentation = next;
        if (presentation is not null)
        {
            presentation.PropertyChanged += OnPresentationChanged;
        }
    }

    private void OnPresentationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (presentation?.IsVisible == true && e.PropertyName is nameof(UninstallDialogViewModel.IsVisible) or nameof(UninstallDialogViewModel.State))
        {
            Dispatcher.UIThread.Post(FocusPhase, DispatcherPriority.Background);
        }
        else if (e.PropertyName == nameof(UninstallDialogViewModel.IsVisible) && presentation?.IsVisible == false)
        {
            Dispatcher.UIThread.Post(RestoreOperationFocus, DispatcherPriority.Background);
        }
    }

    private void FocusPhase()
    {
        if (presentation?.IsVisible != true)
        {
            return;
        }
        Control target = presentation.IsConfirmation ? UninstallCancelButton
            : presentation.IsResult ? UninstallDoneButton : UninstallSurface;
        target.Focus(NavigationMethod.Unspecified);
    }

    private void RestoreOperationFocus()
    {
        if (DataContext is not MainWindowViewModel vm || !vm.ModalHost.IsBaseLayerInteractive)
        {
            return;
        }
        var window = TopLevel.GetTopLevel(this);
        if (window?.FocusManager?.GetFocusedElement() is Control { IsEffectivelyVisible: true, IsEnabled: true })
        {
            return;
        }
        window?.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(button => button.Classes.Contains("primary-operation") && button.IsEffectivelyVisible && button.IsEnabled)
            ?.Focus(NavigationMethod.Unspecified);
    }
}
