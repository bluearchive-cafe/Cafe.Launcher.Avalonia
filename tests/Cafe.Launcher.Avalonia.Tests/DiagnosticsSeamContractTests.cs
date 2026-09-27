using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Core.Services.Diagnostics;
using Cafe.Launcher.Core.Services.Diagnostics;

namespace Cafe.Launcher.Avalonia.Tests;

/// <summary>
/// Core 诊断接缝的契约：表现层的 <see cref="LocalDiagnostics"/> 是唯一实现，接口成员集合是
/// 后端的可见词汇表——扩成员必须同时补实现，缩成员会让 Core 里的调用点编译不过。
/// </summary>
public sealed class DiagnosticsSeamContractTests
{
    [Fact]
    public void LocalDiagnostics_ImplementsTheCoreDiagnosticsSeam()
    {
        var seam = typeof(ILauncherDiagnostics);

        Assert.True(seam.IsAssignableFrom(typeof(LocalDiagnostics)));

        // 钉住接缝的**完整**成员集合：接口是 Core 与表现层之间唯一的诊断词汇表，
        // 加成员必须是刻意决定（属性访问器也会出现在 GetMethods 里，故一并列出）。
        var members = seam.GetMethods().Select(method => method.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        Assert.Equal(
            [
                "DebugAsync",
                "ErrorAsync",
                "ErrorAsync",
                "FatalAsync",
                "LogMessage",
                "MessageAsync",
                "SetMinimumLevel",
                "VerboseAsync",
                "WarningAsync",
                "get_LogFilePath",
                "get_MinimumLevel"
            ],
            members);
    }

    [Fact]
    public void LogMessage_ForwardsToTheSharedLoggerWithoutThrowing()
    {
        // 同步入口是给「调用点无法等待」的路径用的（系统代理读取、缓存告警）；这里只钉住
        // 转发本身不会抛——共享日志器未注册时它必须是安静的 no-op。
        ILauncherDiagnostics diagnostics = new LocalDiagnostics();

        var exception = Record.Exception(
            () => diagnostics.LogMessage(LogEntrySeverity.Warn, "DiagnosticsSeam", "forwarding smoke"));

        Assert.Null(exception);
    }

    [Fact]
    public async Task MessageAsync_IsReachableThroughTheSeam()
    {
        ILauncherDiagnostics diagnostics = new LocalDiagnostics();

        await diagnostics.MessageAsync("DiagnosticsSeam", "message smoke", CancellationToken.None);
    }
}
