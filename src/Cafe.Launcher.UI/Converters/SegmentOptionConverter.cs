using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace Cafe.Launcher.UI.Converters;

/// <summary>
/// Two-way bridge between a segmented option and the single code property it selects.
/// Convert answers "is this segment the selected code"; ConvertBack writes the segment's
/// own constant back only when checked, letting the partner segment's uncheck pass through
/// untouched so several RadioButtons can share one binding source without fighting over it.
/// Used by the resource panel UID source (ADR-041) and the appearance page's small enums.
/// </summary>
internal sealed class SegmentOptionConverter : IValueConverter
{
    public static readonly SegmentOptionConverter Instance = new();

    public object? Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture) =>
        string.Equals(value as string, parameter as string, StringComparison.Ordinal);

    public object? ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture) =>
        value is true ? parameter : BindingOperations.DoNothing;
}
