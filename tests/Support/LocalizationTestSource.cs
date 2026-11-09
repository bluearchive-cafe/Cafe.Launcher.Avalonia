using System;
using System.Collections.Generic;
using Cafe.Launcher.UI.Services;

namespace Cafe.Launcher.Testing;

/// <summary>
/// Per-instance resource table for tests. Replaces the removed process-wide override, so a
/// custom table can never leak into another test's service instance.
/// </summary>
internal sealed class LocalizationTestSource : ILocalizationResourceSource
{
    private readonly Dictionary<string, Dictionary<string, string>> resources;

    public LocalizationTestSource(Dictionary<string, Dictionary<string, string>> resources)
    {
        this.resources = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var (language, entries) in resources)
        {
            this.resources[language] = new Dictionary<string, string>(entries, StringComparer.Ordinal);
        }
    }

    public string? GetString(string effectiveLanguage, string key) =>
        resources.TryGetValue(effectiveLanguage, out var entries)
        && entries.TryGetValue(key, out var value)
            ? value
            : null;
}
