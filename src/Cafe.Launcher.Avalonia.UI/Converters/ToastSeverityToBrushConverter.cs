using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Cafe.Launcher.Avalonia.Models;

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

        var resourceKey = ToastSeverityProfile.For(severity).BrushResourceKey;

        var app = Application.Current;
        return app?.TryGetResource(
            resourceKey,
            app.ActualThemeVariant,
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
