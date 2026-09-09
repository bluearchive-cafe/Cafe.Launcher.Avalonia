using System.Text.RegularExpressions;

namespace Cafe.Launcher.Avalonia.HeadlessTests;

/// <summary>
/// Keeps the committed golden baselines and the tests that compare them in step.
/// An orphan PNG is dead weight nobody compares; a comparison without a baseline
/// only fails on the machine that runs it.
/// </summary>
public sealed class GoldenBaselineContractTests
{
    [Fact]
    public void Baselines_CommittedBaselinesAndGoldenComparisons_MatchOneToOne()
    {
        var baselineDirectory = Path.Combine(
            GoldenScreenshot.FindRepositoryRoot(),
            GoldenScreenshot.BaselineRelativeDir);
        var projectDirectory = Path.GetDirectoryName(baselineDirectory)!;

        var baselines = Directory
            .EnumerateFiles(baselineDirectory, "*.png")
            .Select(file => Path.GetFileNameWithoutExtension(file)!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        // The guard reads source text, so it only sees comparisons that name the
        // baseline with a literal; a name passed through a variable or a wrapper
        // method escapes it and needs this pattern widened.
        var comparisonPattern = new Regex(
            @"GoldenScreenshot\.Compare\(\s*[A-Za-z0-9_.]+\s*,\s*""(?<name>[^""]+)""",
            RegexOptions.CultureInvariant);
        var compared = Directory
            .EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(IsSourceFile)
            .SelectMany(file => comparisonPattern
                .Matches(File.ReadAllText(file))
                .Select(match => match.Groups["name"].Value))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(baselines, compared);
    }

    private static bool IsSourceFile(string path) =>
        !path.Contains(
            $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase)
        && !path.Contains(
            $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase);
}
