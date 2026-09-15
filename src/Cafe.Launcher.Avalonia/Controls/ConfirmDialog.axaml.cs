using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Threading;

namespace Cafe.Launcher.Avalonia.Controls;

/// <summary>
/// Basic 形态确认框的类型化门面（ADR-015）。安全操作位于左侧、
/// 主操作填充式位于右侧，危险确认经 <see cref="IsDangerConfirm"/> 切换；
/// 默认焦点落在安全操作上。
/// </summary>
public partial class ConfirmDialog : UserControl
{
    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<ConfirmDialog, bool>(nameof(IsOpen));

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<ConfirmDialog, string?>(nameof(Title));

    /// <summary>滚动区正文；为空时仅保留标题。</summary>
    public static readonly StyledProperty<string?> MessageProperty =
        AvaloniaProperty.Register<ConfirmDialog, string?>(nameof(Message));

    /// <summary>
    /// 位于正文下方的一个可选勾选项（ADR-030）；为空时整行折叠，
    /// 因此未使用该槽的确认框渲染不变。
    /// </summary>
    public static readonly StyledProperty<string?> OptionTextProperty =
        AvaloniaProperty.Register<ConfirmDialog, string?>(nameof(OptionText));

    /// <summary>勾选项的状态；默认双向绑定，供调用方直接读回用户的选择。</summary>
    public static readonly StyledProperty<bool> IsOptionCheckedProperty =
        AvaloniaProperty.Register<ConfirmDialog, bool>(
            nameof(IsOptionChecked),
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string> CancelTextProperty =
        AvaloniaProperty.Register<ConfirmDialog, string>(nameof(CancelText), "Cancel");

    public static readonly StyledProperty<ICommand?> CancelCommandProperty =
        AvaloniaProperty.Register<ConfirmDialog, ICommand?>(nameof(CancelCommand));

    public static readonly StyledProperty<string> ConfirmTextProperty =
        AvaloniaProperty.Register<ConfirmDialog, string>(nameof(ConfirmText), "Confirm");

    public static readonly StyledProperty<ICommand?> ConfirmCommandProperty =
        AvaloniaProperty.Register<ConfirmDialog, ICommand?>(nameof(ConfirmCommand));

    public static readonly StyledProperty<bool> IsDangerConfirmProperty =
        AvaloniaProperty.Register<ConfirmDialog, bool>(nameof(IsDangerConfirm));

    public bool IsOpen { get => GetValue(IsOpenProperty); set => SetValue(IsOpenProperty, value); }
    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? Message { get => GetValue(MessageProperty); set => SetValue(MessageProperty, value); }
    public string? OptionText { get => GetValue(OptionTextProperty); set => SetValue(OptionTextProperty, value); }
    public bool IsOptionChecked { get => GetValue(IsOptionCheckedProperty); set => SetValue(IsOptionCheckedProperty, value); }
    public string CancelText { get => GetValue(CancelTextProperty); set => SetValue(CancelTextProperty, value); }
    public ICommand? CancelCommand { get => GetValue(CancelCommandProperty); set => SetValue(CancelCommandProperty, value); }
    public string ConfirmText { get => GetValue(ConfirmTextProperty); set => SetValue(ConfirmTextProperty, value); }
    public ICommand? ConfirmCommand { get => GetValue(ConfirmCommandProperty); set => SetValue(ConfirmCommandProperty, value); }
    public bool IsDangerConfirm { get => GetValue(IsDangerConfirmProperty); set => SetValue(IsDangerConfirmProperty, value); }

    public ConfirmDialog()
    {
        InitializeComponent();
        PropertyChanged += OnPropertyChanged;
    }

    private void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        // M3 dialog guidance: the safe (cancel) action receives initial focus.
        if (e.Property == IsOpenProperty && e.NewValue is true)
        {
            Dispatcher.UIThread.Post(
                () => this.FindControl<Button>("SafeActionButton")?.Focus(),
                DispatcherPriority.Background);
        }
    }
}
