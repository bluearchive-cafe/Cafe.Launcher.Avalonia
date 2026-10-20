using System;
using System.IO;
using System.Text;
using System.Threading;
using Cafe.Launcher.Core.Constants;

namespace Cafe.Launcher.Core.Services;

/// <summary>
/// 启动器私有数据的根，以及根内知名路径的唯一出处。
/// </summary>
/// <remarks>
/// <para>
/// 「数据放哪」原先是一个每个模块各自读一次的进程级静态，15 处消费点各自长出一段
/// 测试专用构造器来绕开它。现在进程根只在少数几处解析——组合根一次，以及 ADR-019
/// 保护的 pre-DI 路径（崩溃日志器、崩溃窗口、首启探测、单实例信号）各一次——其余模块
/// 一律接收本类型，测试因此只在一个接缝上换根。允许解析进程根的文件由
/// <c>TestUserDataIsolationTests.ProcessRootResolution_IsConfinedToDeclaredPreDiSites</c>
/// 钉住。
/// </para>
/// <para>
/// 所有平台都只有一个根：Windows/macOS 是 <c>%LOCALAPPDATA%\Cafe Launcher</c>，
/// Linux 是 <c>$XDG_DATA_HOME/cafe-launcher</c>（未设置时 <c>~/.local/share/cafe-launcher</c>）。
/// Unix 目录名取产品名的小写连字符形式，与旧版兼容前缀助手使用的拼写保持一致。
/// ADR-048 曾按 XDG 的 data/config/state/cache 把根拆成四个，因复杂度高于收益，
/// 在发布前由 ADR-049 收回；本类型因此只保留一个根。
/// </para>
/// </remarks>
public sealed class LauncherDataRoot
{
    /// <summary>
    /// 测试用覆盖：置位后所有走 <see cref="ForCurrentProcess"/> 的路径都落在这个目录。
    /// 层级必须保持浅短——Unix 上单实例套接字路径由它派生（见 <c>TestUserDataIsolationTests</c>）。
    /// </summary>
    internal const string TestOverrideEnvironmentVariable = "CAFE_LAUNCHER_TEST_USER_DATA_DIRECTORY";

    /// <summary>图片缓存目录名。</summary>
    public const string ImageCacheFolderName = "image-cache";

    /// <summary>受管兼容环境目录名（位于 <see cref="Root"/> 下）。</summary>
    public const string CompatibilityFolderName = "compatibility";

    /// <summary>崩溃快照目录名——崩溃进程与主进程共享的落点。</summary>
    public const string CrashReportsFolderName = "CrashReports";

    /// <summary>启动器自更新下载的暂存目录名（可整体删除，缺失时按需重建）。</summary>
    public const string UpdateFolderName = "update";

    private static int migrationAttempted;

    /// <summary>
    /// 单一根构造：所有知名路径都从这个根派生。生产路径由 <see cref="ForCurrentProcess"/>
    /// 按平台解析，测试用临时目录构造它来保持隔离。
    /// </summary>
    public LauncherDataRoot(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Root = Path.GetFullPath(root);
    }

    /// <summary>数据根：设置、日志、缓存、检查点与受管兼容环境都放在这里。</summary>
    public string Root { get; }

    /// <summary><c>settings.json</c>：设置与开关的唯一持久化落点。</summary>
    public string SettingsPath => Path.Combine(Root, LauncherPaths.LauncherSettingsFileName);

    /// <summary><c>download_state.json</c>：可续传下载的检查点。</summary>
    public string DownloadStatePath => Path.Combine(Root, LauncherPaths.DownloadStateFileName);

    /// <summary><c>shown_notices.json</c>：已读公告的指纹集合。</summary>
    public string NoticeStatePath => Path.Combine(Root, LauncherPaths.NoticeStateFileName);

    /// <summary><c>runner_output.log</c>：最近一次运行器 stdout/stderr 的有上限捕获（每次启动覆盖）。</summary>
    public string RunnerOutputPath => Path.Combine(Root, LauncherPaths.RunnerOutputFileName);

    /// <summary><c>compatibility_environment.json</c>：最近一次兼容前缀环境预检报告（每次启动覆盖）。</summary>
    public string CompatibilityEnvironmentPath => Path.Combine(Root, LauncherPaths.CompatibilityEnvironmentFileName);

    /// <summary><c>prefix_metadata.json</c>：最近一次启动所用兼容前缀的元数据（每次启动覆盖）。</summary>
    public string PrefixMetadataPath => Path.Combine(Root, LauncherPaths.PrefixMetadataFileName);

