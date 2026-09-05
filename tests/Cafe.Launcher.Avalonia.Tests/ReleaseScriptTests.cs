namespace Cafe.Launcher.Avalonia.Tests;

public sealed class ReleaseScriptTests
{
    [Fact]
    public void ReleaseScript_SkipsVersionCommitWhenProjectVersionIsAlreadyCommitted()
    {
        var script = File.ReadAllText(ProjectFile("release.ps1"));

        Assert.Contains(
            "git -C $ScriptDir diff --cached --quiet -- $CsprojRelativePath",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "Version already committed; using HEAD",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "if ($stagedDiffExitCode -eq 1)",
            script,
            StringComparison.Ordinal);
        Assert.EndsWith("exit 0", script.TrimEnd(), StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseScript_PreservesMaintainedChangelog()
    {
        var script = File.ReadAllText(ProjectFile("release.ps1"));

        Assert.Contains("if (Test-Path $ChangelogFile)", script, StringComparison.Ordinal);
        Assert.Contains(
            "Using existing changelog without modifying it",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "does not contain the expected heading",
            script,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseWorkflow_PrefersMaintainedChangelog()
    {
        var workflow = File.ReadAllText(ProjectFile(".github/workflows/release.yml"));

        Assert.Contains(
            "if (Test-Path \"CHANGELOG_RELEASE.md\")",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains(
            "Copy-Item \"CHANGELOG_RELEASE.md\" \"changelog.md\"",
            workflow,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseChangelog_BannerWhenPresent_UsesTaggedSourceRepository()
    {
        var changelog = File.ReadAllText(ProjectFile("CHANGELOG_RELEASE.md"));
        var bannerReferences = changelog
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.Contains("/docs/assets/release-banners/", StringComparison.Ordinal))
            .ToArray();

        Assert.All(bannerReferences, reference => Assert.Contains(
            "https://raw.githubusercontent.com/bluearchive-cafe/Cafe.Launcher.Avalonia/v",
            reference,
            StringComparison.Ordinal));
        Assert.DoesNotContain("/releases/download/", changelog, StringComparison.Ordinal);
    }

    private static string ProjectFile(string relativePath) =>
        Path.Combine(TestLocalizationHelper.FindRepositoryRoot(), relativePath);

}
