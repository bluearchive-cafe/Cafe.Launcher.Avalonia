using System.Text.RegularExpressions;

namespace Cafe.Launcher.Avalonia.Helpers;

internal static partial class ReleaseNotesMarkdownSanitizer
{
    public static string Sanitize(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return "";
        }

        var withoutImages = ImageRegex().Replace(markdown, "$1");
        return LinkRegex().Replace(withoutImages, "$1");
    }

    [GeneratedRegex(@"!\[([^\]]*)\]\([^)]*\)", RegexOptions.CultureInvariant)]
    private static partial Regex ImageRegex();

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]*\)", RegexOptions.CultureInvariant)]
    private static partial Regex LinkRegex();
}
