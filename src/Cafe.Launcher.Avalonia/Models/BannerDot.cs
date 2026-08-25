using Avalonia.Media;

namespace Cafe.Launcher.Avalonia.Models;

/// <summary>
/// Represents a single carousel indicator dot. Colors are applied via theme-aware style classes
/// (Border.banner-dot / Border.banner-dot.active in MainWindow.Styles.axaml).
/// </summary>
public sealed partial class BannerDot : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public int Index { get; init; }

    private string accessibleName = "";
    public string AccessibleName
    {
        get => accessibleName;
        set => SetProperty(ref accessibleName, value);
    }

    private bool isActive;
    public bool IsActive
    {
        get => isActive;
        set => SetProperty(ref isActive, value);
    }
}
