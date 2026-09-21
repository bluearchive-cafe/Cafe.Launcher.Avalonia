using System;
using System.Threading;

namespace Cafe.Launcher.Avalonia.Services;

/// <summary>
/// 单实例启动桥:把「二次实例转发 → 首实例唤起」的握手收敛为一个调用序列 ——
/// 信号端点先于互斥量创建(转发永远命中一个监听端点),互斥量败者转发
/// <c>--launch-game</c> 并唤起显示信号,胜者把已绑定的端点交给应用。
/// 此前以注释承载的顺序契约,现在以构造顺序固化在本模块内。
/// 传输层为双适配器:Windows 命名事件 / Unix 本地套接字
/// (<see cref="CrossProcessLaunchSignal"/>);launch-game 与 show-window
/// 两个信号共用同一传输,仅名字不同。
/// </summary>
internal sealed class CrossProcessLaunchBridge : IDisposable
{
    private readonly string launchSignalName;
    private readonly string showSignalName;
    private readonly LauncherDataRoot dataRoot;
    private readonly CrossProcessLaunchSignal signal;
    private readonly CrossProcessLaunchSignal showSignal;
    private Mutex? mutex;
    private bool disposed;

    /// <summary>
    /// 创建桥并立即建立监听端点 —— 必须在探测单实例互斥量之前调用,
    /// 否则第二个实例的转发可能抢在监听端点存在之前落空。
    /// </summary>
    internal CrossProcessLaunchBridge(
        string launchSignalName,
        string showSignalName,
        LauncherDataRoot dataRoot)
    {
        ArgumentNullException.ThrowIfNull(dataRoot);
        this.launchSignalName = launchSignalName;
        this.showSignalName = showSignalName;
        this.dataRoot = dataRoot;
        signal = CrossProcessLaunchSignal.Listen(launchSignalName, dataRoot);
        showSignal = CrossProcessLaunchSignal.Listen(showSignalName, dataRoot);
    }

    /// <summary>Gets the launch-game endpoint owned by this process (valid only after winning).</summary>
    internal CrossProcessLaunchSignal Signal => signal;

    /// <summary>Gets the show-window endpoint owned by this process (valid only after winning).</summary>
    internal CrossProcessLaunchSignal ShowSignal => showSignal;

    /// <summary>
    /// Probes the single-instance mutex. Returns true when this process won and
    /// owns the endpoints bound; otherwise forwards <c>--launch-game</c> (when
    /// requested), raises the show-window signal, then returns false.
    /// The mutex is retained until <see cref="Dispose"/> so the process keeps
    /// single-instance ownership for its whole lifetime.
    /// </summary>
    internal bool TryEnterSingleInstance(string mutexName, string[] args)
    {
        mutex = new Mutex(true, mutexName, out var createdNew);
        if (createdNew)
        {
            signal.EnsureBound();
            showSignal.EnsureBound();
            return true;
        }

        if (Program.HasLaunchGameArgument(args))
        {
            CrossProcessLaunchSignal.Raise(launchSignalName, dataRoot);
        }

        CrossProcessLaunchSignal.Raise(showSignalName, dataRoot);
        return false;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        mutex?.Dispose();
        signal.Dispose();
        showSignal.Dispose();
    }
}
