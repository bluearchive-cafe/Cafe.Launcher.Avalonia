using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Services;

/// <summary>
/// Pure resolvers for the launcher's 4-language model:
/// mapping setting codes → effective launcher language → .NET CultureInfo,
/// plus system-language detection. Stateless and safe to call from any thread.
/// </summary>
public static class LauncherCultureResolver
{
    /// <summary>
    /// Resolves a language setting value to one of the four
    /// effective launcher language codes (never <c>"auto"</c>).
    /// </summary>
    public static string ResolveEffectiveLanguage(string? language)
    {
        return language switch
        {
            LauncherLanguages.English => LauncherLanguages.English,
            LauncherLanguages.SimplifiedChinese => LauncherLanguages.SimplifiedChinese,
            LauncherLanguages.TraditionalChinese => LauncherLanguages.TraditionalChinese,
            LauncherLanguages.Japanese => LauncherLanguages.Japanese,
            _ => ResolveSystemLanguage(CultureInfo.CurrentUICulture.Name)
        };
    }

    /// <summary>
    /// Determines the appropriate launcher language for a given system culture name.
    /// </summary>
    public static string ResolveSystemLanguage(string? systemCultureName)
    {
        if (string.IsNullOrWhiteSpace(systemCultureName))
            return LauncherLanguages.English;

        var subtags = systemCultureName.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (subtags.Length == 0)
            return LauncherLanguages.English;

        if (string.Equals(subtags[0], "zh", StringComparison.OrdinalIgnoreCase))
        {
            return IsTraditionalChineseRegion(systemCultureName)
                ? LauncherLanguages.TraditionalChinese
                : LauncherLanguages.SimplifiedChinese;
        }

        if (string.Equals(subtags[0], "ja", StringComparison.OrdinalIgnoreCase))
            return LauncherLanguages.Japanese;

        return LauncherLanguages.English;
    }

    /// <summary>
    /// Returns true when the culture name contains an exact traditional Chinese
    /// script subtag or a recognized traditional Chinese region subtag.
    /// </summary>
    public static bool IsTraditionalChineseRegion(string? cultureName)
    {
        if (string.IsNullOrWhiteSpace(cultureName))
            return false;

        var subtags = cultureName.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (subtags.Length == 0)
            return false;

        if (subtags.Skip(1).Any(subtag =>
                string.Equals(subtag, "Hant", StringComparison.OrdinalIgnoreCase)))
            return true;

        if (subtags.Skip(1).Any(subtag =>
                string.Equals(subtag, "Hans", StringComparison.OrdinalIgnoreCase)))
            return false;

        return subtags.Skip(1).Any(subtag =>
            string.Equals(subtag, "TW", StringComparison.OrdinalIgnoreCase)
            || string.Equals(subtag, "HK", StringComparison.OrdinalIgnoreCase)
            || string.Equals(subtag, "MO", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Maps an effective launcher language to its concrete .NET CultureInfo.
    /// Throws <see cref="ArgumentOutOfRangeException"/> for unrecognised codes.
    /// </summary>
    public static CultureInfo GetCultureFor(string effectiveLanguage)
    {
        return effectiveLanguage switch
        {
            LauncherLanguages.English => new CultureInfo("en-US"),
            LauncherLanguages.SimplifiedChinese => new CultureInfo("zh-CN"),
            LauncherLanguages.TraditionalChinese => new CultureInfo("zh-TW"),
            LauncherLanguages.Japanese => new CultureInfo("ja-JP"),
            _ => throw new ArgumentOutOfRangeException(
                nameof(effectiveLanguage), effectiveLanguage, "Unsupported effective launcher language.")
        };
    }
}
