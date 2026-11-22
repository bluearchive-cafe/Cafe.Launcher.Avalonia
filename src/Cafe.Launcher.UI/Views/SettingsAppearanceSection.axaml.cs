using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Cafe.Launcher.UI.Views;

internal partial class SettingsAppearanceSection : UserControl
{
    private bool collapseClearedContentFocus;

    public SettingsAppearanceSection()
    {
        InitializeComponent();
        AdvancedColorsExpander.PropertyChanged += OnSectionPropertyChanged;
        AdvancedColorsExpander.AddHandler(LostFocusEvent, OnExpanderLostFocus, RoutingStrategies.Bubble);
    }

    private void OnExpanderLostFocus(object? sender, RoutedEventArgs e)
    {
        // FluentAvalonia 的 Expander 在折叠时先移除内容、清空焦点，再通知
        // IsExpanded 观察者；此时已无法从视觉树判断焦点来自内容区，故在失焦时记录。
        if (AdvancedColorsExpander.Content is Control content
            && e.Source is Control source
            && (ReferenceEquals(content, source) || content.IsVisualAncestorOf(source)))
        {
            collapseClearedContentFocus = true;
        }
    }

    private void OnPaletteKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End))
            return;
        var choices = PaletteChoices.GetVisualDescendants().OfType<RadioButton>()
            .Where(choice => choice.IsEffectivelyEnabled && choice.IsEffectivelyVisible).ToArray();
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        var index = System.Array.IndexOf(choices, focused);
        if (index < 0 || choices.Length == 0)
            return;
        var next = e.Key switch
        {
            Key.Home => 0,
            Key.End => choices.Length - 1,
            Key.Left or Key.Up => (index + choices.Length - 1) % choices.Length,
            _ => (index + 1) % choices.Length
        };
        choices[next].Focus(NavigationMethod.Directional);
        choices[next].SetCurrentValue(ToggleButton.IsCheckedProperty, true);
        e.Handled = true;
    }

    private void OnSectionPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Expander.IsExpandedProperty || sender is not Expander { IsExpanded: false } expander)
            return;

        // A programmatic collapse can hide the keyboard's current target. Keep the
        // user at the section header rather than leaving focus inside hidden content.
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        var focusWasInContent = focused is Control control && expander.Content is Control content
            && (ReferenceEquals(content, control) || content.IsVisualAncestorOf(control));
        if (focusWasInContent || (focused is null && collapseClearedContentFocus))
        {
            expander.GetVisualDescendants().OfType<ToggleButton>().FirstOrDefault()
                ?.Focus(NavigationMethod.Tab);
        }

        collapseClearedContentFocus = false;
    }
}
