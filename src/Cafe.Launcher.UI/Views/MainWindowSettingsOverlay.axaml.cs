using Avalonia.Controls;

namespace Cafe.Launcher.UI.Views;

internal partial class MainWindowSettingsOverlay : UserControl
{
    public MainWindowSettingsOverlay()
    {
        InitializeComponent();
        SettingsNavigation.SelectionChanged += OnCategorySelectionChanged;
    }

    private void OnCategorySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // All categories share this viewport. A new category must not inherit
        // the previous page's offset; leave keyboard focus with the navigation.
        if (ReferenceEquals(e.Source, SettingsNavigation))
            SettingsContentScroll.Offset = default;
    }
}
