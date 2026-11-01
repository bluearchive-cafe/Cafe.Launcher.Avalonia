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
