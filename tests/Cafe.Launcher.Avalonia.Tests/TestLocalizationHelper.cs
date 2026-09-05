using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

/// <summary>
/// Shared locale and project-root helpers for unit tests. All test classes that use
/// <see cref="LocalizationService"/> must call <see cref="Initialize"/> in
/// their static constructor, because <see cref="LocalizationService.InitializeForTesting"/>
/// replaces the entire test resource set — the last static constructor to run wins.
/// This helper loads from the .resx files on disk so tests always match the committed
/// resource data.
/// </summary>
public static class TestLocalizationHelper
{
    private static readonly string[] Locales = [LauncherLanguages.English, LauncherLanguages.SimplifiedChinese, LauncherLanguages.TraditionalChinese, LauncherLanguages.Japanese];
    private static readonly string[] ResxFiles = ["LauncherStrings.resx", "LauncherStrings.zh-Hans.resx", "LauncherStrings.zh-Hant.resx", "LauncherStrings.ja.resx"];

    public static void Initialize()
    {
        var resxDir = Path.Combine(FindProjectRoot(), "Resources");
        if (!Directory.Exists(resxDir))
            throw new DirectoryNotFoundException($"Required localization resource directory is missing: {resxDir}");

        var resources = new Dictionary<string, Dictionary<string, string>>();
        for (var i = 0; i < Locales.Length; i++)
        {
            var filePath = Path.Combine(resxDir, ResxFiles[i]);
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Required localization resource is missing: {filePath}", filePath);

            resources[Locales[i]] = ReadResx(filePath);
        }

        LocalizationService.InitializeForTesting(resources);
    }

    /// <summary>
    /// Reads a .resx file and returns its key→value pairs.
    /// </summary>
    public static Dictionary<string, string> ReadResx(string path)
    {
        var doc = XDocument.Load(path);
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var data in doc.Root!.Elements("data"))
        {
            var name = data.Attribute("name")?.Value
                ?? throw new InvalidDataException($"data element without name in {path}");
            var value = data.Element("value")?.Value ?? string.Empty;
            dict[name] = value;
        }

        return dict;
    }

    /// <summary>
    /// Walks up from <see cref="AppContext.BaseDirectory"/> to find the
    /// application root containing <c>Cafe.Launcher.Avalonia.csproj</c>.
    /// </summary>
    public static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var applicationProject = Path.Combine(directory.FullName, "src", "Cafe.Launcher.Avalonia", "Cafe.Launcher.Avalonia.csproj");
            if (File.Exists(applicationProject))
                return Path.GetDirectoryName(applicationProject)!;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("src/Cafe.Launcher.Avalonia/Cafe.Launcher.Avalonia.csproj was not found.");
    }

    /// <summary>
    /// Walks up to the repository root (the directory containing the solution
    /// file) — for tests reading repo-level files such as workflows and
    /// release scripts that do not live under the project directory.
    /// </summary>
    public static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Cafe.Launcher.Avalonia.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Cafe.Launcher.Avalonia.slnx was not found.");
    }
}
