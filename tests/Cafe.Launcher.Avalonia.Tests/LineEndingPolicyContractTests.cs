namespace Cafe.Launcher.Avalonia.Tests;

/// <summary>
/// Keeps the two halves of the line-ending policy in sync. <c>.editorconfig</c> states it for
/// editors; <c>.gitattributes</c> is what git actually applies on checkout and add. Only the latter
/// can stop a CRLF file from landing in the repository, so a policy declared in one file and not
/// the other is the drift this guards: it once left five <c>scripts/*.ps1</c> files committed with
/// CRLF (one of them carrying a corrupted <c>\r\r\n</c> ending), where every later edit showed up
/// as a whole-file diff.
/// </summary>
public sealed class LineEndingPolicyContractTests
{
    [Fact]
    public void GitAttributes_EnforcesTheLineEndingsEditorConfigDeclares()
    {
        var declared = ReadEditorConfigLineEndings(ReadRepoFile(".editorconfig"));
        Assert.True(declared.Count > 0, ".editorconfig no longer declares any end_of_line value.");

        var enforced = ReadGitAttributesLineEndings(ReadRepoFile(".gitattributes"));
        var missing = new List<string>();
        foreach (var (extension, expected) in declared)
        {
            if (!enforced.TryGetValue(extension, out var actual) || actual != expected)
            {
                var found = actual is null ? "nothing" : actual;
                missing.Add($"*{extension} needs eol={expected} but .gitattributes declares {found}");
            }
        }

        Assert.True(
            missing.Count == 0,
            "Every end_of_line an editor is told about must also be enforced for git:\n"
            + string.Join("\n", missing));
    }

    /// <summary>Maps each extension named by an editorconfig glob to the end_of_line it declares.</summary>
    private static Dictionary<string, string> ReadEditorConfigLineEndings(string text)
    {
        var declared = new Dictionary<string, string>(StringComparer.Ordinal);
        string[]? sectionExtensions = null;
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                sectionExtensions = ExtensionsOf(line[1..^1]);
                continue;
            }

            if (sectionExtensions is null || !line.StartsWith("end_of_line", StringComparison.Ordinal))
            {
                continue;
            }

            var value = line[(line.IndexOf('=') + 1)..].Trim();
            foreach (var extension in sectionExtensions)
            {
                declared[extension] = value;
            }
        }

        return declared;
    }

    /// <summary>Maps each extension named by a gitattributes glob to the eol it enforces.</summary>
    private static Dictionary<string, string> ReadGitAttributesLineEndings(string text)
    {
        var enforced = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var eol = parts
                .Skip(1)
                .FirstOrDefault(attribute => attribute.StartsWith("eol=", StringComparison.Ordinal));
            if (eol is null)
            {
                continue;
            }

            foreach (var extension in ExtensionsOf(parts[0]))
            {
                enforced[extension] = eol["eol=".Length..];
            }
        }

        return enforced;
    }

    /// <summary>
    /// Gets the extensions a glob covers, or an empty set when it does not name any (a path glob,
    /// binary pattern, or the coverage.ps1 override whose rule is about charset, not line endings).
    /// </summary>
    private static string[] ExtensionsOf(string pattern)
    {
        if (!pattern.StartsWith("*.", StringComparison.Ordinal))
        {
            return [];
        }

        var body = pattern[1..];
        if (body.StartsWith(".{", StringComparison.Ordinal) && body.EndsWith('}'))
        {
            return body[2..^1]
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(extension => "." + extension)
                .ToArray();
        }

        return body.Contains('/') || body.Contains('*') ? [] : [body];
    }

    private static string ReadRepoFile(string relativePath) =>
        File.ReadAllText(Path.Combine(TestLocalizationHelper.FindRepositoryRoot(), relativePath));
}
