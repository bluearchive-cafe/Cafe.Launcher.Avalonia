using System.Text.RegularExpressions;

namespace Cafe.Launcher.Tests;

/// <summary>
/// Source contract for PROJECT_CONVENTIONS.md §5.2: the <c>title</c> argument of a diagnostics call
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

    /// <summary>
    /// Interpolated titles in the instance slot. The literal patterns above cannot see
    /// <c>diagnostics.ErrorAsync($"... {value}.", ...)</c>: the first argument is a string, just not a
    /// constant one, so the tag became the rendered heading of the entry (AUD-LOG-002). A tag assembled
    /// at runtime is never a stable module identifier, so every interpolation prefix is an offender.
    /// A constant or a variable is deliberately still accepted — only a compiler can tell those apart.
    /// </summary>
    private static readonly Regex InterpolatedInstanceTitlePattern = new(
        @"\bdiagnostics\.(?:Error|Warning|Message|Verbose|Debug|Fatal)Async\s*\(\s*(?:\$@?|@\$)""",
        RegexOptions.CultureInvariant);

    /// <summary>Interpolated titles in the static slot, whose first argument is the severity.</summary>
    private static readonly Regex InterpolatedStaticTitlePattern = new(
        @"\bLocalDiagnostics\.Log(?:Async|Sync)\s*\(\s*[A-Za-z_][\w.]*\s*,\s*(?:\$@?|@\$)""",
        RegexOptions.CultureInvariant);

    private static readonly Regex ModuleTagPattern = new(
        "^[A-Z][A-Za-z0-9]*$",
        RegexOptions.CultureInvariant);

    [Fact]
    public void DiagnosticsCalls_PassAModuleTagAsTitle()
    {
        var offenders = FindOffenders(
            EnumerateSourceFiles().Select(file => (Name: Relative(file), Text: File.ReadAllText(file))));

        Assert.True(
            offenders.Count == 0,
            "PROJECT_CONVENTIONS.md §5.2 requires a PascalCase module tag (for example \"GameDownload\") "
            + "as the title argument; move the descriptive sentence into the message. Offenders:\n"
            + string.Join("\n", offenders));
    }

    /// <summary>
    /// The scanner's own red test: the shape that escaped the literal patterns must still be reported.
    /// Without this fixture the interpolated scan could stop matching and the contract would silently
    /// pass again, which is exactly how the sentence-in-title form survived.
    /// </summary>
    [Fact]
    public void DiagnosticsCallScanner_FlagsTheInterpolatedTitleThatEscapedTheLiteralPatterns()
    {
        const string historicCall =
            "await diagnostics.ErrorAsync(\n"
            + "    $\"Launcher remote state read failed: {operation}.\",\n"
            + "    exception.Message,\n"
            + "    exception,\n"
            + "    CancellationToken.None).ConfigureAwait(false);";

        var offenders = FindOffenders([(Name: "LauncherStatusSnapshotService.cs", Text: historicCall)]);

        Assert.Single(offenders);
        Assert.Contains("interpolated title", offenders[0], StringComparison.Ordinal);

        // The replacement form — constant tag first, interpolation moved into the message — stays silent.
        const string currentCall =
            "await diagnostics.ErrorAsync(\n"
            + "    \"LauncherCore\",\n"
            + "    $\"Launcher remote state read failed: {operation}.\",\n"
            + "    exception,\n"
            + "    CancellationToken.None).ConfigureAwait(false);";

        Assert.Empty(FindOffenders([(Name: "LauncherStatusSnapshotService.cs", Text: currentCall)]));
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

    /// <summary>
    /// Reports every title argument that cannot be a stable module tag: a string literal that is not
    /// PascalCase, or an interpolated string sitting in the title slot. Takes the sources rather than
    /// reading the repository so the scanner itself can be exercised on a fixture.
    /// </summary>
    private static List<string> FindOffenders(IEnumerable<(string Name, string Text)> sources)
    {
        var offenders = new List<string>();
        foreach (var (name, text) in sources)
        {
            foreach (var match in InstanceTitlePattern
                .Matches(text)
                .Concat(StaticTitlePattern.Matches(text)))
            {
                var title = match.Groups[1].Value;
                if (!ModuleTagPattern.IsMatch(title))
                {
                    offenders.Add($"{name}: \"{title}\"");
                }
            }

            foreach (var match in InterpolatedInstanceTitlePattern
                .Matches(text)
                .Concat(InterpolatedStaticTitlePattern.Matches(text)))
            {
                var line = text[..match.Index].Count(character => character == '\n') + 1;
                var value = match.Value;
                var newline = value.IndexOf('\n', StringComparison.Ordinal);
                var head = newline < 0 ? value : value[..newline];
                offenders.Add($"{name}:{line}: interpolated title in \"{head.Trim()}\"");
            }
        }

        return offenders;
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
