using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Converters;

public sealed class ToastSeverityToBrushConverter : IValueConverter
{
    public static readonly ToastSeverityToBrushConverter Instance = new();

    public object? Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        if (value is not ToastSeverity severity)
        {
            return null;
        }

        var resourceKey = severity switch
        {
            ToastSeverity.Success => "LauncherToastSuccessBrush",
            ToastSeverity.Warning => "LauncherToastWarningBrush",
            ToastSeverity.Error => "LauncherToastErrorBrush",
            _ => "LauncherToastInfoBrush"
        };

        return Application.Current?.TryGetResource(
            resourceKey,
            ThemeVariant.Default,
            out var resource) == true
            && resource is IBrush brush
                ? brush
                : null;
    }

    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture) =>
        throw new NotSupportedException();
}
