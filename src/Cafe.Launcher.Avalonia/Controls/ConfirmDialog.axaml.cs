using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

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

    public static readonly StyledProperty<string?> MessageProperty =
        AvaloniaProperty.Register<ConfirmDialog, string?>(nameof(Message));

    public static readonly StyledProperty<string?> AlertTitleProperty =
        AvaloniaProperty.Register<ConfirmDialog, string?>(nameof(AlertTitle));

    public static readonly StyledProperty<bool> IsWarningAlertProperty =
        AvaloniaProperty.Register<ConfirmDialog, bool>(nameof(IsWarningAlert));

    public static readonly StyledProperty<bool> IsDangerAlertProperty =
        AvaloniaProperty.Register<ConfirmDialog, bool>(nameof(IsDangerAlert));

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

    public static readonly DirectProperty<ConfirmDialog, string?> DisplayMessageProperty =
        AvaloniaProperty.RegisterDirect<ConfirmDialog, string?>(nameof(DisplayMessage), dialog => dialog.DisplayMessage);

    private string? displayMessage;

    public bool IsOpen { get => GetValue(IsOpenProperty); set => SetValue(IsOpenProperty, value); }
    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? Description { get => GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public string IconKind { get => GetValue(IconKindProperty); set => SetValue(IconKindProperty, value); }
    public string? Message { get => GetValue(MessageProperty); set => SetValue(MessageProperty, value); }
    public string? AlertTitle { get => GetValue(AlertTitleProperty); set => SetValue(AlertTitleProperty, value); }
    public bool IsWarningAlert { get => GetValue(IsWarningAlertProperty); set => SetValue(IsWarningAlertProperty, value); }
    public bool IsDangerAlert { get => GetValue(IsDangerAlertProperty); set => SetValue(IsDangerAlertProperty, value); }
    public string CancelText { get => GetValue(CancelTextProperty); set => SetValue(CancelTextProperty, value); }
    public ICommand? CancelCommand { get => GetValue(CancelCommandProperty); set => SetValue(CancelCommandProperty, value); }
    public string ConfirmText { get => GetValue(ConfirmTextProperty); set => SetValue(ConfirmTextProperty, value); }
    public ICommand? ConfirmCommand { get => GetValue(ConfirmCommandProperty); set => SetValue(ConfirmCommandProperty, value); }
    public string ConfirmIconKind { get => GetValue(ConfirmIconKindProperty); set => SetValue(ConfirmIconKindProperty, value); }
    public bool IsDangerConfirm { get => GetValue(IsDangerConfirmProperty); set => SetValue(IsDangerConfirmProperty, value); }
    public double DialogMaxWidth { get => GetValue(DialogMaxWidthProperty); set => SetValue(DialogMaxWidthProperty, value); }
    public string? CloseToolTip { get => GetValue(CloseToolTipProperty); set => SetValue(CloseToolTipProperty, value); }
    public string? DisplayMessage => displayMessage;

    public ConfirmDialog()
    {
        InitializeComponent();
        PropertyChanged += OnPropertyChanged;
    }

    private void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == MessageProperty || e.Property == DescriptionProperty)
        {
            SetAndRaise(
                DisplayMessageProperty,
                ref displayMessage,
                string.IsNullOrWhiteSpace(Message) ? Description : Message);
        }

        // M3 dialog guidance: the safe (cancel) action receives initial focus.
        if (e.Property == IsOpenProperty && e.NewValue is true)
        {
            Dispatcher.UIThread.Post(() => SafeActionButton.Focus(), DispatcherPriority.Background);
        }
    }
}
