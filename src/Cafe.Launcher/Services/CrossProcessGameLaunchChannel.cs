using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Core.Services;
using Cafe.Launcher.Core.Services.Diagnostics;

namespace Cafe.Launcher.Services;

/// <summary>
/// 单实例胜者拥有的同用户本机管道。端点身份由产品信号名、既有数据根和用户共同派生，
/// 不跨用户或跨数据根转发；每连接只接受一条有界固定游戏请求。
/// </summary>
internal sealed class CrossProcessGameLaunchChannel : IDisposable
{
    internal const int MaxPendingRequests = 16;
    private readonly string pipeName;
    private readonly object gate = new();
    private readonly Queue<CrossProcessGameLaunchRequest> requests = new();
    private readonly AutoResetEvent pending = new(false);
    private readonly CancellationTokenSource shutdown = new();
    private NamedPipeServerStream? server;
    private Task? receiveLoop;
    private bool disposed;

    internal CrossProcessGameLaunchChannel(string signalName, LauncherDataRoot dataRoot)
    {
        pipeName = GetPipeName(signalName, dataRoot);
    }

    internal static string GetPipeName(string signalName, LauncherDataRoot dataRoot)
    {
        ArgumentNullException.ThrowIfNull(dataRoot);
        var identity = signalName + "\n" + dataRoot.Root + "\n" + Environment.UserName;
        var suffix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)).AsSpan(0, 12));
        // .NET's Unix pipe transport accepts an absolute socket path. Keep it in the
        // same root as the existing ownership socket, rather than a session temp path.
        return OperatingSystem.IsWindows() ? "CafeLauncherGame-" + suffix : Path.Combine(dataRoot.Root, "cl-launch-" + suffix);
    }

    /// <summary>仅在既有单实例所有权确认之后创建监听端点。</summary>
    internal void EnsureBound()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (receiveLoop is not null)
            {
                return;
            }

            try
            {
                server = CreateServer();
                receiveLoop = ReceiveAsync();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                LauncherLog.LogSync(LogEntrySeverity.Warn, "CrossProcess", $"Launch request listener unavailable: {exception.Message}");
            }
        }
    }

    internal bool WaitOne(TimeSpan timeout) => pending.WaitOne(timeout);

    internal bool TryTake(out CrossProcessGameLaunchRequest? request)
    {
        lock (gate)
        {
            if (requests.Count == 0)
            {
                request = null;
                return false;
            }

            request = requests.Dequeue();
            return true;
        }
    }

    /// <summary>只有已确认败给既有实例的进程才发送；连接与写入各有有界预算。</summary>
    internal static bool Send(string signalName, LauncherDataRoot dataRoot, CrossProcessGameLaunchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            using var client = new NamedPipeClientStream(".", GetPipeName(signalName, dataRoot), PipeDirection.Out,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            client.ConnectAsync(budget.Token).GetAwaiter().GetResult();
            client.WriteAsync(request.Encode(), budget.Token).AsTask().GetAwaiter().GetResult();
            client.FlushAsync(budget.Token).GetAwaiter().GetResult();
            return true;
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or OperationCanceledException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private NamedPipeServerStream CreateServer()
    {
        var pipe = new NamedPipeServerStream(pipeName, PipeDirection.In, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly,
            CrossProcessGameLaunchRequest.MaxPayloadBytes + 1, CrossProcessGameLaunchRequest.MaxPayloadBytes + 1);
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(pipeName, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            return pipe;
        }
        catch
        {
            pipe.Dispose();
            throw;
        }
    }

    private async Task ReceiveAsync()
    {
        while (!shutdown.IsCancellationRequested)
        {
            var current = server;
            if (current is null)
            {
                return;
            }

            try
            {
                await current.WaitForConnectionAsync(shutdown.Token).ConfigureAwait(false);
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
                budget.CancelAfter(TimeSpan.FromSeconds(2));
                var buffer = new byte[CrossProcessGameLaunchRequest.MaxPayloadBytes + 1];
                var length = 0;
                while (length < buffer.Length)
                {
                    var read = await current.ReadAsync(buffer.AsMemory(length), budget.Token).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    length += read;
                }

                if (CrossProcessGameLaunchRequest.TryDecode(buffer.AsMemory(0, length), out var request))
                {
                    lock (gate)
                    {
                        if (!disposed && requests.Count < MaxPendingRequests)
                        {
                            requests.Enqueue(request!);
                            pending.Set();
                        }
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException)
            {
                // 关闭、断连和超预算连接均不构成可执行请求。
            }
            finally
            {
                current.Dispose();
            }

            lock (gate)
            {
                if (disposed)
                {
                    return;
                }

                try
                {
                    server = CreateServer();
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    LauncherLog.LogSync(LogEntrySeverity.Warn, "CrossProcess", $"Launch request listener stopped: {exception.Message}");
                    return;
                }
            }
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            shutdown.Cancel();
            server?.Dispose();
            requests.Clear();
        }

        try
        {
            receiveLoop?.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
            // 退出时只需确保管道关闭，已取消的接收循环不传播。
        }

        pending.Dispose();
        shutdown.Dispose();
    }
}
