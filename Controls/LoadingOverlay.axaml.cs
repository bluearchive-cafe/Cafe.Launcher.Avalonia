using Avalonia;
using Avalonia.Controls;

namespace Cafe.Launcher.Avalonia.Controls;

public partial class LoadingOverlay : UserControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<LoadingOverlay, string?>(nameof(Text));

    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }

    public LoadingOverlay()
    {
        InitializeComponent();
    }
}
