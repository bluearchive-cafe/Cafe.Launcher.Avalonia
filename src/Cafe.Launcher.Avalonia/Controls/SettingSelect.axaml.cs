using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;

namespace Cafe.Launcher.Avalonia.Controls;

/// <summary>
/// Reusable M3 settings row for selectable launcher options.
/// </summary>
public partial class SettingSelect : UserControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<SettingSelect, string?>(nameof(Title));

    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<SettingSelect, string?>(nameof(Description));

    public static readonly StyledProperty<string?> AutomationNameProperty =
        AvaloniaProperty.Register<SettingSelect, string?>(nameof(AutomationName));

    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<SettingSelect, IEnumerable?>(nameof(ItemsSource));

    public static readonly StyledProperty<object?> SelectedValueProperty =
        AvaloniaProperty.Register<SettingSelect, object?>(
            nameof(SelectedValue),
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<bool> ShowTopDividerProperty =
        AvaloniaProperty.Register<SettingSelect, bool>(nameof(ShowTopDivider), true);

    public static readonly StyledProperty<string?> HintProperty =
        AvaloniaProperty.Register<SettingSelect, string?>(nameof(Hint));

    public static readonly StyledProperty<bool> IsHintVisibleProperty =
        AvaloniaProperty.Register<SettingSelect, bool>(nameof(IsHintVisible));

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public string? AutomationName
    {
        get => GetValue(AutomationNameProperty);
        set => SetValue(AutomationNameProperty, value);
    }

    public IEnumerable? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public object? SelectedValue
    {
        get => GetValue(SelectedValueProperty);
        set => SetValue(SelectedValueProperty, value);
    }

    public bool ShowTopDivider
    {
        get => GetValue(ShowTopDividerProperty);
        set => SetValue(ShowTopDividerProperty, value);
    }

    public string? Hint
    {
        get => GetValue(HintProperty);
        set => SetValue(HintProperty, value);
    }

    public bool IsHintVisible
    {
        get => GetValue(IsHintVisibleProperty);
        set => SetValue(IsHintVisibleProperty, value);
    }

    public SettingSelect()
    {
        InitializeComponent();
    }
}
