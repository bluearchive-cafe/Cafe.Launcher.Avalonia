using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;

namespace Cafe.Launcher.Avalonia.Controls;

/// <summary>
/// Composes <see cref="SettingRow"/> with a standard settings ComboBox that binds
/// a string code through <c>SelectableOption.Code</c>. Replaces the repeated
/// row-plus-combo pattern in settings sections.
/// </summary>
public partial class SettingComboRow : UserControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<SettingComboRow, string?>(nameof(Title));

    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<SettingComboRow, string?>(nameof(Description));

    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<SettingComboRow, IEnumerable?>(nameof(ItemsSource));

    public static readonly StyledProperty<string?> SelectedValueProperty =
        AvaloniaProperty.Register<SettingComboRow, string?>(
            nameof(SelectedValue),
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<IDataTemplate?> ItemTemplateProperty =
        AvaloniaProperty.Register<SettingComboRow, IDataTemplate?>(nameof(ItemTemplate));

    /// <summary>Gets or sets the localized row title. Also used as the ComboBox accessible name.</summary>
    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }

    /// <summary>Gets or sets the localized row description shown under the title.</summary>
    public string? Description { get => GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }

    /// <summary>Gets or sets the selectable options exposed by the ComboBox.</summary>
    public IEnumerable? ItemsSource { get => GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }

    /// <summary>Gets or sets the selected option code, mapped through <c>SelectableOption.Code</c>.</summary>
    public string? SelectedValue { get => GetValue(SelectedValueProperty); set => SetValue(SelectedValueProperty, value); }

    /// <summary>Gets or sets the template used to render each selectable option.</summary>
    public IDataTemplate? ItemTemplate { get => GetValue(ItemTemplateProperty); set => SetValue(ItemTemplateProperty, value); }

    public SettingComboRow()
    {
        InitializeComponent();
    }
}
