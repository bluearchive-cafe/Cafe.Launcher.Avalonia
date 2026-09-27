using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Media;

namespace Cafe.Launcher.Avalonia.Helpers;

public sealed class DesignTokenItem
{
    public required string Key { get; init; }
    public required string ValueText { get; init; }
    public IBrush? Swatch { get; init; }
}

public sealed class DesignTokenGroup
{
    public required string Family { get; init; }
    public required string DisplayName { get; init; }
    public required string ResxKey { get; init; }
    public required int Order { get; init; }
    public required IReadOnlyList<DesignTokenItem> Items { get; init; }
}

/// <summary>
/// Classifies <c>Launcher.*</c> tokens into the §3.2 twelve families from the key
/// segments and formats their resource values for the debug design gallery.
/// Pure logic (no Avalonia dependencies) so it is unit-testable without an app.
/// </summary>
public static class DesignTokenGrouping
{
    public static readonly string[] FamilyOrder =
    [
        "Color",
        "Text",
        "Spacing",
        "Radius",
        "Typography",
        "Icon",
        "Control",
        "Layout",
        "Motion",
        "Elevation",
        "StateLayer",
        "Component",
        "Border",
        "Other"
    ];

    /// <summary>Derives the family from the token key's second segment.</summary>
    public static string FamilyForKey(string key)
    {
        var parts = key.Split('.');
        return parts.Length >= 2 ? parts[1] : "Other";
    }

    /// <summary>
    /// Groups token pairs by family in spec order, sorted by key inside each group,
    /// with localized display names and formatted values.
    /// </summary>
    public static IReadOnlyList<DesignTokenGroup> BuildGroups(
        IEnumerable<(string Key, object? Value)> pairs,
        Func<string, string> localize)
    {
        var grouped = pairs
            .GroupBy(pair => FamilyForKey(pair.Key), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => new DesignTokenItem
                    {
                        Key = pair.Key,
                        ValueText = FormatValue(pair.Value),
                        Swatch = pair.Value as IBrush
                    })
                    .ToList(),
                StringComparer.Ordinal);

        return grouped
            .OrderBy(entry => FamilyIndex(entry.Key))
            .Select(entry => new DesignTokenGroup
            {
                Family = entry.Key,
                DisplayName = localize($"designGroup{entry.Key}"),
                ResxKey = $"designGroup{entry.Key}",
                Order = FamilyIndex(entry.Key),
                Items = entry.Value
            })
            .ToList();
    }

    /// <summary>Formats a token resource value for gallery display (invariant culture).</summary>
    public static string FormatValue(object? value) => value switch
    {
        SolidColorBrush brush => brush.Color.ToString(),
        LinearGradientBrush => "gradient",
        Thickness thickness => thickness.ToString(),
        CornerRadius cornerRadius => cornerRadius.ToString(),
        TimeSpan duration => $"{duration.TotalMilliseconds:0} ms",
        double number => number.ToString(CultureInfo.InvariantCulture),
        _ => value?.ToString() ?? "—"
    };

    private static int FamilyIndex(string family)
    {
        var index = Array.IndexOf(FamilyOrder, family);
        return index >= 0 ? index : FamilyOrder.Length - 1;
    }
}
