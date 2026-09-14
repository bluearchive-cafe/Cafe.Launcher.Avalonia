using System.IO;

namespace Cafe.Launcher.Avalonia.Tests;

/// <summary>
/// 测试用的数据根构造入口：把「数据放在哪个临时目录」收敛成一处。
/// </summary>
/// <remarks>
/// 生产侧 <see cref="Services.LauncherDataRoot"/> 由组合根解析一次并注入各模块；
/// 测试侧不再认识各服务的历史构造形状（有的历史上收文件路径、有的收目录），
/// 一律先在这里换根，再由根派生出该服务需要的知名路径。
/// </remarks>
internal static class TestDataRoot
{
    /// <summary>以给定目录为数据根。</summary>
    internal static Services.LauncherDataRoot ForDirectory(string directory) => new(directory);

    /// <summary>以给定文件的所在目录为数据根——用于仍按文件路径组织的既有用例。</summary>
    internal static Services.LauncherDataRoot ForFile(string filePath) =>
        new(Path.GetDirectoryName(Path.GetFullPath(filePath))!);

    /// <summary>
    /// 进程根：测试模块初始化器（<c>TestUserDataIsolation</c>）已把它指向按程序集隔离的
    /// 临时目录，因此这条路径不会碰到真实用户数据。用于原先走「服务自己解析默认根」
    /// 的那些用例，语义与改造前一致。
    /// </summary>
    internal static Services.LauncherDataRoot ForCurrentProcess() =>
        Services.LauncherDataRoot.ForCurrentProcess();
}
