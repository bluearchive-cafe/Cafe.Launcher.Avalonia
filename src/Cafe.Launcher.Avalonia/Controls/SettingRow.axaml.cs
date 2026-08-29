using Avalonia;
using Avalonia.Controls;

namespace Cafe.Launcher.Avalonia.Controls;

public partial class SettingRow : UserControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<SettingRow, string?>(nameof(Title));

    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<SettingRow, string?>(nameof(Description));

    public static readonly StyledProperty<string?> HintProperty =
        AvaloniaProperty.Register<SettingRow, string?>(nameof(Hint));

    public static readonly StyledProperty<bool> IsHintVisibleProperty =
        AvaloniaProperty.Register<SettingRow, bool>(nameof(IsHintVisible));

    public static readonly StyledProperty<object?> ActionProperty =
        AvaloniaProperty.Register<SettingRow, object?>(nameof(Action));

    public static readonly StyledProperty<bool> ShowTopDividerProperty =
        AvaloniaProperty.Register<SettingRow, bool>(nameof(ShowTopDivider), true);

    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? Description { get => GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public string? Hint { get => GetValue(HintProperty); set => SetValue(HintProperty, value); }
    public bool IsHintVisible { get => GetValue(IsHintVisibleProperty); set => SetValue(IsHintVisibleProperty, value); }
    public object? Action { get => GetValue(ActionProperty); set => SetValue(ActionProperty, value); }
    public bool ShowTopDivider { get => GetValue(ShowTopDividerProperty); set => SetValue(ShowTopDividerProperty, value); }

    public SettingRow()
    {
        InitializeComponent();
    }
}
