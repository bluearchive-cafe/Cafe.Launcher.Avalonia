using System;
using System.Threading;

namespace Cafe.Launcher.Avalonia.Services;

/// <summary>
/// 单实例启动桥:把「二次实例转发 → 首实例唤起」的握手收敛为一个调用序列 ——
/// 信号端点先于所有权判定创建(转发永远命中一个监听端点),互斥量败者转发
/// <c>--launch-game</c> 并唤起显示信号,胜者把已绑定的端点交给应用。
/// 所有权判定分平台:Windows 用命名互斥量;Unix 上 .NET 的 <c>Local\</c>
/// 命名空间按 POSIX 会话隔离(见 ADR-034),改用数据根内锁套接字的内核原子
/// 绑定(<see cref="CrossProcessLaunchSignal.TryBindExclusive"/>)。
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
    private readonly CrossProcessLaunchSignal? lockSignal;
    private Mutex? mutex;
    private bool disposed;

    /// <summary>
    /// 创建桥并立即建立监听端点 —— 必须在判定单实例所有权之前调用,
    /// 否则第二个实例的转发可能抢在监听端点存在之前落空。
    /// </summary>
    internal CrossProcessLaunchBridge(
        string launchSignalName,
        string showSignalName,
        string lockSignalName,
        LauncherDataRoot dataRoot)
    {
        ArgumentNullException.ThrowIfNull(dataRoot);
        this.launchSignalName = launchSignalName;
        this.showSignalName = showSignalName;
        this.dataRoot = dataRoot;
        signal = CrossProcessLaunchSignal.Listen(launchSignalName, dataRoot);
        showSignal = CrossProcessLaunchSignal.Listen(showSignalName, dataRoot);
        lockSignal = OperatingSystem.IsWindows()
            ? null
            : CrossProcessLaunchSignal.ListenAt(lockSignalName, dataRoot.Root);
    }

    /// <summary>Gets the launch-game endpoint owned by this process (valid only after winning).</summary>
    internal CrossProcessLaunchSignal Signal => signal;

    /// <summary>Gets the show-window endpoint owned by this process (valid only after winning).</summary>
    internal CrossProcessLaunchSignal ShowSignal => showSignal;

    /// <summary>
    /// Probes the single-instance gate. Returns true when this process won and
    /// owns the endpoints bound; otherwise forwards <c>--launch-game</c> (when
    /// requested), raises the show-window signal, then returns false.
    /// The gate is retained until <see cref="Dispose"/> so the process keeps
    /// single-instance ownership for its whole lifetime.
    /// </summary>
    internal bool TryEnterSingleInstance(string mutexName, string[] args)
    {
        if (!AcquireOwnership(mutexName))
        {
            if (Program.HasLaunchGameArgument(args))
            {
                CrossProcessLaunchSignal.Raise(launchSignalName, dataRoot);
            }

            CrossProcessLaunchSignal.Raise(showSignalName, dataRoot);
            return false;
        }

        signal.EnsureBound();
        showSignal.EnsureBound();
        return true;
    }

    /// <summary>
    /// Windows 以命名互斥量定胜负；Unix 以锁套接字绑定定胜负。
    /// </summary>
    private bool AcquireOwnership(string mutexName)
    {
        if (lockSignal is null)
        {
            mutex = new Mutex(true, mutexName, out var createdNew);
            return createdNew;
        }

        return lockSignal.TryBindExclusive();
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
        lockSignal?.Dispose();
    }
}
