using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Services.Diagnostics;

namespace Cafe.Launcher.Avalonia.Services;

/// <summary>
/// 跨进程单向信号，用于把第二个启动器实例的 <c>--launch-game</c> 请求转发给
/// 正在运行的第一个实例（单实例互斥量由 <see cref="Cafe.Launcher.Avalonia.Program"/> 持有）。
/// Windows 使用命名 AutoReset <see cref="EventWaitHandle"/>（Win32 内核对象）；
/// Unix 上 .NET 不提供命名事件（带 name 的 EventWaitHandle 直接抛出
/// <see cref="PlatformNotSupportedException"/>），因此以本机 Unix 域套接字实现同一契约：
/// 第一个实例持有监听端点，后续实例连接后写入一个字节即完成一次“设置信号”。
/// </summary>
internal sealed class CrossProcessLaunchSignal : IDisposable
{
    /// <summary>绑定失败后重试的间隔（毫秒）。</summary>
    private const int PollIntervalMilliseconds = 25;

    /// <summary>
    /// “旧进程仍在监听”或“套接字资源不可用”时的绑定等待上限（毫秒），
    /// 超过后放弃绑定并降级为不可转发（不影响本进程其余功能）。
    /// </summary>
    private const int BindWaitTimeoutMilliseconds = 2000;

    /// <summary>发起转发的进程等待监听端点出现的重试次数与间隔（毫秒）。</summary>
    private const int RaiseRetryAttempts = 6;
    private const int RaiseRetryIntervalMilliseconds = 25;

    /// <summary>Dispose 时等待接收循环退出的时间上限（毫秒）。</summary>
    private const int AcceptLoopShutdownWaitMilliseconds = 300;

    private static readonly byte SignalByte = 1;

    private readonly string signalName;
    private readonly string? socketDirectory;
    private readonly EventWaitHandle? windowsEvent;
    private AutoResetEvent? unixPending;
    private Socket? unixListener;
    private readonly CancellationTokenSource shutdownCts = new();
    private Task? acceptLoop;
    private bool disposed;

    private CrossProcessLaunchSignal(string signalName, string? socketDirectory)
    {
        this.signalName = signalName;
        if (socketDirectory is null)
        {
            // create-or-open：第二个实例在第一个实例已建好事件时会打开同一个内核对象，
            // 因此“先建事件、后抢互斥量”的顺序保证转发请求总能命中一个监听端点。
            windowsEvent = new EventWaitHandle(false, EventResetMode.AutoReset, signalName);
        }
        else
        {
            this.socketDirectory = socketDirectory;
        }
    }

    /// <summary>
    /// 创建第一个实例的监听端点，必须在拿到单实例互斥量之前调用：
    /// Windows 上这保证命名事件先于互斥量存在，转发永不落空；
    /// Unix 上只解析套接字路径，实际绑定由互斥量获胜者调用 <see cref="EnsureBound"/>。
    /// </summary>
    internal static CrossProcessLaunchSignal Listen(string signalName) =>
        OperatingSystem.IsWindows()
            ? new CrossProcessLaunchSignal(signalName, socketDirectory: null)
            : ListenAt(signalName, LauncherUserDataDirectory.Root);

    /// <summary>
    /// 测试缝：始终使用 Unix 域套接字传输（Windows 10 及以上同样支持 AF_UNIX，
    /// 因此这套套接字协议可以在 CI 上验证）。
    /// </summary>
    internal static CrossProcessLaunchSignal ListenAt(string signalName, string socketDirectory) =>
        new CrossProcessLaunchSignal(signalName, socketDirectory);

    /// <summary>
    /// Windows 上是无操作（命名事件已随 <see cref="Listen"/> 创建）；
    /// Unix 上绑定本机套接字并启动接收循环，只应由互斥量获胜者调用。
    /// 与上一个进程退场的竞态（以及崩溃残留的陈旧套接字文件）通过“活性探测 + 短暂重试”收敛，
    /// 没有任何一个存活进程会被误删套接字。
    /// </summary>
    internal void EnsureBound()
    {
        if (disposed || socketDirectory is null || unixListener is not null)
        {
            return;
        }

        try
        {
            BindUnixSocket(GetSocketFilePath(socketDirectory, signalName));
        }
        catch (Exception ex)
        {
            // 绑定失败不应影响启动器主流程（仅失去 --launch-game 转发能力）。
            LocalDiagnostics.LogSync(LogEntrySeverity.Warn, "CrossProcess", $"bind failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 在截止时间前完成绑定——可重试以跨越旧进程退场与残留套接字文件两种状态。
    /// </summary>
    private void BindUnixSocket(string socketPath)
    {
        Directory.CreateDirectory(socketDirectory!);
        var deadline = Environment.TickCount64 + BindWaitTimeoutMilliseconds;
        while (true)
        {
            if (TryBind(socketPath) is { } socket)
            {
                unixListener = socket;
                unixPending = new AutoResetEvent(false);
                acceptLoop = Task.Run(AcceptLoop);
                return;
            }

            if (IsLiveListener(socketPath))
            {
                // 仍有进程在监听（大概率是刚释放互斥量的旧进程正在退出）：等它关闭套接字，
                // 再由下方的“陈旧文件”分支接管重绑。
                if (Environment.TickCount64 >= deadline)
                {
                    break;
                }

                Thread.Sleep(PollIntervalMilliseconds);
                continue;
            }

            // 文件背后没有活着的监听者：要么是上次进程留下的残留文件，要么是路径不可用
            // （如超长路径）。删除残留后重试；到达截止时间仍未成功则放弃绑定。
            try
            {
                File.Delete(socketPath);
            }
            catch (IOException)
            {
                // 文件不存在或权限问题——交给下一次重试或超时退出。
            }

            Thread.Sleep(PollIntervalMilliseconds);
        }

        LocalDiagnostics.LogSync(LogEntrySeverity.Warn, "CrossProcess", $"could not bind Unix socket '{socketPath}' in time; --launch-game forwarding is unavailable for this process.");
    }

    /// <summary>
    /// 轮询一次信号：存在未消费的信号时返回 true 并复位（AutoReset 语义），
    /// 否则在时限内等待后返回 false。与 Windows 命名事件行为一致。
    /// </summary>
    internal bool WaitOne(TimeSpan timeout)
    {
        if (windowsEvent is not null)
        {
            return windowsEvent.WaitOne(timeout);
        }

        return unixPending?.WaitOne(timeout) ?? false;
    }

    /// <summary>
    /// 由非第一个实例调用：请求第一个实例拉起游戏。尽力而为，绝不抛出。
    /// </summary>
    internal static void Raise(string signalName)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                // create-or-open 与 Listen 相同：即使与第一个实例启动竞速，
                // 也会落在同一个内核事件对象上。
                using var signal = new EventWaitHandle(false, EventResetMode.AutoReset, signalName);
                signal.Set();
            }
            catch
            {
                // 第一个实例可能尚未创建事件——忽略。
            }

            return;
        }

        RaiseAt(signalName, LauncherUserDataDirectory.Root);
    }