    /// <summary>图片缓存目录（可整体删除，缺失时按需重建）。</summary>
    public string ImageCacheDirectory => Path.Combine(Root, ImageCacheFolderName);

    /// <summary>崩溃快照的默认目录。</summary>
    public string CrashReportsDirectory => Path.Combine(Root, CrashReportsFolderName);

    /// <summary>启动器自更新的下载暂存目录。</summary>
    public string UpdateDirectory => Path.Combine(Root, UpdateFolderName);

    /// <summary>日志导出的默认目录。</summary>
    public string LogExportDirectory => Path.Combine(Root, LauncherConstants.LogExportFolderName);

    /// <summary>统一日志目录（<c>unified.log</c> 及其轮转文件）。</summary>
    public string LogDirectory => Root;

    /// <summary>
    /// 在应用数据主目录下拼出数据根：测试覆盖优先，否则使用给定的产品目录名。
    /// Unix 的 <see cref="ResolveLayout"/> 传入的已经是小写目录名。
    /// </summary>
    /// <param name="productName">
    /// 产品名（数据根目录名），由调用方从
    /// <see cref="Models.LauncherProductProfile.ProductName"/> 取。做成参数而不是读常量：
    /// pre-DI 路径没有容器可用，产品身份必须在调用点显式写出来。
    /// </param>
    internal static string ResolveProcessRoot(
        string? testOverride,
        string dataHome,
        string productName)
    {
        if (!string.IsNullOrWhiteSpace(testOverride))
        {
            return Path.GetFullPath(testOverride);
        }

        return Path.Combine(dataHome, productName);
    }

    /// <summary>
    /// 解析数据根。测试覆盖优先；Unix 使用产品名的小写连字符目录名，其他平台沿用产品名原样。
    /// </summary>
    internal static LauncherDataRoot ResolveLayout(
        string productName,
        string? testOverride,
        bool isUnix,
        string dataHome)
    {
        var directoryName = isUnix ? ToUnixDirectoryName(productName) : productName;
        return new LauncherDataRoot(ResolveProcessRoot(testOverride, dataHome, directoryName));
    }

    /// <summary>
    /// Unix 目录名：小写、空白折叠成单个连字符。<c>Cafe Launcher</c> → <c>cafe-launcher</c>。
    /// 这是 Linux 上唯一的目录拼写，避免大小写敏感文件系统里出现两份用户数据。
    /// </summary>
    internal static string ToUnixDirectoryName(string productName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productName);
        var builder = new StringBuilder(productName.Length);
        var separatorPending = false;
        foreach (var character in productName.Trim())
        {
            if (char.IsWhiteSpace(character))
            {
                separatorPending = builder.Length > 0;
                continue;
            }

            if (separatorPending)
            {
                builder.Append('-');
                separatorPending = false;
            }

            builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }

    /// <summary>
    /// XDG 基目录：绝对路径才被采用；空值、相对路径按规范回退到 <paramref name="fallbackRelativePath"/>。
    /// 目前只用于 <c>XDG_DATA_HOME</c>；config/state/cache 不再单独解析（ADR-049）。
    /// </summary>
    internal static string ResolveXdgDirectory(
        string? configuredDirectory,
        string homeDirectory,
        string fallbackRelativePath)
    {
        if (!string.IsNullOrWhiteSpace(configuredDirectory) && Path.IsPathRooted(configuredDirectory))
        {
            return Path.GetFullPath(configuredDirectory);
        }

        var home = string.IsNullOrWhiteSpace(homeDirectory)
            ? Path.GetTempPath()
            : homeDirectory;
        return Path.Combine(home, fallbackRelativePath);
    }

    /// <summary>
    /// 当前进程的数据根。只有组合根与 ADR-019 保护的 pre-DI 路径可以调用；
    /// 其余模块接收注入的实例。
    /// </summary>
    public static LauncherDataRoot ForCurrentProcess(string productName)
    {
        var testOverride = Environment.GetEnvironmentVariable(TestOverrideEnvironmentVariable);
        var isUnix = !OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS();
        var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dataHome = isUnix
            ? ResolveXdgDirectory(Environment.GetEnvironmentVariable("XDG_DATA_HOME"), homeDirectory, ".local/share")
            : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var root = ResolveLayout(productName, testOverride, isUnix, dataHome);

        // 迁移只在 Unix 的生产路径上尝试一次。测试覆盖已经指向隔离目录，不走这里。
        if (isUnix && string.IsNullOrWhiteSpace(testOverride)
            && Interlocked.Exchange(ref migrationAttempted, 1) == 0)
        {
            LauncherDataRootMigration.MigrateLegacyRoot(
                root,
                Path.Combine(dataHome, productName));
        }

        return root;
    }
}
