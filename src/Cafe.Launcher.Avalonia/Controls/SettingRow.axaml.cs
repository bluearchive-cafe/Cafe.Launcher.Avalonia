using Avalonia;
using Avalonia.Controls;

namespace Cafe.Launcher.Avalonia.Controls;

public partial class SettingRow : UserControl
{
    public static readonly StyledProperty<string> IconKindProperty =
        AvaloniaProperty.Register<SettingRow, string>(nameof(IconKind), "AlertCircle");

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<SettingRow, string?>(nameof(Title));

    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<SettingRow, string?>(nameof(Description));

    public static readonly StyledProperty<object?> ActionProperty =
        AvaloniaProperty.Register<SettingRow, object?>(nameof(Action));

    public string IconKind { get => GetValue(IconKindProperty); set => SetValue(IconKindProperty, value); }
    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? Description { get => GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public object? Action { get => GetValue(ActionProperty); set => SetValue(ActionProperty, value); }

    public SettingRow()
    {
        InitializeComponent();
    }
}