    /// <summary>
    /// 测试缝：始终使用 Unix 域套接字发送信号，短重试以覆盖
    /// “第一个实例已抢到互斥量但尚未绑定套接字”的微小窗口。
    /// </summary>
    internal static void RaiseAt(string signalName, string socketDirectory)
    {
        var socketPath = GetSocketFilePath(socketDirectory, signalName);
        for (var attempt = 0; attempt < RaiseRetryAttempts; attempt++)
        {
            try
            {
                using var socket = Connect(socketPath);
                socket.Send([SignalByte]);
                return;
            }
            catch
            {
                // 监听端点尚未绑定（旧进程正在退出或新进程尚未启动）——短暂重试后放弃。
                Thread.Sleep(RaiseRetryIntervalMilliseconds);
            }
        }
    }

    /// <summary>
    /// 由信号名推导稳定的套接字文件路径。文件名保持短小：
    /// Unix 域套接字路径总长必须显著低于 108 字节，而用户名路径可能很长。
    /// </summary>
    internal static string GetSocketFilePath(string socketDirectory, string signalName)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(signalName));
        var suffix = Convert.ToHexString(hash.AsSpan(0, 6)).ToLowerInvariant();
        return Path.Combine(socketDirectory, $"cl-signal-{suffix}.sock");
    }

    private void AcceptLoop()
    {
        var listener = unixListener;
        var pending = unixPending;
        if (listener is null || pending is null)
        {
            return;
        }

        var buffer = new byte[16];
        while (!shutdownCts.IsCancellationRequested)
        {
            Socket client;
            try
            {
                client = listener.Accept();
            }
            catch (SocketException ex) when (ex.SocketErrorCode is SocketError.OperationAborted
                or SocketError.Interrupted or SocketError.InvalidArgument)
            {
                break; // 监听套接字已被 Dispose 关闭。
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException)
            {
                Thread.Sleep(PollIntervalMilliseconds);
                continue;
            }

            using (client)
            {
                client.ReceiveTimeout = 2000;
                try
                {
                    if (client.Receive(buffer) > 0 && !shutdownCts.IsCancellationRequested)
                    {
                        // 与 Windows 命名 AutoReset 事件相同：短时间内多次 Raise 合并为一次唤醒。
                        pending.Set();
                    }
                }
                catch (SocketException)
                {
                    // 客户端在发送前就断开——忽略。
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
            }
        }
    }

    private Socket? TryBind(string socketPath)
    {
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            socket.Bind(new UnixDomainSocketEndPoint(socketPath));
            socket.Listen(32);
            return socket;
        }
        catch (SocketException)
        {
            socket.Dispose();
            return null;
        }
        catch (IOException)
        {
            socket.Dispose();
            return null;
        }
    }

    /// <summary>
    /// 探测套接字文件背后是否有活着的监听者：能连上即有（连接会被接收循环读取并立即关闭，无副作用）。
    /// </summary>
    private static bool IsLiveListener(string socketPath)
    {
        try
        {
            using var probe = Connect(socketPath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static Socket Connect(string socketPath)
    {
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            socket.Connect(new UnixDomainSocketEndPoint(socketPath));
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        shutdownCts.Cancel();
        // 先关闭监听套接字以中止阻塞的 Accept，再等待接收循环退出。
        unixListener?.Dispose();
        try
        {
            acceptLoop?.Wait(TimeSpan.FromMilliseconds(AcceptLoopShutdownWaitMilliseconds));
        }
        catch
        {
            // 退出清理尽力而为。
        }

        unixPending?.Dispose();
        windowsEvent?.Dispose();
        shutdownCts.Dispose();
    }
}
