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
        var projectDirectory = Path.Combine(
            GoldenScreenshot.FindRepositoryRoot(),
            "tests",
            "Cafe.Launcher.Avalonia.HeadlessTests");

        var baselines = Directory
            .EnumerateFiles(Path.Combine(projectDirectory, "Baselines"), "*.png")
            .Select(file => Path.GetFileNameWithoutExtension(file)!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        var comparisonPattern = new Regex(
            @"GoldenScreenshot\.Compare\(\s*[A-Za-z0-9_.]+\s*,\s*""(?<name>[^""]+)""",
            RegexOptions.CultureInvariant);
        var compared = Directory
            .EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
            .SelectMany(file => comparisonPattern
                .Matches(File.ReadAllText(file))
                .Select(match => match.Groups["name"].Value))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(baselines, compared);
    }
}
