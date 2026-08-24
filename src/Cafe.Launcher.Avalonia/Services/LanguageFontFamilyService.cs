using System;
using Avalonia.Media;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Services;

public static class LanguageFontFamilyService
{
    private static readonly FontFamily English = new("Segoe UI");
    private static readonly FontFamily SimplifiedChinese = new("Microsoft YaHei UI");
    private static readonly FontFamily TraditionalChinese = new("Microsoft JhengHei UI");
    private static readonly FontFamily Japanese = new("Yu Gothic UI");

    public static FontFamily GetForEffectiveLanguage(string language) =>
        language switch
        {
            LauncherLanguages.English => English,
            LauncherLanguages.SimplifiedChinese => SimplifiedChinese,
            LauncherLanguages.TraditionalChinese => TraditionalChinese,
            LauncherLanguages.Japanese => Japanese,
            _ => throw new ArgumentOutOfRangeException(
                nameof(language),
                language,
                "Unsupported effective launcher language.")
        };
}
