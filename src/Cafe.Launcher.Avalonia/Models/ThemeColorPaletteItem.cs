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

    /// <summary>勾选标记前景：由 ViewModel 与 <see cref="Brush"/>（生成的 Primary）
    /// 同一 DynamicScheme 配对产生（OnPrimary），保证任意种子的勾选对比度。</summary>
    [ObservableProperty]
    private IBrush checkBrush = Brushes.White;

    [ObservableProperty]
    private bool isSelected;
}
