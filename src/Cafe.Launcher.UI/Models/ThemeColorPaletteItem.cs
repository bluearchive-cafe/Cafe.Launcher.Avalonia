using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cafe.Launcher.UI.Models;

internal sealed partial class ThemeColorPaletteItem : ObservableObject
{
    [ObservableProperty]
    private int index;

    [ObservableProperty]
    private string colorHex = "";

    [ObservableProperty]
    private IBrush brush = Brushes.Transparent;

    [ObservableProperty]
    private bool isSelected;
}
