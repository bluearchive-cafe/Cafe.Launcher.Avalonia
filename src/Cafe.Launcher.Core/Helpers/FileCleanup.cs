using System;
using System.IO;

namespace Cafe.Launcher.Core.Helpers;

/// <summary>
/// 清理路径上的尽力而为删除：文件被并发读取、只读或已不存在都算预期结果。
/// 容忍集合来自 <see cref="StorageFailure.IsRecoverable"/>，不在这里另写一份。
/// </summary>
internal static class FileCleanup
{
    public static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (StorageFailure.IsRecoverable(exception))
        {
        }
    }
}
