using System;
using global::Avalonia.Animation.Easings;

namespace Cafe.Launcher.Avalonia.Helpers;

/// <summary>
/// Reads shared motion values defined in App.axaml so C# helpers stay on the design-token
/// source of truth, falling back to literal constants for headless/test scenarios.
/// </summary>
public static class MotionResourceLookup
{
    public static double GetDouble(string key, double fallback)
    {
        return application is null
            ? fallback
            : application.TryGetResource(key, application.ActualThemeVariant, out var doubleValue)
                && doubleValue is double typed
            ? typed
            : fallback;
    }

    public static Easing GetEasing(string key, Func<Easing> fallback)
    {
        return application is null
            ? fallback()
            : application.TryGetResource(key, application.ActualThemeVariant, out var easingValue)
                && easingValue is Easing typed
            ? typed
            : fallback();
    }

    private static global::Avalonia.Application? application => global::Avalonia.Application.Current;
}
