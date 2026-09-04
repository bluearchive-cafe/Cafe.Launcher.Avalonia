using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Testing;

/// <summary>
/// <see cref="IFileDownloadService"/> 的共享测试替身，由两个测试工程通过 csproj Link
/// 共用（与 TestUserDataIsolation.cs 同一机制）。默认空操作；通过构造委托注入下载行为，
/// 并按调用顺序记录每次请求参数。需要并发计数、分块写盘等更特殊行为的测试可在委托里
/// 用闭包自行实现，确实无法无损表达时再保留文件内的专用 fake。
/// </summary>
internal sealed class StubFileDownloadService : IFileDownloadService
{
    private readonly object gate = new();
    private readonly Func<FileDownloadRequest, FileDownloadOperationControl, CancellationToken, Task>? downloadAsync;

    /// <summary>传入委托即自定义下载行为；不传则为空操作（只记录调用）。</summary>
    public StubFileDownloadService(
        Func<FileDownloadRequest, FileDownloadOperationControl, CancellationToken, Task>? downloadAsync = null)
    {
        this.downloadAsync = downloadAsync;
    }

    /// <summary>Gets 按调用顺序记录的请求参数；并发调用下由锁保证读取一致。</summary>
    public List<FileDownloadRequest> Requests { get; } = [];

    /// <summary>Gets 已收到的调用次数。</summary>
    public int InvocationCount
    {
        get
        {
            lock (gate)
            {
                return Requests.Count;
            }
        }
    }

    /// <inheritdoc />
    public async Task DownloadAsync(
        FileDownloadRequest request,
        FileDownloadOperationControl control,
        CancellationToken cancellationToken)
    {
        lock (gate)
        {
            Requests.Add(request);
        }

        if (downloadAsync is not null)
        {
            await downloadAsync(request, control, cancellationToken);
        }
    }
}
