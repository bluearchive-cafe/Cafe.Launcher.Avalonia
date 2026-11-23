using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Cafe.Launcher.UI.Features.Settings;

namespace Cafe.Launcher.UI.Views;

internal partial class MainWindow
{
    private IInputElement? settingsPreviewFocus;

    private void OnSettingsPreviewChanging(object? sender, PropertyChangingEventArgs args)
    {
        if (args.PropertyName == nameof(SettingsViewModel.IsPreviewing)
            && configuredViewModel?.Settings.IsPreviewing == false)
        {
            settingsPreviewFocus = FocusManager?.GetFocusedElement();
        }
    }

    private void OnSettingsPreviewChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(SettingsViewModel.IsPreviewing)) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (configuredViewModel?.Settings.IsPreviewing == true)
            {
                ReturnToSettingsButton.Focus(NavigationMethod.Unspecified);
            }
            else
            {
                if (configuredViewModel?.WindowChrome.IsSettingsVisible == true
                    && configuredViewModel.ModalHost.IsSettingsInteractive
                    && settingsPreviewFocus is Control { IsEffectivelyVisible: true, IsEffectivelyEnabled: true } control)
                {
                    control.Focus(NavigationMethod.Unspecified);
                }
                settingsPreviewFocus = null;
            }
        }, DispatcherPriority.Input);
    }
}
