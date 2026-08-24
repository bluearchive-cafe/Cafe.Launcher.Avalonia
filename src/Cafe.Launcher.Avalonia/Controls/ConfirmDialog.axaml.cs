using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Cafe.Launcher.Avalonia.Controls;

public partial class ConfirmDialog : UserControl
{
    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<ConfirmDialog, bool>(nameof(IsOpen));

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<ConfirmDialog, string?>(nameof(Title));

    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<ConfirmDialog, string?>(nameof(Description));

    public static readonly StyledProperty<string> IconKindProperty =
        AvaloniaProperty.Register<ConfirmDialog, string>(nameof(IconKind), "AlertCircle");

    public static readonly StyledProperty<bool> IsDangerIconProperty =
        AvaloniaProperty.Register<ConfirmDialog, bool>(nameof(IsDangerIcon));

    public static readonly StyledProperty<string?> MessageProperty =
        AvaloniaProperty.Register<ConfirmDialog, string?>(nameof(Message));

    public static readonly StyledProperty<IBrush?> MessageBackgroundProperty =
        AvaloniaProperty.Register<ConfirmDialog, IBrush?>(nameof(MessageBackground));

    public static readonly StyledProperty<string> CancelTextProperty =
        AvaloniaProperty.Register<ConfirmDialog, string>(nameof(CancelText), "Cancel");

    public static readonly StyledProperty<ICommand?> CancelCommandProperty =
        AvaloniaProperty.Register<ConfirmDialog, ICommand?>(nameof(CancelCommand));

    public static readonly StyledProperty<string> ConfirmTextProperty =
        AvaloniaProperty.Register<ConfirmDialog, string>(nameof(ConfirmText), "Confirm");

    public static readonly StyledProperty<ICommand?> ConfirmCommandProperty =
        AvaloniaProperty.Register<ConfirmDialog, ICommand?>(nameof(ConfirmCommand));

    public static readonly StyledProperty<string> ConfirmIconKindProperty =
        AvaloniaProperty.Register<ConfirmDialog, string>(nameof(ConfirmIconKind), "Check");

    public static readonly StyledProperty<bool> IsDangerConfirmProperty =
        AvaloniaProperty.Register<ConfirmDialog, bool>(nameof(IsDangerConfirm));

    public static readonly StyledProperty<double> DialogMaxWidthProperty =
        AvaloniaProperty.Register<ConfirmDialog, double>(nameof(DialogMaxWidth), 540);

    public static readonly StyledProperty<string?> CloseToolTipProperty =
        AvaloniaProperty.Register<ConfirmDialog, string?>(nameof(CloseToolTip));

    public bool IsOpen { get => GetValue(IsOpenProperty); set => SetValue(IsOpenProperty, value); }
    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? Description { get => GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public string IconKind { get => GetValue(IconKindProperty); set => SetValue(IconKindProperty, value); }
    public bool IsDangerIcon { get => GetValue(IsDangerIconProperty); set => SetValue(IsDangerIconProperty, value); }
    public string? Message { get => GetValue(MessageProperty); set => SetValue(MessageProperty, value); }
    public IBrush? MessageBackground { get => GetValue(MessageBackgroundProperty); set => SetValue(MessageBackgroundProperty, value); }
    public string CancelText { get => GetValue(CancelTextProperty); set => SetValue(CancelTextProperty, value); }
    public ICommand? CancelCommand { get => GetValue(CancelCommandProperty); set => SetValue(CancelCommandProperty, value); }
    public string ConfirmText { get => GetValue(ConfirmTextProperty); set => SetValue(ConfirmTextProperty, value); }
    public ICommand? ConfirmCommand { get => GetValue(ConfirmCommandProperty); set => SetValue(ConfirmCommandProperty, value); }
    public string ConfirmIconKind { get => GetValue(ConfirmIconKindProperty); set => SetValue(ConfirmIconKindProperty, value); }
    public bool IsDangerConfirm { get => GetValue(IsDangerConfirmProperty); set => SetValue(IsDangerConfirmProperty, value); }
    public double DialogMaxWidth { get => GetValue(DialogMaxWidthProperty); set => SetValue(DialogMaxWidthProperty, value); }
    public string? CloseToolTip { get => GetValue(CloseToolTipProperty); set => SetValue(CloseToolTipProperty, value); }

    public ConfirmDialog()
    {
        InitializeComponent();
    }
}
