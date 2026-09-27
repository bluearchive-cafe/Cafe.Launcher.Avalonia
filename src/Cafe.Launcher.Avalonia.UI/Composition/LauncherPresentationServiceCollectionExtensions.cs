using System;
using Microsoft.Extensions.DependencyInjection;

namespace Cafe.Launcher.Avalonia.UI.Composition;

/// <summary>Registers the presentation façade after Core registration.</summary>
public static class LauncherPresentationServiceCollectionExtensions
{
    public static IServiceCollection AddLauncherPresentation(
        this IServiceCollection services,
        LauncherPresentationCallbacks callbacks)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(callbacks);

        services.AddSingleton(callbacks);
        services.AddSingleton<LauncherPresentationSession>();
        return services;
    }
}
