using System;
using System.Runtime.CompilerServices;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Cafe.Launcher.Avalonia.Views;

public static class OverlayFocusBehavior
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<OverlayFocusBehaviorOwner, Control, bool>("IsEnabled");

    private static readonly ConditionalWeakTable<Control, FocusState> States = new();

    static OverlayFocusBehavior()
    {
        IsEnabledProperty.Changed.AddClassHandler<Control>(OnIsEnabledChanged);
    }

    public static bool GetIsEnabled(Control control) =>
        control.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(Control control, bool value) =>
        control.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(Control control, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.GetNewValue<bool>())
        {
            States.GetValue(control, static item => new FocusState(item)).Enable();
        }
        else if (States.TryGetValue(control, out var state))
        {
            state.Disable();
            States.Remove(control);
        }
    }

    private sealed class FocusState
    {
        private readonly Control overlay;
        private IInputElement? previousFocus;
        private bool enabled;

        public FocusState(Control overlay)
        {
            this.overlay = overlay;
        }

        public void Enable()
        {
            if (enabled)
            {
                return;
            }

            enabled = true;
            overlay.AttachedToVisualTree += OnAttachedToVisualTree;
            overlay.DetachedFromVisualTree += OnDetachedFromVisualTree;
            overlay.PropertyChanged += OnOverlayPropertyChanged;
            if (overlay.IsAttachedToVisualTree())
            {
                HandleVisibilityChanged(overlay.IsEffectivelyVisible);
            }
        }

        public void Disable()
        {
            if (!enabled)
            {
                return;
            }

            enabled = false;
            overlay.AttachedToVisualTree -= OnAttachedToVisualTree;
            overlay.DetachedFromVisualTree -= OnDetachedFromVisualTree;
            overlay.PropertyChanged -= OnOverlayPropertyChanged;
            RestorePreviousFocus();
        }

        private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs args)
        {
            HandleVisibilityChanged(overlay.IsEffectivelyVisible);
        }

        private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs args)
        {
            RestorePreviousFocus();
        }

        private void OnOverlayPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (args.Property == Visual.IsVisibleProperty && overlay.IsAttachedToVisualTree())
            {
                if (!args.GetNewValue<bool>())
                {
                    HandleVisibilityChanged(false);
                    return;
                }

                Dispatcher.UIThread.Post(
                    () => HandleVisibilityChanged(overlay.IsEffectivelyVisible),
                    DispatcherPriority.Input);
            }
        }

        private void HandleVisibilityChanged(bool isVisible)
        {
            var topLevel = TopLevel.GetTopLevel(overlay);
            if (topLevel?.FocusManager is not { } focusManager)
            {
                return;
            }

            if (!isVisible)
            {
                RestorePreviousFocus();
                return;
            }

            previousFocus = focusManager.GetFocusedElement();
            Dispatcher.UIThread.Post(
                () =>
                {
                    if (!overlay.IsEffectivelyVisible
                        || TopLevel.GetTopLevel(overlay)?.FocusManager is null)
                    {
                        return;
                    }

                    overlay
                        .GetVisualDescendants()
                        .OfType<Control>()
                        .FirstOrDefault(control =>
                            control.Focusable
                            && control.IsEffectivelyVisible
                            && control.IsEnabled
                            && control.IsTabStop)
                        ?.Focus(NavigationMethod.Tab);
                },
                DispatcherPriority.Input);
        }

        private void RestorePreviousFocus()
        {
            var focus = previousFocus;
            previousFocus = null;
            focus?.Focus(NavigationMethod.Tab);
        }
    }

    private sealed class OverlayFocusBehaviorOwner;
}
