namespace Cafe.Launcher.Avalonia.Tests;

/// <summary>
/// The self-contained archives redistribute the .NET runtime alongside the NuGet packages, so the
/// license disclosure has to name it and has to travel with the binaries. Both halves are asserted
/// here because either can be dropped silently: the generator's header is hand-written text, and
/// the packaging copy is one statement in a long script.
/// </summary>
public sealed class ThirdPartyNoticesContractTests
{
    [Fact]
    public void Notices_DeclareTheRedistributedSelfContainedRuntime()
    {
        var notices = File.ReadAllText(ProjectFile("THIRD-PARTY-NOTICES.md"));

        Assert.Contains("## Self-contained .NET runtime", notices, StringComparison.Ordinal);
        Assert.Contains("Microsoft.NETCore.App", notices, StringComparison.Ordinal);
        Assert.Contains("MIT-licensed", notices, StringComparison.Ordinal);
    }

    [Fact]
    public void NoticesGenerator_EmitsTheRuntimeSectionOnRegeneration()
    {
        var generator = File.ReadAllText(ProjectFile("scripts/New-ThirdPartyNotices.ps1"));

        Assert.Contains("## Self-contained .NET runtime", generator, StringComparison.Ordinal);
        Assert.Contains("dotnet --list-runtimes", generator, StringComparison.Ordinal);
    }

    [Fact]
    public void DistributionScript_ShipsTheLicenseDisclosureWithEveryArchive()
    {
        var script = File.ReadAllText(ProjectFile("scripts/Build-Distribution.ps1"));

        // Every packaging step below the publish loop copies the publish directory, so placing the
        // two files there is what puts them inside the zip, .app, tar.gz, deb and AppImage.
        Assert.Contains("\"LICENSE\", \"THIRD-PARTY-NOTICES.md\"", script, StringComparison.Ordinal);
        Assert.Contains("Copy-Item -LiteralPath (Join-Path $RootDir $noticeFile)", script, StringComparison.Ordinal);
    }

    private static string ProjectFile(string relativePath) =>
        Path.Combine(TestLocalizationHelper.FindRepositoryRoot(), relativePath);
}
