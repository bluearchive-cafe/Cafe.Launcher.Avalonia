using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace Cafe.Launcher.Avalonia.Converters;

/// <summary>
/// Two-way bridge between the UID-source segmented buttons and the single
/// <c>SelectedResourcePanelUidSource</c> property (ADR-041): Convert answers "is this segment
/// the selected source"; ConvertBack writes the segment's own constant back only when the
/// segment is checked, letting the partner segment's uncheck pass through untouched so two
/// RadioButtons can share one binding source without fighting over it.
/// </summary>
public sealed class ResourcePanelSourceSegmentConverter : IValueConverter
{
    public static readonly ResourcePanelSourceSegmentConverter Instance = new();

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
