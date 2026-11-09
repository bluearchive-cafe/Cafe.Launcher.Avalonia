using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Cafe.Launcher.UI.Views;

internal partial class SettingsAppearanceSection : UserControl
{
    public SettingsAppearanceSection()
    {
        InitializeComponent();
        AdvancedColorsExpander.PropertyChanged += OnSectionPropertyChanged;
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
        if (focused is Control control && expander.Content is Control content
            && (ReferenceEquals(content, control) || content.IsVisualAncestorOf(control)))
        {
            expander.GetVisualDescendants().OfType<ToggleButton>().FirstOrDefault()
                ?.Focus(NavigationMethod.Tab);
        }
    }
}
