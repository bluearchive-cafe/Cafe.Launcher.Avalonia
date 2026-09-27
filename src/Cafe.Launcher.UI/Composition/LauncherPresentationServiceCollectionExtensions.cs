using System;
using Microsoft.Extensions.DependencyInjection;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.UI.ViewModels;
using Cafe.Launcher.Core.Services;
using Cafe.Launcher.Core.Services.Diagnostics;

namespace Cafe.Launcher.UI.Composition;

/// <summary>
/// 表现层登记入口。会话自己组装窗口、ViewModel 与托盘，所以这里只需登记会话本身——
/// 宿主不再向表现层传任何回调。
/// </summary>
public static class LauncherPresentationServiceCollectionExtensions
{
    public static IServiceCollection AddLauncherPresentation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // 协作者的装配留在登记入口：会话按 §5.3 走构造函数注入（不注入 IServiceProvider），
        // 而它依赖的表现层类型是 internal——只有本程序集的登记方拼得出它，宿主因此拿不到
        // 「半个会话」，服务定位也不会渗进生命周期门面。
        services.AddSingleton(sp => new LauncherPresentationSession(
            sp.GetRequiredService<MainWindowViewModel>(),
            sp.GetRequiredService<WindowFilePickerService>(),
            sp.GetRequiredService<WindowMetricsService>(),
            sp.GetRequiredService<ILauncherDiagnostics>(),
            sp.GetRequiredService<ILauncherSettingsService>(),
            sp.GetRequiredService<ISavedSettingsWriter>(),
            sp.GetRequiredService<LocalizationService>(),
            sp.GetRequiredService<IErrorHandlingService>(),
            sp.GetRequiredService<ISystemTrayActions>(),
            sp.GetRequiredService<LauncherDataRoot>()));
        return services;
    }
}
