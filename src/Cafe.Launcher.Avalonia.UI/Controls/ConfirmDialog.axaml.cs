using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Cafe.Launcher.Avalonia.Controls;

/// <summary>
/// Basic 形态确认框的类型化门面（ADR-015 / ADR-040）。
/// 动作顺序按 Fluent/WinUI：do-it（确认）在最左、安全动作（取消）在最右。
/// 默认按钮语义同样按 Fluent：
/// <list type="bullet">
/// <item>非破坏性确认 → 确认动作就是默认按钮：强调填充（primary-action）+ Enter 响应 + 打开时获得初始焦点；</item>
/// <item>破坏性确认（<see cref="IsDangerConfirm"/>）→ <b>不设默认按钮</b>：初始焦点仍在安全动作上，
/// Enter 因此落在安全动作，破坏性操作不会被键盘一次带走。</item>
/// </list>
/// 刻意不使用 <see cref="Button.IsDefaultProperty"/>：主窗口同时存在多个 ConfirmDialog 实例
/// （各自 IsOpen 互斥），而 IsDefault 是窗口级候选，隐藏实例之间的默认按钮归属无法在本地约束。
/// WinUI 的规则是「焦点所在控件若自行处理 Enter，则默认按钮不响应」，本类的处理函数遵守同一条。
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

    /// <summary>
    /// 当前形态下 Enter 应当触发的动作；破坏性确认返回 <c>null</c>（不设默认按钮）。
    /// 键盘处理与行为测试共用这一处判定。
    /// </summary>
    internal ICommand? DefaultActionCommand => IsDangerConfirm ? null : ConfirmCommand;

    public ConfirmDialog()
    {
        InitializeComponent();
        PropertyChanged += OnPropertyChanged;
        // 冒泡到本控件：已被焦点控件处理掉的 Enter（例如聚焦中的按钮）不会到达这里，
        // 与 WinUI「聚焦控件处理 Enter 时默认按钮不响应」一致。
        AddHandler(KeyDownEvent, OnDialogKeyDown, RoutingStrategies.Bubble);
    }

    private void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsOpenProperty && e.NewValue is true)
        {
            Dispatcher.UIThread.Post(FocusInitialAction, DispatcherPriority.Background);
        }
    }

    /// <summary>
    /// 初始焦点：非破坏性确认落在默认按钮（确认）上，破坏性确认落在安全动作上（ADR-040）。
    /// 用 <see cref="NavigationMethod.Unspecified"/> 而不是 Tab：自动聚焦不是键盘导航，
    /// 不该在打开对话框的一瞬间画出焦点环（焦点视觉只在用户真按键盘导航时出现）。
    /// </summary>
    internal void FocusInitialAction()
    {
        var target = IsDangerConfirm
            ? this.FindControl<Button>("SafeActionButton")
            : this.FindControl<Button>("PrimaryActionButton") ?? this.FindControl<Button>("SafeActionButton");
        target?.Focus(NavigationMethod.Unspecified);
    }

    private void OnDialogKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || !IsOpen)
        {
            return;
        }

        var command = DefaultActionCommand;
        if (command is null || !command.CanExecute(null))
        {
            return;
        }

        command.Execute(null);
        e.Handled = true;
    }
}
