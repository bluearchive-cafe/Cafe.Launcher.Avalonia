using Cafe.Launcher.Avalonia.Testing;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class ReleaseScriptTests
{
    [Fact]
    public void ReleaseScript_SkipsVersionCommitWhenProjectVersionIsAlreadyCommitted()
    {
        var script = File.ReadAllText(TestRepository.FromRepositoryRoot("release.ps1"));

        Assert.Contains(
            "git -C $ScriptDir diff --cached --quiet -- @releaseVersionFiles",
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
        Assert.Contains("git commit release version files", script, StringComparison.Ordinal);
        Assert.Contains("\"commit\", \"--only\"", script, StringComparison.Ordinal);
        Assert.EndsWith("exit 0", script.TrimEnd(), StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseScript_UpdatesArchVersionFilesBeforeCreatingTheTag()
    {
        var script = File.ReadAllText(TestRepository.FromRepositoryRoot("release.ps1"));

        Assert.Contains("ConvertTo-ArchPackageVersion", script, StringComparison.Ordinal);
        Assert.Contains("_realver=$newVersion", script, StringComparison.Ordinal);
        Assert.Contains("pkgver=$archPackageVersion", script, StringComparison.Ordinal);
        Assert.Contains("#tag=v$newVersion", script, StringComparison.Ordinal);
        Assert.Contains("$ArchPkgbuildRelativePath", script, StringComparison.Ordinal);
        Assert.Contains("$ArchSrcinfoRelativePath", script, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseScript_PreservesMaintainedChangelog()
    {
        var script = File.ReadAllText(TestRepository.FromRepositoryRoot("release.ps1"));

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
        var workflow = File.ReadAllText(TestRepository.FromRepositoryRoot(".github/workflows/release.yml"));

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
    public void VerifyScript_RestoresTheReleaseRuntimeForTheCurrentHost()
    {
        var script = File.ReadAllText(TestRepository.FromRepositoryRoot("verify.ps1"));

        Assert.Contains("if ($IsWindows)", script, StringComparison.Ordinal);
        Assert.Contains("elseif ($IsMacOS)", script, StringComparison.Ordinal);
        Assert.Contains("'linux-x64'", script, StringComparison.Ordinal);
        Assert.Contains("-r $releaseRid", script, StringComparison.Ordinal);
        Assert.DoesNotContain("-r win-x64", script, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseChangelog_BannerWhenPresent_UsesTaggedSourceRepository()
    {
        var changelog = File.ReadAllText(TestRepository.FromRepositoryRoot("CHANGELOG_RELEASE.md"));
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

}
