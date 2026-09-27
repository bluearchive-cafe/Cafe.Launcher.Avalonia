using System;
using System.IO;
using System.Text.Json;
using Cafe.Launcher.UI.Services.GameRuntime;
using Cafe.Launcher.Testing;
using Cafe.Launcher.Core;
using Cafe.Launcher.Core.Services.GameRuntime;

namespace Cafe.Launcher.Tests;

public sealed class PrefixMetadataStoreTests : IDisposable
{
    private readonly TestDirectory tempDir = TestDirectory.Create();

    [Fact]
    public void Record_SamePrefixTwice_KeepsCreationTimeAndCountsLaunches()
    {
        var store = new PrefixMetadataStore(tempDir.DataRoot);
        var prefix = Path.Combine(tempDir, "prefix");

        var first = store.Record(prefix, "blue-archive-jp", "umu", "1.4.4", "auto");
        var second = store.Record(prefix, "blue-archive-jp", "umu", "1.4.5", "/opt/proton");

        Assert.Equal(first.CreatedAt, second.CreatedAt);
        Assert.Equal(1, first.LaunchCount);
        Assert.Equal(2, second.LaunchCount);
        Assert.Equal("1.4.5", second.RunnerVersion);
        Assert.Equal("/opt/proton", second.ProtonPath);
    }

    [Fact]
    public void Record_WithABuildIdentity_StampsTheInjectedLauncherVersion()
    {
        // 元数据里的 launcherVersion 曾读宿主 BuildInfo；迁入 Core 后必须来自注入的构建标识。
        var store = new PrefixMetadataStore(
            tempDir.DataRoot,
            new LauncherBuildIdentity("1.2.3-beta.4", "abc1234", "2026-09-27 17:06", "Release"));

        store.Record(Path.Combine(tempDir, "prefix"), "blue-archive-jp", "umu", "1.4.4", "auto");

        using var document = JsonDocument.Parse(File.ReadAllText(store.FilePath));
        Assert.Equal("1.2.3-beta.4", document.RootElement.GetProperty("launcherVersion").GetString());
    }

    [Fact]
    public void Record_WithoutABuildIdentity_LeavesTheLauncherVersionEmpty()
    {
        var store = new PrefixMetadataStore(tempDir.DataRoot);

        store.Record(Path.Combine(tempDir, "prefix"), "blue-archive-jp", "umu", "1.4.4", "auto");

        using var document = JsonDocument.Parse(File.ReadAllText(store.FilePath));
        Assert.Equal("", document.RootElement.GetProperty("launcherVersion").GetString());
    }
    [Fact]
    public void Record_WhenThePrefixChanges_StartsANewRecord()
    {
        var store = new PrefixMetadataStore(tempDir.DataRoot);

        var first = store.Record(Path.Combine(tempDir, "a"), "blue-archive-jp", "umu", "1.0", "auto");
        var second = store.Record(Path.Combine(tempDir, "b"), "blue-archive-jp", "wine", "9.0", null);

        Assert.Equal(1, first.LaunchCount);
        Assert.Equal(1, second.LaunchCount);
        Assert.Equal("wine", second.RunnerId);
    }

    [Fact]
    public void Record_WritesTheMetadataFile()
    {
        var store = new PrefixMetadataStore(tempDir.DataRoot);

        store.Record(Path.Combine(tempDir, "prefix"), "blue-archive-jp", "umu", "1.4.4", "auto");

        using var document = JsonDocument.Parse(File.ReadAllText(store.FilePath));
        var root = document.RootElement;
        Assert.Equal("umu", root.GetProperty("runnerId").GetString());
        Assert.Equal("1.4.4", root.GetProperty("runnerVersion").GetString());
        Assert.Equal(1, root.GetProperty("launchCount").GetInt32());
    }

    public void Dispose()
    {
        tempDir.Dispose();
    }
}
