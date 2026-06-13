using Avalonia.Media;

namespace Cafe.Launcher.Avalonia.Models;

/// <summary>
/// Represents a single carousel indicator dot. Colors chosen to be visible on both light and dark backgrounds.
/// </summary>
public sealed partial class BannerDot : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    private static readonly SolidColorBrush ActiveBrush = new(0xFF2E7DF6);
    // Medium gray: visible on dark themes (unlike very light gray) and still distinct on light themes
    private static readonly SolidColorBrush InactiveBrush = new(0xFF6B7280);

    public int Index { get; init; }

    private bool isActive;
    public bool IsActive
    {
        get => isActive;
        set
        {
            if (SetProperty(ref isActive, value))
                OnPropertyChanged(nameof(DotBrush));
        }
    }

    public IBrush DotBrush => IsActive ? ActiveBrush : InactiveBrush;
}
