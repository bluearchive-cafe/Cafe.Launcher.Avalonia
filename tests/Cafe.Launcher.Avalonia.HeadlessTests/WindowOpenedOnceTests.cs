using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Cafe.Launcher.Avalonia.Helpers;

namespace Cafe.Launcher.Avalonia.HeadlessTests;

public sealed class WindowOpenedOnceTests
{
    [AvaloniaFact]
    public void Subscribe_WhenWindowIsReshownAfterHide_RunsHandlerOnlyOnce()
    {
        // 从托盘恢复窗口走 Show()，而 Avalonia 在每次 Hide 之后的 Show 上都会重新触发
        // Opened（openedCount 断言钉住这一平台行为，防止测试退化为空转）：一次性启动
        // 处理器（--launch-game 首实例流程）必须只响应第一次，否则恢复窗口会重新执行
        // 启动命令、再次弹出「启动器已最小化到托盘」提示并把窗口再次最小化。
        var window = new Window();
        var openedCount = 0;
        var handledCount = 0;
        window.Opened += (_, _) => openedCount++;
        WindowOpenedOnce.Subscribe(window, (_, _) => handledCount++);

        window.Show();
        window.Hide();
        window.Show();
        window.Close();

        Assert.Equal(2, openedCount);
        Assert.Equal(1, handledCount);
    }

    [AvaloniaFact]
    public void Subscribe_WhenWindowOpensOnce_RunsHandlerExactlyOnce()
    {
        var window = new Window();
        var handledCount = 0;
        WindowOpenedOnce.Subscribe(window, (_, _) => handledCount++);

        window.Show();
        window.Close();

        Assert.Equal(1, handledCount);
    }
}
