using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

/// <summary>
/// Shared locale initialization for unit tests. All test classes that use
/// <see cref="LocalizationService"/> must call <see cref="Initialize"/> in
/// their static constructor, because <see cref="LocalizationService.InitializeForTesting"/>
/// replaces the entire dictionary set — the last static constructor to run wins.
/// This helper loads the real locale JSON files from the source tree so tests
/// always match production data without manual key-list maintenance.
/// </summary>
public static class TestLocalizationHelper
{
    public static void Initialize()
    {
        var localesDir = FindLocalesDirectory();
        var resources = new Dictionary<string, Dictionary<string, string>>();
        foreach (var locale in new[] { LauncherLanguages.English, LauncherLanguages.SimplifiedChinese, LauncherLanguages.Japanese })
        {
            var filePath = Path.Combine(localesDir, $"{locale}.json");
            if (File.Exists(filePath))
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(filePath));
                if (dict is not null)
                    resources[locale] = dict;
            }
        }

        if (resources.Count > 0)
            LocalizationService.InitializeForTesting(resources);
    }

    private static string FindLocalesDirectory()
    {
        // Walk up from the test assembly location until we find Assets/Locales.
        var dir = Path.GetDirectoryName(typeof(TestLocalizationHelper).Assembly.Location);
        for (var i = 0; i < 8 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir, "Assets", "Locales");
            if (Directory.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }

        // Fallback: working directory (CI typically runs from repo root).
        return Path.Combine(Directory.GetCurrentDirectory(), "Assets", "Locales");
    }
}
