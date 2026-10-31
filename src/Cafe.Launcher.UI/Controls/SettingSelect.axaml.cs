using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;

namespace Cafe.Launcher.UI.Controls;

/// <summary>
/// Reusable M3 settings row for selectable launcher options. Forwards the shared
/// row's adaptive-wrap metrics so standalone hosts can override them per instance;
/// the defaults still come from the window-level styles.
/// </summary>
internal partial class SettingSelect : UserControl
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

    /// <summary>提示显示闸门，默认开启；提示文本为空时始终折叠。</summary>
    public static readonly StyledProperty<bool> IsHintVisibleProperty =
        AvaloniaProperty.Register<SettingSelect, bool>(nameof(IsHintVisible), true);

    /// <inheritdoc cref="SettingRow.MinimumCopyWidthProperty"/>
    public static readonly StyledProperty<double> MinimumCopyWidthProperty =
        AvaloniaProperty.Register<SettingSelect, double>(nameof(MinimumCopyWidth));

    /// <inheritdoc cref="SettingRow.ContentSpacingProperty"/>
    public static readonly StyledProperty<double> ContentSpacingProperty =
        AvaloniaProperty.Register<SettingSelect, double>(nameof(ContentSpacing));

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

    /// <inheritdoc cref="MinimumCopyWidthProperty"/>
    public double MinimumCopyWidth
    {
        get => GetValue(MinimumCopyWidthProperty);
        set => SetValue(MinimumCopyWidthProperty, value);
    }

    /// <inheritdoc cref="ContentSpacingProperty"/>
    public double ContentSpacing
    {
        get => GetValue(ContentSpacingProperty);
        set => SetValue(ContentSpacingProperty, value);
    }

    public SettingSelect()
    {
        InitializeComponent();
    }
}
