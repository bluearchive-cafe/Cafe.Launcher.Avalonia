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
