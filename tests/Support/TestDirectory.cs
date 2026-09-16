using System;
using System.IO;
using System.Threading;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Testing;

/// <summary>临时目录删除失败时的处理方式。</summary>
public enum TestDirectoryCleanup
{
    /// <summary>重试耗尽即抛出：普通测试的残留目录是需要处理的维护问题，不该被静默吞掉。</summary>
    ReportFailure,

    /// <summary>
    /// 重试耗尽后留下目录并写出诊断：无头测试拆卸时句柄常延迟释放，
    /// 让清理问题把一个失败变成另一个失败会掩盖真正的断言结果。
    /// </summary>
    BestEffort
}

/// <summary>
/// 测试自有的临时目录：一处创建，一处删除，并顺带给出该目录的数据根。
/// </summary>
/// <remarks>
/// <para>路径保持浅短是硬约束：<c>LauncherDataRoot</c> 在 Unix 上会派生 Unix 域套接字路径，
/// <c>sockaddr_un</c> 的上限是 108 字节（含终止符 107，AUD-CI-002）。</para>
/// <para>删除必须发生在调用方放掉资源之后（先 dispose 容器/服务，再 dispose 本类型）；
/// 反序会让 Windows 上的延迟释放变成删除失败——这正是 <see cref="TestDirectoryCleanup.BestEffort"/>
/// 存在的理由，也只有无头拆卸该用它。</para>
/// </remarks>
public sealed class TestDirectory : IDisposable
{
    /// <summary>删除重试次数：覆盖 Windows 上句柄延迟释放的窗口，同时保证清理是有限的。</summary>
    private const int DeleteAttempts = 5;

    /// <summary>每轮重试的等待随尝试次数递增，总预算约 1 秒——覆盖句柄延迟释放，又不至于让清理拖住用例。</summary>
    private static TimeSpan DeleteRetryDelay(int attempt) =>
        TimeSpan.FromMilliseconds(100 * (attempt + 1));

    private readonly TestDirectoryCleanup cleanup;
    private bool disposed;

    private TestDirectory(string path, TestDirectoryCleanup cleanup)
    {
        Path = path;
        this.cleanup = cleanup;
        DataRoot = new LauncherDataRoot(path);
    }

    /// <summary>目录的绝对路径。</summary>
    public string Path { get; }

    /// <summary>以本目录为根的持久化落点，供各服务按数据根构造。</summary>
    public LauncherDataRoot DataRoot { get; }

    /// <summary>创建一个独立目录，创建时即落盘。</summary>
    public static TestDirectory Create(TestDirectoryCleanup cleanup = TestDirectoryCleanup.ReportFailure)
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return new TestDirectory(path, cleanup);
    }

    /// <summary>目录内路径，不创建。目录缺失时由消费方按需创建。</summary>
    public string Sub(string name) => System.IO.Path.Combine(Path, name);

    /// <summary>
    /// 目录的路径。隐式转换让「拥有目录」与「把路径交给服务、替身或断言」共用一个名字：
    /// 调用点按路径使用它时不必回写成 <c>tempDir.Path</c>。
    /// </summary>
    public static implicit operator string(TestDirectory directory) => directory.Path;

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        TryDelete();
    }

    private void TryDelete()
    {
        Exception? lastFailure = null;
        for (var attempt = 0; attempt < DeleteAttempts; attempt++)
        {
            try
            {
                if (!Directory.Exists(Path))
                {
                    return;
                }

                Directory.Delete(Path, recursive: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lastFailure = ex;
                Thread.Sleep(DeleteRetryDelay(attempt));
            }
        }

        var message =
            $"TestDirectory could not remove {Path} after {DeleteAttempts} attempts: {lastFailure?.Message}";
        if (cleanup == TestDirectoryCleanup.BestEffort)
        {
            // 尽力清理：残留一个临时目录不影响断言结论，但必须留下可追的诊断。
            Console.Error.WriteLine(message);
            return;
        }

        throw new IOException(message, lastFailure);
    }
}
