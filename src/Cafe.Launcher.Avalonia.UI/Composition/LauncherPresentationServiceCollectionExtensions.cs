using System;
using Microsoft.Extensions.DependencyInjection;

namespace Cafe.Launcher.Avalonia.UI.Composition;

/// <summary>
/// 表现层登记入口。会话自己从容器解析窗口、ViewModel 与托盘，所以这里只需登记会话本身——
/// 宿主不再向表现层传任何回调。
/// </summary>
public static class LauncherPresentationServiceCollectionExtensions
{
    public static IServiceCollection AddLauncherPresentation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<LauncherPresentationSession>();
        return services;
    }
}
