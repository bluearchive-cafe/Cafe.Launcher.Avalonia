using Avalonia;
using Avalonia.Controls;

namespace Cafe.Launcher.Avalonia.Controls;

public partial class SettingRow : UserControl
{
    private const double CompactBreakpoint = 600;
    private bool? isCompactLayout;
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<SettingRow, string?>(nameof(Title));

    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<SettingRow, string?>(nameof(Description));

    public static readonly StyledProperty<object?> ActionProperty =
        AvaloniaProperty.Register<SettingRow, object?>(nameof(Action));

    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? Description { get => GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public object? Action { get => GetValue(ActionProperty); set => SetValue(ActionProperty, value); }

    public SettingRow()
    {
        InitializeComponent();
        SizeChanged += OnSizeChanged;
        UpdateResponsiveLayout(Bounds.Width);
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e) =>
        UpdateResponsiveLayout(e.NewSize.Width);

    private void UpdateResponsiveLayout(double width)
    {
        var isCompact = width > 0 && width < CompactBreakpoint;
        if (isCompactLayout == isCompact)
        {
            return;
        }

        isCompactLayout = isCompact;
        RowLayout.ColumnDefinitions = isCompact ? new ColumnDefinitions("*") : new ColumnDefinitions("*,Auto");
        RowLayout.RowDefinitions = isCompact ? new RowDefinitions("Auto,Auto") : new RowDefinitions("*");
        Grid.SetColumn(ActionPresenter, isCompact ? 0 : 1);
        Grid.SetRow(ActionPresenter, isCompact ? 1 : 0);
        ActionPresenter.HorizontalAlignment = isCompact ? global::Avalonia.Layout.HorizontalAlignment.Stretch : global::Avalonia.Layout.HorizontalAlignment.Right;
        ActionPresenter.Margin = isCompact ? new Thickness(0, 8, 0, 0) : new Thickness();
        TitleCopy.MinWidth = isCompact ? 0 : 240;
    }
}
