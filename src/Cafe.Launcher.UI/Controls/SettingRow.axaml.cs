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

    public static readonly StyledProperty<bool> ShowTopDividerProperty =
        AvaloniaProperty.Register<SettingRow, bool>(nameof(ShowTopDivider), true);

    /// <summary>左右布局为说明文字保留的最小宽度；低于此空间时把操作放到下一行。
    /// 默认值由共享样式绑定 <c>Launcher.Component.Settings.Control.MinWidth</c>（220 DIP），
    /// 属设计规范记载的有意复用——调整该令牌会同步改变换行阈值。</summary>
    public static readonly StyledProperty<double> MinimumCopyWidthProperty =
        AvaloniaProperty.Register<SettingRow, double>(nameof(MinimumCopyWidth));

    /// <summary>说明与操作之间的间距，横排与竖排共用同一刻度。</summary>
    public static readonly StyledProperty<double> ContentSpacingProperty =
        AvaloniaProperty.Register<SettingRow, double>(nameof(ContentSpacing));

    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? Description { get => GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public string? Hint { get => GetValue(HintProperty); set => SetValue(HintProperty, value); }
    public bool IsHintVisible { get => GetValue(IsHintVisibleProperty); set => SetValue(IsHintVisibleProperty, value); }
    public object? Action { get => GetValue(ActionProperty); set => SetValue(ActionProperty, value); }
    public bool ShowTopDivider { get => GetValue(ShowTopDividerProperty); set => SetValue(ShowTopDividerProperty, value); }

    /// <inheritdoc cref="MinimumCopyWidthProperty"/>
    public double MinimumCopyWidth { get => GetValue(MinimumCopyWidthProperty); set => SetValue(MinimumCopyWidthProperty, value); }

    /// <inheritdoc cref="ContentSpacingProperty"/>
    public double ContentSpacing { get => GetValue(ContentSpacingProperty); set => SetValue(ContentSpacingProperty, value); }

    static SettingRow()
    {
        AffectsMeasure<SettingRow>(MinimumCopyWidthProperty, ContentSpacingProperty);
    }

    public SettingRow()
    {
        InitializeComponent();
    }

    /// <summary>根据操作的实际宽度换行，不重建控件，保留选中值、绑定与键盘焦点。</summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        ActionPresenter.Measure(Size.Infinity);
        var stacked = Action is not null && double.IsFinite(availableSize.Width)
            && availableSize.Width < MinimumCopyWidth + ContentSpacing + ActionPresenter.DesiredSize.Width;
        RowLayout.ColumnDefinitions[1].Width = stacked ? new GridLength(0) : GridLength.Auto;
        RowLayout.ColumnSpacing = stacked ? 0 : ContentSpacing;
        RowLayout.RowSpacing = stacked ? ContentSpacing : 0;
        // 竖排操作复用文案区的底部内缩；横排复位，保持整行垂直居中。
        ActionPresenter.Margin = new Thickness(0, 0, 0, stacked ? RowCopy.Margin.Bottom : 0);
        Grid.SetColumn(ActionPresenter, stacked ? 0 : 1);
        Grid.SetRow(ActionPresenter, stacked ? 1 : 0);
        return base.MeasureOverride(availableSize);
    }
}
