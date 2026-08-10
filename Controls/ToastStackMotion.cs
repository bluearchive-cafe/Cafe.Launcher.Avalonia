using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Cafe.Launcher.Avalonia.Helpers;

namespace Cafe.Launcher.Avalonia.Controls;

public static class ToastStackMotion
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<ToastStackMotionOwner, Panel, bool>("IsEnabled");

    private static readonly ConditionalWeakTable<Panel, ToastStackState> States = new();

    static ToastStackMotion()
    {
        IsEnabledProperty.Changed.AddClassHandler<Panel>(OnIsEnabledChanged);
    }

    public static bool GetIsEnabled(Panel panel) =>
        panel.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(Panel panel, bool value) =>
        panel.SetValue(IsEnabledProperty, value);

    internal static double CalculateInitialOffset(double previousY, double currentY) =>
        previousY - currentY;

    private static void OnIsEnabledChanged(Panel panel, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.GetNewValue<bool>())
        {
            States.GetValue(panel, static item => new ToastStackState(item)).Enable();
        }
        else if (States.TryGetValue(panel, out var state))
        {
            state.Disable();
            States.Remove(panel);
        }
    }

    private sealed class ToastStackState
    {
        private readonly Panel panel;
        private readonly Dictionary<Control, double> previousY = [];
        private readonly Dictionary<Control, ITransform?> originalTransforms = [];
        private readonly Dictionary<Control, TranslateTransform> transforms = [];
        private bool isEnabled;
        private bool isListening;

        public ToastStackState(Panel panel)
        {
            this.panel = panel;
        }

        public void Enable()
        {
            if (isEnabled)
            {
                return;
            }

            isEnabled = true;
            panel.AttachedToVisualTree += OnAttachedToVisualTree;
            panel.DetachedFromVisualTree += OnDetachedFromVisualTree;
            if (panel.IsAttachedToVisualTree())
            {
                StartListening();
            }
        }

        public void Disable()
        {
            if (!isEnabled)
            {
                return;
            }

            isEnabled = false;
            panel.AttachedToVisualTree -= OnAttachedToVisualTree;
            panel.DetachedFromVisualTree -= OnDetachedFromVisualTree;
            StopListening();
            Reset();
        }

        private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs args)
        {
            StartListening();
        }

        private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs args)
        {
            StopListening();
            Reset();
        }

        private void StartListening()
        {
            if (!isEnabled || isListening)
            {
                return;
            }

            isListening = true;
            panel.LayoutUpdated += OnLayoutUpdated;
        }

        private void StopListening()
        {
            if (!isListening)
            {
                return;
            }

            isListening = false;
            panel.LayoutUpdated -= OnLayoutUpdated;
        }

        private void OnLayoutUpdated(object? sender, EventArgs args)
        {
            var children = panel.Children.ToArray();
            var activeChildren = new HashSet<Control>(children);
            foreach (var child in previousY.Keys.Where(child => !activeChildren.Contains(child)).ToArray())
            {
                ResetChild(child);
            }

            foreach (var child in children)
            {
                var currentY = child.Bounds.Y;
                if (previousY.TryGetValue(child, out var priorY) && priorY != currentY)
                {
                    AnimateToCurrentLayout(child, CalculateInitialOffset(priorY, currentY));
                }

                previousY[child] = currentY;
            }
        }

        private void AnimateToCurrentLayout(Control child, double initialOffset)
        {
            var transform = GetTransform(child);
            transform.Transitions = null;
            transform.Y = initialOffset;
            transform.Transitions =
            [
                new DoubleTransition
                {
                    Property = TranslateTransform.YProperty,
                    Duration = MotionTokens.FastDuration,
                    Easing = new ExponentialEaseOut()
                }
            ];
            transform.Y = 0;
        }

        private TranslateTransform GetTransform(Control child)
        {
            if (transforms.TryGetValue(child, out var transform))
            {
                return transform;
            }

            originalTransforms.Add(child, child.RenderTransform);
            transform = new TranslateTransform();
            child.RenderTransform = transform;
            transforms.Add(child, transform);
            return transform;
        }

        private void Reset()
        {
            foreach (var child in transforms.Keys.ToArray())
            {
                ResetChild(child);
            }

            previousY.Clear();
        }

        private void ResetChild(Control child)
        {
            previousY.Remove(child);
            if (!transforms.Remove(child, out var transform))
            {
                return;
            }

            transform.Transitions = null;
            transform.Y = 0;
            if (originalTransforms.Remove(child, out var originalTransform)
                && ReferenceEquals(child.RenderTransform, transform))
            {
                child.RenderTransform = originalTransform;
            }
        }
    }

    private sealed class ToastStackMotionOwner;
}
