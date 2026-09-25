using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cafe.Launcher.Avalonia.Controls;

namespace Cafe.Launcher.Avalonia.HeadlessTests;

/// <summary>
/// ADR-040：确认框的动作顺序与默认按钮语义。
/// 顺序 = Fluent/WinUI（do-it 在左、安全动作在右）；
/// 默认按钮 = 非破坏性确认落在确认动作上，破坏性确认不设默认按钮（Enter 落回安全动作）。
/// </summary>
public sealed class ConfirmDialogHeadlessTests
{
    private sealed class RecorderCommand : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecuteResult { get; set; } = true;

        public int ExecuteCount { get; private set; }

        public bool CanExecute(object? parameter) => CanExecuteResult;

        public void Execute(object? parameter) => ExecuteCount++;
    }

    [AvaloniaFact]
    public void ActionBand_PlacesConfirmBeforeCancel()
    {
        using var host = CreateHost(new ConfirmDialog { IsOpen = true });

        var buttons = ActionButtons(host.Dialog);
        Assert.Equal(3, buttons.Count);
        Assert.Contains("primary-action", buttons[0].Classes);
        Assert.Contains("danger-action", buttons[1].Classes);
        Assert.Contains("flat-action", buttons[2].Classes);
    }

    [AvaloniaFact]
    public void NonDangerConfirm_UsesTheConfirmActionAsDefaultButton()
    {
        var confirm = new RecorderCommand();
        using var host = CreateHost(new ConfirmDialog
        {
            IsOpen = true,
            ConfirmCommand = confirm,
            ConfirmText = "Repair",
            CancelText = "Cancel"
        });

        Assert.Same(confirm, host.Dialog.DefaultActionCommand);
        Assert.Same(FindButton(host.Dialog, "PrimaryActionButton"), FocusedElement(host.Window));

        RaiseEnter(host.Dialog);
        Assert.Equal(1, confirm.ExecuteCount);
    }

    [AvaloniaFact]
    public void DangerConfirm_LeavesEnterToTheSafeAction()
    {
        var confirm = new RecorderCommand();
        using var host = CreateHost(new ConfirmDialog
        {
            IsOpen = true,
            IsDangerConfirm = true,
            ConfirmCommand = confirm,
            ConfirmText = "Uninstall",
            CancelText = "Cancel"
        });

        // 破坏性确认不设默认按钮：Enter 不该带走破坏性操作，初始焦点仍在安全动作上。
        Assert.Null(host.Dialog.DefaultActionCommand);
        Assert.Same(FindButton(host.Dialog, "SafeActionButton"), FocusedElement(host.Window));

        RaiseEnter(host.Dialog);
        Assert.Equal(0, confirm.ExecuteCount);
    }

    [AvaloniaFact]
    public void NonDangerConfirm_WhenTheCommandCannotExecute_IgnoresEnter()
    {
        var confirm = new RecorderCommand { CanExecuteResult = false };
        using var host = CreateHost(new ConfirmDialog
        {
            IsOpen = true,
            ConfirmCommand = confirm,
            ConfirmText = "Repair",
            CancelText = "Cancel"
        });

        RaiseEnter(host.Dialog);
        Assert.Equal(0, confirm.ExecuteCount);
    }

    /// <summary>
    /// ADR-040（修订二）：动作带按钮没有**可见**边框，但焦点环的 2px 厚度常驻预留
    /// （静止时描边透明）—— 这样聚焦只改颜色、不改几何。
    /// </summary>
    [AvaloniaFact]
    public void DialogActionButtons_ShowNoVisibleBorderAndReserveTheFocusRing()
    {
        var confirm = new RecorderCommand();
        using var host = CreateHost(new ConfirmDialog
        {
            IsOpen = true,
            ConfirmCommand = confirm,
            ConfirmText = "Repair",
            CancelText = "Cancel"
        });

        foreach (var button in ActionButtons(host.Dialog))
        {
            // Launcher.Color.Transparent 是 #00000000，故断言 alpha 而不是整色相等。
            Assert.Equal(0, Assert.IsType<SolidColorBrush>(button.BorderBrush).Color.A);
            Assert.Equal(new Thickness(2), button.BorderThickness);
        }
    }

    /// <summary>
    /// ADR-040（修订）：焦点视觉只在键盘导航时出现。
    /// 打开对话框是程序式聚焦 → 不画焦点环；用户按 Tab 导航 → 画（应用自己的 FocusRing）。
    /// </summary>
    [AvaloniaFact]
    public void DialogActionButtons_ShowFocusRingOnlyOnKeyboardNavigation()
    {
        var confirm = new RecorderCommand();
        using var host = CreateHost(new ConfirmDialog
        {
            IsOpen = true,
            ConfirmCommand = confirm,
            ConfirmText = "Repair",
            CancelText = "Cancel"
        });

        var primary = FindButton(host.Dialog, "PrimaryActionButton");

        // 程序式初始焦点（CreateHost 调用 FocusInitialAction）：不算键盘导航。
        Assert.True(primary.IsFocused);
        Assert.DoesNotContain(":focus-visible", primary.Classes);
        Assert.Equal(0, Assert.IsType<SolidColorBrush>(primary.BorderBrush).Color.A);

        // 键盘导航取焦：焦点环出现（alpha > 0 即 FocusRing 生效）。
        PressTab(host.Window);
        var focused = Assert.IsType<Button>(FocusedElement(host.Window));
        Assert.Contains(":focus-visible", focused.Classes);
        var ringBrush = Assert.IsType<SolidColorBrush>(focused.BorderBrush);
        Assert.True(
            ringBrush.Color.A > 0,
            $"键盘导航时必须画出焦点环。classes=[{string.Join(",", focused.Classes)}] " +
            $"brush={ringBrush.Color} opacity={ringBrush.Opacity} border={focused.BorderThickness}");
    }

    /// <summary>
    /// ADR-040（修订）：框架默认焦点矩形（FocusAdorner）被全局清掉，焦点可见性交给
    /// `:focus-visible` 上的 FocusRing；对一个不在对话框里的普通按钮断言同一套规律。
    /// </summary>
    [AvaloniaFact]
    public void FocusVisual_FollowsKeyboardNavigationOnly()
    {
        var first = new Button { Content = "first" };
        var second = new Button { Content = "second" };
        var window = new Window
        {
            Content = new StackPanel { Children = { first, second } },
            Width = 320,
            Height = 200
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        first.Focus(NavigationMethod.Unspecified);
        Dispatcher.UIThread.RunJobs();
        Assert.True(first.IsFocused);
        Assert.Null(first.FocusAdorner);
        Assert.DoesNotContain(":focus-visible", first.Classes);

        PressTab(window);
        Assert.True(second.IsFocused);
        Assert.Contains(":focus-visible", second.Classes);
        window.Close();
    }

    /// <summary>
    /// ADR-040（修订二）：焦点环不能改变按钮几何 —— 环用 BorderThickness 画，厚度必须常驻预留，
    /// 否则聚焦那一刻按钮变宽、右对齐的动作带整体位移（用户报告的正是这一条）。
    /// 这里按「聚焦前后逐个按钮的宽度与横坐标完全相等」来钉。
    /// </summary>
    [AvaloniaFact]
    public void DialogActionButtons_KeepGeometryWhenTheFocusRingAppears()
    {
        var confirm = new RecorderCommand();
        using var host = CreateHost(new ConfirmDialog
        {
            IsOpen = true,
            ConfirmCommand = confirm,
            ConfirmText = "Repair",
            CancelText = "Cancel"
        });

        var before = ActionButtons(host.Dialog)
            .Select(button => (button.Name, button.Bounds))
            .ToArray();

        // 键盘导航取焦：焦点环出现（颜色变），但几何不许动。
        PressTab(host.Window);

        var focused = Assert.IsType<Button>(FocusedElement(host.Window));
        Assert.Contains(":focus-visible", focused.Classes);

        foreach (var button in ActionButtons(host.Dialog))
        {
            var original = before.Single(entry => entry.Name == button.Name).Bounds;
            Assert.Equal(original.Width, button.Bounds.Width);
            Assert.Equal(original.X, button.Bounds.X);
            Assert.Equal(original.Height, button.Bounds.Height);
        }
    }

    /// <summary>
    /// 真实按键路径：Enter 先到达聚焦中的默认按钮（按钮自己会 Click 并标记 handled），
    /// 不该再冒泡到对话框的默认动作处理 —— 否则一次 Enter 会执行两遍命令。
    /// </summary>
    [AvaloniaFact]
    public void NonDangerConfirm_WhenTheDefaultButtonHasFocus_EnterExecutesExactlyOnce()
    {
        var confirm = new RecorderCommand();
        using var host = CreateHost(new ConfirmDialog
        {
            IsOpen = true,
            ConfirmCommand = confirm,
            ConfirmText = "Repair",
            CancelText = "Cancel"
        });

        Assert.True(FindButton(host.Dialog, "PrimaryActionButton").IsFocused);
        PressEnter(host.Window);

        Assert.Equal(1, confirm.ExecuteCount);
    }

    private static void PressEnter(Window window)
    {
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, string.Empty);
        window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, string.Empty);
        Dispatcher.UIThread.RunJobs();
    }

    private static void PressTab(Window window)
    {
        window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, string.Empty);
        window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, string.Empty);
        Dispatcher.UIThread.RunJobs();
    }

    private static void RaiseEnter(ConfirmDialog dialog)
    {
        dialog.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Dispatcher.UIThread.RunJobs();
    }

    private static IReadOnlyList<Button> ActionButtons(ConfirmDialog dialog) =>
        dialog
            .GetVisualDescendants()
            .OfType<StackPanel>()
            .Single(panel => panel.Classes.Contains("confirm-actions"))
            .Children
            .OfType<Button>()
            .ToList();

    private static Button FindButton(ConfirmDialog dialog, string name) =>
        dialog.GetVisualDescendants().OfType<Button>().Single(button => button.Name == name);

    private static object? FocusedElement(Window window) =>
        window.FocusManager?.GetFocusedElement();

    private static Host CreateHost(ConfirmDialog dialog)
    {
        var window = new Window { Content = dialog, Width = 640, Height = 480 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        dialog.FocusInitialAction();
        Dispatcher.UIThread.RunJobs();
        return new Host(window, dialog);
    }

    private sealed class Host(Window window, ConfirmDialog dialog) : IDisposable
    {
        public Window Window { get; } = window;

        public ConfirmDialog Dialog { get; } = dialog;

        public void Dispose() => Window.Close();
    }
}
