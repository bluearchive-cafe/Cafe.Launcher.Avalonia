using System;
using System.IO;
using System.Linq;
using Cafe.Launcher.UI.Services.GameRuntime;
using Cafe.Launcher.Testing;
using Cafe.Launcher.Core.Services.GameRuntime;

namespace Cafe.Launcher.Tests;

public sealed class ProtonBuildDiscoveryTests : IDisposable
{
    private readonly TestDirectory tempDir = TestDirectory.Create();

    [Fact]
    public void Discover_ListsDirectoriesThatContainAProtonScript()
    {
        var tools = Path.Combine(tempDir, "compatibilitytools.d");
        CreateBuild(tools, "GE-Proton9-1");
        CreateBuild(tools, "GE-Proton9-2");
        Directory.CreateDirectory(Path.Combine(tools, "not-a-build"));
        var discovery = new ProtonBuildDiscovery([tools]);

        var builds = discovery.Discover();

        Assert.Equal(["GE-Proton9-1", "GE-Proton9-2"], builds.Select(build => build.Name));
    }

    [Fact]
    public void Discover_WhenTheSearchDirectoriesDoNotExist_ReturnsEmpty()
    {
        var discovery = new ProtonBuildDiscovery([Path.Combine(tempDir, "missing")]);

        Assert.Empty(discovery.Discover());
    }

    [Fact]
    public void Discover_DeduplicatesTheSameInstallDirectoryListedTwice()
    {
        var tools = Path.Combine(tempDir, "compatibilitytools.d");
        CreateBuild(tools, "GE-Proton9-1");
        var discovery = new ProtonBuildDiscovery([tools, tools]);

        Assert.Single(discovery.Discover());
    }

    [Fact]
    public void Discover_WhenSteamRootsAreSymlinkAliases_ReturnsOneCanonicalBuild()
    {
        var steam = Path.Combine(tempDir, "Steam");
        var tools = Path.Combine(steam, "compatibilitytools.d");
        CreateBuild(tools, "GE-Proton9-1");
        var rootAlias = Path.Combine(tempDir, "root");
        var steamAlias = Path.Combine(tempDir, "steam-alias");
        TestSymlinks.CreateDirectorySymbolicLinkOrSkip(rootAlias, steam);
        TestSymlinks.CreateDirectorySymbolicLinkOrSkip(steamAlias, rootAlias);

        var builds = new ProtonBuildDiscovery([
            Path.Combine(rootAlias, "compatibilitytools.d"),
            Path.Combine(steamAlias, "compatibilitytools.d"), tools]).Discover();

        var build = Assert.Single(builds);
        Assert.Equal("GE-Proton9-1", build.Name);
        Assert.Equal(Path.Combine(tools, "GE-Proton9-1"), build.Path);
    }

    [Fact]
    public void Discover_WhenBuildDirectoryIsASymlink_DeduplicatesItsTarget()
    {
        var tools = Path.Combine(tempDir, "compatibilitytools.d");
        CreateBuild(tools, "GE-Proton9-1");
        TestSymlinks.CreateDirectorySymbolicLinkOrSkip(
            Path.Combine(tools, "alias"), Path.Combine(tools, "GE-Proton9-1"));

        var build = Assert.Single(new ProtonBuildDiscovery([tools]).Discover());

        Assert.Equal(Path.Combine(tools, "GE-Proton9-1"), build.Path);
    }

    [Fact]
    public void Discover_KeepsSameNamedBuildsFromDifferentSteamRoots()
    {
        var native = Path.Combine(tempDir, "native", "compatibilitytools.d");
        var flatpak = Path.Combine(tempDir, "flatpak", "compatibilitytools.d");
        CreateBuild(native, "GE-Proton9-1");
        CreateBuild(flatpak, "GE-Proton9-1");

        var builds = new ProtonBuildDiscovery([native, flatpak]).Discover();

        Assert.Equal(2, builds.Count);
        Assert.Contains(builds, build => build.Path == Path.Combine(native, "GE-Proton9-1"));
        Assert.Contains(builds, build => build.Path == Path.Combine(flatpak, "GE-Proton9-1"));
    }

    private static void CreateBuild(string toolsDirectory, string name)
    {
        var directory = Path.Combine(toolsDirectory, name);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "proton"), "#!/bin/sh\n");
    }

    public void Dispose()
    {
        tempDir.Dispose();
    }
}
