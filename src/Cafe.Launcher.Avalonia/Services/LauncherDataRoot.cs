using System;
using System.IO;
using Cafe.Launcher.Avalonia.Constants;

namespace Cafe.Launcher.Avalonia.Services;

/// <summary>
/// 启动器私有数据的根，以及根内知名路径的唯一出处。
/// </summary>
/// <remarks>
/// 「数据放哪」原先是一个每个模块各自读一次的进程级静态，15 处消费点各自长出一段
/// 测试专用构造器来绕开它。现在进程根只在少数几处解析——组合根一次，以及 ADR-019
/// 保护的 pre-DI 路径（崩溃日志器、崩溃窗口、首启探测、单实例信号）各一次——其余模块
/// 一律接收本类型，测试因此只在一个接缝上换根。允许解析进程根的文件由
/// <c>TestUserDataIsolationTests.ProcessRootResolution_IsConfinedToDeclaredPreDiSites</c>
/// 钉住。
/// </remarks>
public sealed class LauncherDataRoot
{
    /// <summary>
    /// 测试用覆盖：置位后所有走 <see cref="ForCurrentProcess"/> 的路径都落在该目录。
    /// 层级必须保持浅短——Unix 上单实例套接字路径由它派生（见 <c>TestUserDataIsolationTests</c>）。
    /// </summary>
    internal const string TestOverrideEnvironmentVariable = "CAFE_LAUNCHER_TEST_USER_DATA_DIRECTORY";

    /// <summary>图片缓存目录名。</summary>
    public const string ImageCacheFolderName = "image-cache";

    /// <summary>崩溃快照目录名——崩溃进程与主进程共享的落点。</summary>
    public const string CrashReportsFolderName = "CrashReports";

    public LauncherDataRoot(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Root = Path.GetFullPath(root);
    }

    /// <summary>数据根的绝对路径。</summary>
    public string Root { get; }

    /// <summary><c>settings.json</c>：设置与开关的唯一持久化落点。</summary>
    public string SettingsPath => Path.Combine(Root, GamePaths.LauncherSettingsFileName);

    /// <summary><c>download_state.json</c>：可续传下载的检查点。</summary>
    public string DownloadStatePath => Path.Combine(Root, GamePaths.DownloadStateFileName);

    /// <summary><c>shown_notices.json</c>：已读公告的指纹集合。</summary>
    public string NoticeStatePath => Path.Combine(Root, GamePaths.NoticeStateFileName);

    /// <summary>图片缓存目录（可整体删除，缺失时按需重建）。</summary>
    public string ImageCacheDirectory => Path.Combine(Root, ImageCacheFolderName);

    /// <summary>崩溃快照的默认目录。</summary>
    public string CrashReportsDirectory => Path.Combine(Root, CrashReportsFolderName);

    /// <summary>日志导出的默认目录。</summary>
    public string LogExportDirectory => Path.Combine(Root, LauncherConstants.LogExportFolderName);

    /// <summary>
    /// 按进程解析数据根：测试覆盖优先，否则本机 LocalApplicationData 下的产品目录。
    /// </summary>
    internal static string ResolveProcessRoot(string? testOverride, string localApplicationData)
    {
        if (!string.IsNullOrWhiteSpace(testOverride))
        {
            return Path.GetFullPath(testOverride);
        }

        return Path.Combine(localApplicationData, LauncherConstants.ProductName);
    }

    /// <summary>
    /// 当前进程的数据根。只有组合根与 ADR-019 保护的 pre-DI 路径可以调用；
    /// 其余模块接收注入的实例。
    /// </summary>
    internal static LauncherDataRoot ForCurrentProcess() => new(ResolveProcessRoot(
        Environment.GetEnvironmentVariable(TestOverrideEnvironmentVariable),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)));
}
