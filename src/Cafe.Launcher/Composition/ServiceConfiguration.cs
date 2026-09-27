using Microsoft.Extensions.DependencyInjection;
using Cafe.Launcher.Core.Composition;
using Cafe.Launcher.Constants;
using Cafe.Launcher.Services.Diagnostics;
using Cafe.Launcher.UI.Services.Diagnostics;
using Cafe.Launcher.UI.Composition;
using Cafe.Launcher.Core.Services;
using Cafe.Launcher.Core.Services.Diagnostics;

namespace Cafe.Launcher.Composition;

/// <summary>
/// 宿主的服务登记入口：解析数据根、登记 Core 与宿主自有服务，然后把表现层
/// 整体交给 UI 程序集（<c>AddLauncherPresentationServices</c> 与生命周期门面 <c>AddLauncherPresentation</c>）。
/// 宿主不再逐个命名表现层类型。
/// </summary>
public static class ServiceConfiguration
{
    /// <summary>
    /// 注册全部启动器服务。
    /// </summary>
    /// <param name="launcherDataRoot">
    /// 显式数据根：缺省时按进程解析（生产路径）。测试传它来让整张对象图——登记项与
    /// 闭包中捕获的那些（日志、崩溃快照、设置、下载检查点）——都落在同一个隔离目录里；
    /// 只替换 DI 登记项而漏掉闭包，会让测试写进真实用户数据目录。
    /// </param>
    public static IServiceCollection AddLauncherServices(
        this IServiceCollection services,
        UnifiedLogger? existingLogger = null,
        IFatalCrashService? existingFatalCrashService = null,
        LauncherDataRoot? launcherDataRoot = null)
    {
        // 进程根在这里解析一次，其余登记项与所有消费方共用这一个实例——
        // 「数据放哪」不再是各模块各自读一次的进程级静态。
        var dataRoot = launcherDataRoot ?? LauncherDataRoot.ForCurrentProcess();
        var buildIdentity = BuildInfo.Identity;

        // Core 服务先入容器：逆序释放时表现层先析构。
        services.AddLauncherCore(buildIdentity, dataRoot);

        // 宿主自有：拉起独立崩溃报告进程要用本进程的可执行文件与崩溃参数，
        // 是进程入口的知识。接口由 UI 声明（消费者在那边），实现在这里。
        services.AddSingleton<ICrashReporterLauncher, CrashReporterLauncher>();

        // 表现层整体登记（含生命周期门面），参数只传宿主才知道的值。
        services.AddLauncherPresentationServices(
            dataRoot,
            buildIdentity,
            existingLogger,
            existingFatalCrashService,
            Program.ShowHiddenSettings);

        // 生命周期门面：宿主只通过它与表现层交互（窗口/VM/托盘由它自己构造）。
        services.AddLauncherPresentation();

        return services;
    }
}
