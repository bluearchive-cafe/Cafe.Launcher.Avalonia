using Cafe.Launcher.Core.Models;
using Cafe.Launcher.UI.Features.Shell;

namespace Cafe.Launcher.Tests;

public sealed class WindowClosePolicyTests
{
    // 期望值用名字传递：internal 枚举不能出现在 public 测试方法的签名里。
    [Theory]
    [InlineData(CloseBehaviors.Minimize, false, true, nameof(WindowCloseAction.HideToTray))]
    [InlineData(CloseBehaviors.Minimize, false, false, nameof(WindowCloseAction.MinimizeToTaskbar))]
    [InlineData(CloseBehaviors.Exit, false, true, nameof(WindowCloseAction.Shutdown))]
    [InlineData(CloseBehaviors.Exit, false, false, nameof(WindowCloseAction.Shutdown))]
    [InlineData(CloseBehaviors.Minimize, true, true, nameof(WindowCloseAction.Ignore))]
    [InlineData(CloseBehaviors.Exit, true, false, nameof(WindowCloseAction.Ignore))]
    public void Decide_MapsCloseBehaviorUninstallGateAndTrayPresence(
        string closeBehavior,
        bool isUninstallExecuting,
        bool hasTray,
        string expected)
    {
        var action = WindowClosePolicy.Decide(closeBehavior, isUninstallExecuting, hasTray);

        Assert.Equal(expected, action.ToString());
    }

    [Fact]
    public void Decide_WithUnrecognizedCloseBehavior_TreatsItAsExit()
    {
        var action = WindowClosePolicy.Decide("unknown", isUninstallExecuting: false, hasTray: true);

        Assert.Equal(WindowCloseAction.Shutdown, action);
    }
}
