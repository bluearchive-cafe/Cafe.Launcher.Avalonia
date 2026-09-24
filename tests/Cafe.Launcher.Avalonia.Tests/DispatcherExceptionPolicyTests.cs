using System;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Services.Diagnostics;

namespace Cafe.Launcher.Avalonia.Tests;

/// <summary>
/// 验证 UI 调度器边界分类：取消（含 TaskCanceledException）属于关停控制流而非崩溃，
/// 其余异常照旧按致命上报。防止 FreeDesktop 托盘图标释放时的取消信号再次触发崩溃报告。
/// </summary>
public sealed class DispatcherExceptionPolicyTests
{
    [Fact]
    public void IsFatal_Cancellation_IsNotFatal()
    {
        Assert.False(DispatcherExceptionPolicy.IsFatal(new OperationCanceledException()));
        Assert.False(DispatcherExceptionPolicy.IsFatal(new TaskCanceledException()));
    }

    [Fact]
    public void IsFatal_NonCancellationFault_IsFatal()
    {
        Assert.True(DispatcherExceptionPolicy.IsFatal(new InvalidOperationException()));
    }
}
