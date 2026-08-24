using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cafe.Launcher.Avalonia.Models;

public sealed partial class ThemeColorPaletteItem : ObservableObject
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
