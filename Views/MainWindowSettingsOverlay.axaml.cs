using Avalonia.Controls;
using Avalonia;
using Avalonia.Layout;

namespace Cafe.Launcher.Avalonia.Views;

public partial class MainWindowSettingsOverlay : UserControl
{
    private const double CompactBreakpoint = 760;
    private bool? isCompactLayout;

    public MainWindowSettingsOverlay()
    {
        InitializeComponent();
        SizeChanged += OnSizeChanged;
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        var isCompact = e.NewSize.Width < CompactBreakpoint;
        if (isCompactLayout == isCompact)
        {
            return;
        }

        isCompactLayout = isCompact;
        SettingsWorkspace.Classes.Set("compact", isCompact);
        SettingsWorkspace.ColumnDefinitions = isCompact
            ? new ColumnDefinitions("*")
            : new ColumnDefinitions("188,*");
        SettingsWorkspace.RowDefinitions = isCompact
            ? new RowDefinitions("Auto,*")
            : new RowDefinitions();

        Grid.SetColumn(SettingsNavigationPane, 0);
        Grid.SetRow(SettingsNavigationPane, 0);
        Grid.SetColumn(SettingsContentDivider, isCompact ? 0 : 1);
        Grid.SetRow(SettingsContentDivider, isCompact ? 1 : 0);
        Grid.SetColumn(SettingsFloatingClose, isCompact ? 0 : 1);
        Grid.SetRow(SettingsFloatingClose, isCompact ? 1 : 0);
        SettingsNavigation.MaxHeight = isCompact ? 108 : double.PositiveInfinity;
        SettingsNavigation.HorizontalAlignment = HorizontalAlignment.Stretch;
    }
}
