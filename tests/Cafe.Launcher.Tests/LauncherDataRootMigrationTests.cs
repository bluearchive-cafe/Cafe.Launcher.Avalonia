using System.IO;
using Cafe.Launcher.Core.Constants;
using Cafe.Launcher.Core.Services;
using Cafe.Launcher.Testing;

namespace Cafe.Launcher.Tests;

public sealed class LauncherDataRootMigrationTests : IDisposable
{
    private readonly TestDirectory tempDir = TestDirectory.Create();

    [Fact]
    public void MigrateLegacyRoot_MovesEntriesIntoTheNewRootAndKeepsSockets()
    {
        var legacy = tempDir.Sub("Cafe Launcher");
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, LauncherPaths.LauncherSettingsFileName), "{}");
        File.WriteAllText(Path.Combine(legacy, LauncherPaths.DownloadStateFileName), "{}");
        File.WriteAllText(Path.Combine(legacy, "unknown.txt"), "unknown");
        var imageCache = Path.Combine(legacy, LauncherDataRoot.ImageCacheFolderName);
        Directory.CreateDirectory(imageCache);
        File.WriteAllText(Path.Combine(imageCache, "image.png"), "cache");
        var compatibility = Path.Combine(legacy, LauncherDataRoot.CompatibilityFolderName);
        Directory.CreateDirectory(compatibility);
        File.WriteAllText(Path.Combine(compatibility, "marker.txt"), "compat");
        var socket = Path.Combine(legacy, "cl-signal-deadbeef.sock");
        File.WriteAllText(socket, "socket");

        var dataRoot = new LauncherDataRoot(tempDir.Sub("cafe-launcher"));
        LauncherDataRootMigration.MigrateLegacyRoot(dataRoot, legacy);

        Assert.Equal(
            "{}",
            File.ReadAllText(Path.Combine(dataRoot.Root, LauncherPaths.LauncherSettingsFileName)));
        Assert.Equal(
            "{}",
            File.ReadAllText(Path.Combine(dataRoot.Root, LauncherPaths.DownloadStateFileName)));
        Assert.Equal(
            "unknown",
            File.ReadAllText(Path.Combine(dataRoot.Root, "unknown.txt")));
        Assert.Equal(
            "cache",
            File.ReadAllText(Path.Combine(dataRoot.ImageCacheDirectory, "image.png")));
        Assert.Equal(
            "compat",
            File.ReadAllText(
                Path.Combine(dataRoot.Root, LauncherDataRoot.CompatibilityFolderName, "marker.txt")));

        // 套接字不迁移，旧目录因此保留；下一次启动会在新根重建运行期套接字。
        Assert.True(File.Exists(socket));
        Assert.True(Directory.Exists(legacy));
    }

    [Fact]
    public void MigrateLegacyRoot_DoesNotOverwriteExistingTargets()
    {
        var legacy = tempDir.Sub("Cafe Launcher");
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, LauncherPaths.LauncherSettingsFileName), "old");
        var dataRoot = new LauncherDataRoot(tempDir.Sub("cafe-launcher"));
        Directory.CreateDirectory(dataRoot.Root);
        File.WriteAllText(dataRoot.SettingsPath, "new");

        LauncherDataRootMigration.MigrateLegacyRoot(dataRoot, legacy);

        Assert.Equal("new", File.ReadAllText(dataRoot.SettingsPath));
        Assert.Equal(
            "old",
            File.ReadAllText(Path.Combine(legacy, LauncherPaths.LauncherSettingsFileName)));
    }

    [Fact]
    public void MigrateLegacyRoot_WhenLegacyRootIsMissing_IsANoOp()
    {
        var dataRoot = new LauncherDataRoot(tempDir.Sub("cafe-launcher"));

        LauncherDataRootMigration.MigrateLegacyRoot(
            dataRoot,
            tempDir.Sub("does-not-exist"));

        Assert.False(Directory.Exists(dataRoot.Root));
    }

    [Fact]
    public void MigrateLegacyRoot_IsIdempotent()
    {
        var legacy = tempDir.Sub("Cafe Launcher");
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, LauncherPaths.NoticeStateFileName), "notices");
        var dataRoot = new LauncherDataRoot(tempDir.Sub("cafe-launcher"));

        LauncherDataRootMigration.MigrateLegacyRoot(dataRoot, legacy);
        LauncherDataRootMigration.MigrateLegacyRoot(dataRoot, legacy);

        Assert.Equal("notices", File.ReadAllText(dataRoot.NoticeStatePath));
        Assert.False(Directory.Exists(legacy));
    }

    [Fact]
    public void MigrateLegacyRoot_MovesSettingsTemporaryFile()
    {
        var legacy = tempDir.Sub("Cafe Launcher");
        Directory.CreateDirectory(legacy);
        File.WriteAllText(
            Path.Combine(legacy, LauncherPaths.LauncherSettingsFileName + ".tmp"),
            "pending");
        var dataRoot = new LauncherDataRoot(tempDir.Sub("cafe-launcher"));

        LauncherDataRootMigration.MigrateLegacyRoot(dataRoot, legacy);

        Assert.Equal(
            "pending",
            File.ReadAllText(Path.Combine(dataRoot.Root, LauncherPaths.LauncherSettingsFileName + ".tmp")));
    }

    [Fact]
    public void MigrateLegacyRoot_WhenOnlySocketsRemain_KeepsTheLegacyDirectory()
    {
        var legacy = tempDir.Sub("Cafe Launcher");
        Directory.CreateDirectory(legacy);
        var socket = Path.Combine(legacy, "cl-signal-deadbeef.sock");
        File.WriteAllText(socket, "socket");
        var dataRoot = new LauncherDataRoot(tempDir.Sub("cafe-launcher"));

        LauncherDataRootMigration.MigrateLegacyRoot(dataRoot, legacy);

        Assert.True(File.Exists(socket));
        Assert.True(Directory.Exists(legacy));
        Assert.False(Directory.Exists(dataRoot.Root));
    }

    [Fact]
    public void MigrateLegacyRoot_WhenRootsAreEqual_IsANoOp()
    {
        var legacy = tempDir.Sub("cafe-launcher");
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "marker.txt"), "data");
        var dataRoot = new LauncherDataRoot(legacy);

        LauncherDataRootMigration.MigrateLegacyRoot(dataRoot, legacy);

        Assert.Equal("data", File.ReadAllText(Path.Combine(legacy, "marker.txt")));
    }

    public void Dispose()
    {
        tempDir.Dispose();
    }
}
