using Avalonia;
using Avalonia.Controls;

namespace Cafe.Launcher.UI.Controls;

internal partial class SettingRow : UserControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<SettingRow, string?>(nameof(Title));

    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<SettingRow, string?>(nameof(Description));

    public static readonly StyledProperty<string?> HintProperty =
        AvaloniaProperty.Register<SettingRow, string?>(nameof(Hint));

    /// <summary>提示显示闸门，默认开启；提示文本为空时始终折叠。</summary>
    public static readonly StyledProperty<bool> IsHintVisibleProperty =
        AvaloniaProperty.Register<SettingRow, bool>(nameof(IsHintVisible), true);

    public static readonly StyledProperty<object?> ActionProperty =
        AvaloniaProperty.Register<SettingRow, object?>(nameof(Action));

    static SettingRow()
    {
        // 空白描述不得占用一行：FASettingsExpanderItem 的折叠门是「Description 为 null」，
        // 接缝处把空串归一为 null，而非把 null 语义漏进模板。
        DescriptionProperty.Changed.AddClassHandler<SettingRow>(OnDescriptionChanged);
    }

    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? Description { get => GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public string? Hint { get => GetValue(HintProperty); set => SetValue(HintProperty, value); }
    public bool IsHintVisible { get => GetValue(IsHintVisibleProperty); set => SetValue(IsHintVisibleProperty, value); }
    public object? Action { get => GetValue(ActionProperty); set => SetValue(ActionProperty, value); }

    public SettingRow()
    {
        InitializeComponent();
    }

    private static void OnDescriptionChanged(SettingRow owner, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.GetNewValue<string?>() is { } description && string.IsNullOrWhiteSpace(description))
        {
            owner.SetCurrentValue(DescriptionProperty, null);
        }
    }
}
