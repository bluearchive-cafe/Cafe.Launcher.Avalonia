using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace Cafe.Launcher.Avalonia.Controls;

public partial class DialogFrame : UserControl
{
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<DialogFrame, string?>(nameof(Title));
    public static readonly StyledProperty<string?> DescriptionProperty = AvaloniaProperty.Register<DialogFrame, string?>(nameof(Description));
    public static readonly StyledProperty<string> IconKindProperty = AvaloniaProperty.Register<DialogFrame, string>(nameof(IconKind), "AlertCircle");
    public static readonly StyledProperty<bool> IsDangerProperty = AvaloniaProperty.Register<DialogFrame, bool>(nameof(IsDanger));
    public static readonly StyledProperty<bool> ShowIconProperty = AvaloniaProperty.Register<DialogFrame, bool>(nameof(ShowIcon), true);
    public static readonly StyledProperty<bool> ShowDescriptionProperty = AvaloniaProperty.Register<DialogFrame, bool>(nameof(ShowDescription), true);
    public static readonly StyledProperty<bool> ShowCloseButtonProperty = AvaloniaProperty.Register<DialogFrame, bool>(nameof(ShowCloseButton), true);
    public static readonly StyledProperty<bool> UseToolPanelChromeProperty = AvaloniaProperty.Register<DialogFrame, bool>(nameof(UseToolPanelChrome));
    public static readonly StyledProperty<bool> IsConfirmPanelProperty = AvaloniaProperty.Register<DialogFrame, bool>(nameof(IsConfirmPanel));
    public static readonly StyledProperty<double> BodyMaxHeightProperty = AvaloniaProperty.Register<DialogFrame, double>(nameof(BodyMaxHeight), double.PositiveInfinity);
    public static readonly StyledProperty<ScrollBarVisibility> BodyVerticalScrollBarVisibilityProperty = AvaloniaProperty.Register<DialogFrame, ScrollBarVisibility>(nameof(BodyVerticalScrollBarVisibility), ScrollBarVisibility.Auto);
    public static readonly StyledProperty<ICommand?> CloseCommandProperty = AvaloniaProperty.Register<DialogFrame, ICommand?>(nameof(CloseCommand));
    public static readonly StyledProperty<string?> CloseToolTipProperty = AvaloniaProperty.Register<DialogFrame, string?>(nameof(CloseToolTip));
    public static readonly StyledProperty<object?> BodyContentProperty = AvaloniaProperty.Register<DialogFrame, object?>(nameof(BodyContent));
    public static readonly StyledProperty<object?> ActionsContentProperty = AvaloniaProperty.Register<DialogFrame, object?>(nameof(ActionsContent));

    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? Description { get => GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public string IconKind { get => GetValue(IconKindProperty); set => SetValue(IconKindProperty, value); }
    public bool IsDanger { get => GetValue(IsDangerProperty); set => SetValue(IsDangerProperty, value); }
    public bool ShowIcon { get => GetValue(ShowIconProperty); set => SetValue(ShowIconProperty, value); }
    public bool ShowDescription { get => GetValue(ShowDescriptionProperty); set => SetValue(ShowDescriptionProperty, value); }
    public bool ShowCloseButton { get => GetValue(ShowCloseButtonProperty); set => SetValue(ShowCloseButtonProperty, value); }
    public bool UseToolPanelChrome { get => GetValue(UseToolPanelChromeProperty); set => SetValue(UseToolPanelChromeProperty, value); }
    public bool IsConfirmPanel { get => GetValue(IsConfirmPanelProperty); set => SetValue(IsConfirmPanelProperty, value); }
    public double BodyMaxHeight { get => GetValue(BodyMaxHeightProperty); set => SetValue(BodyMaxHeightProperty, value); }
    public ScrollBarVisibility BodyVerticalScrollBarVisibility { get => GetValue(BodyVerticalScrollBarVisibilityProperty); set => SetValue(BodyVerticalScrollBarVisibilityProperty, value); }
    public ICommand? CloseCommand { get => GetValue(CloseCommandProperty); set => SetValue(CloseCommandProperty, value); }
    public string? CloseToolTip { get => GetValue(CloseToolTipProperty); set => SetValue(CloseToolTipProperty, value); }
    public object? BodyContent { get => GetValue(BodyContentProperty); set => SetValue(BodyContentProperty, value); }
    public object? ActionsContent { get => GetValue(ActionsContentProperty); set => SetValue(ActionsContentProperty, value); }

    public DialogFrame() => InitializeComponent();
}
