using System.Text.RegularExpressions;

namespace Cafe.Launcher.Avalonia.Tests;

/// <summary>
/// Source contract for PROJECT_CONVENTIONS.md §3.2: the <c>title</c> argument of a diagnostics call
/// is the log line's <c>[LogTitle]</c> tag — a short PascalCase module identifier — and the
/// surrounding detail belongs in the message slot. The tag is the only structured field besides
/// timestamp and level, and the log viewer renders it as the entry heading, so a sentence there is
/// visible to the user and defeats triage by module.
/// </summary>
public sealed class DiagnosticsLogTitleContractTests
{
    /// <summary>Instance calls: the first literal argument of <c>diagnostics.XxxAsync(...)</c> is the title.</summary>
    private static readonly Regex InstanceTitlePattern = new(
        @"\bdiagnostics\.(?:Error|Warning|Message|Verbose|Debug|Fatal)Async\s*\(\s*""((?:[^""\\]|\\.)*)""",
        RegexOptions.CultureInvariant);

    /// <summary>Static calls, whose first argument is the severity rather than the title.</summary>
    private static readonly Regex StaticTitlePattern = new(
        @"\bLocalDiagnostics\.Log(?:Async|Sync)\s*\(\s*[A-Za-z_][\w.]*\s*,\s*""((?:[^""\\]|\\.)*)""",
        RegexOptions.CultureInvariant);

    private static readonly Regex ModuleTagPattern = new(
        "^[A-Z][A-Za-z0-9]*$",
        RegexOptions.CultureInvariant);

    [Fact]
    public void DiagnosticsCalls_PassAModuleTagAsTitle()
    {
        var offenders = new List<string>();
        foreach (var file in EnumerateSourceFiles())
        {
            var text = File.ReadAllText(file);
            foreach (var match in InstanceTitlePattern
                .Matches(text)
                .Concat(StaticTitlePattern.Matches(text)))
            {
                var title = match.Groups[1].Value;
                if (!ModuleTagPattern.IsMatch(title))
                {
                    offenders.Add($"{Relative(file)}: \"{title}\"");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "PROJECT_CONVENTIONS.md §3.2 requires a PascalCase module tag (for example \"GameDownload\") "
            + "as the title argument; move the descriptive sentence into the message. Offenders:\n"
            + string.Join("\n", offenders));
    }

    [Fact]
    public void DiagnosticsCalls_InspectAtLeastOneTitleSoTheContractStaysLive()
    {
        // A guard that silently stops matching anything is worse than no guard: the scan above
        // reports success when both patterns find nothing at all.
        var inspected = EnumerateSourceFiles()
            .Select(File.ReadAllText)
            .Sum(text => InstanceTitlePattern.Count(text) + StaticTitlePattern.Count(text));

        Assert.True(inspected >= 50, $"Only {inspected} diagnostics titles were inspected; the scan pattern has drifted.");
    }

    private static IEnumerable<string> EnumerateSourceFiles() =>
        Directory
            .EnumerateFiles(
                Path.Combine(TestLocalizationHelper.FindRepositoryRoot(), "src"),
                "*.cs",
                SearchOption.AllDirectories)
            // Generated sources (source generators, assembly attributes) are not the contract's subject.
            .Where(file => !HasIntermediateSegment(file));

    private static string Relative(string file) =>
        Path.GetRelativePath(TestLocalizationHelper.FindRepositoryRoot(), file);

    private static bool HasIntermediateSegment(string file) =>
        file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment is "obj" or "bin");
}
