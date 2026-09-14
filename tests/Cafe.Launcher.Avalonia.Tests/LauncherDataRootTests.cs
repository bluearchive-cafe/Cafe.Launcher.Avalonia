using System;
using System.IO;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;

namespace Cafe.Launcher.Avalonia.Tests;

/// <summary>
/// 数据根模块的契约：进程根怎么解析、知名路径落在哪里、注入是否真的决定落点。
/// 「哪些文件可以解析进程根」由 <see cref="TestUserDataIsolationTests"/> 的结构守卫钉住。
/// </summary>
public sealed class LauncherDataRootTests : IDisposable
{
    private readonly string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public LauncherDataRootTests()
    {
        Directory.CreateDirectory(tempDir);
    }

    [Fact]
    public void Root_WhenGivenRelativePath_ResolvesToAbsolutePath()
    {
        var relative = Path.Combine(".", Guid.NewGuid().ToString("N"), "..", "data-root");

        var dataRoot = new LauncherDataRoot(relative);

        Assert.Equal(Path.GetFullPath(relative), dataRoot.Root);
    }

    [Fact]
    public void NamedPaths_AllLiveDirectlyUnderRoot_WithTheDeclaredNames()
    {
        var dataRoot = new LauncherDataRoot(tempDir);

        // 布局由本模块拥有：每个知名路径都是「根 ＋ 已声明的名字」，名字仍由
        // GamePaths / LauncherConstants 声明，避免同一份命名散到各消费方。
        Assert.Equal(Path.Combine(dataRoot.Root, GamePaths.LauncherSettingsFileName), dataRoot.SettingsPath);
        Assert.Equal(Path.Combine(dataRoot.Root, GamePaths.DownloadStateFileName), dataRoot.DownloadStatePath);
        Assert.Equal(Path.Combine(dataRoot.Root, GamePaths.NoticeStateFileName), dataRoot.NoticeStatePath);
        Assert.Equal(Path.Combine(dataRoot.Root, GamePaths.UnifiedLogFileName), dataRoot.UnifiedLogPath);
        Assert.Equal(Path.Combine(dataRoot.Root, LauncherDataRoot.ImageCacheFolderName), dataRoot.ImageCacheDirectory);
        Assert.Equal(Path.Combine(dataRoot.Root, LauncherDataRoot.CrashReportsFolderName), dataRoot.CrashReportsDirectory);
        Assert.Equal(Path.Combine(dataRoot.Root, LauncherConstants.LogExportFolderName), dataRoot.LogExportDirectory);

        foreach (var path in new[]
        {
            dataRoot.SettingsPath,
            dataRoot.DownloadStatePath,
            dataRoot.NoticeStatePath,
            dataRoot.UnifiedLogPath,
            dataRoot.ImageCacheDirectory,
            dataRoot.CrashReportsDirectory,
            dataRoot.LogExportDirectory
        })
        {
            Assert.Equal(dataRoot.Root, Path.GetDirectoryName(path));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WhenRootIsMissing_Throws(string? root)
    {
        Assert.ThrowsAny<ArgumentException>(() => new LauncherDataRoot(root!));
    }

    [Fact]
    public void InjectingADataRoot_DecidesWhereTheWrittenFileLands()
    {
        // 注入的实质：落点由传进来的根决定，而不是由某处进程级静态决定。
        var dataRoot = new LauncherDataRoot(tempDir);

        using var settings = new LauncherSettingsService(dataRoot);

        Assert.Equal(Path.Combine(tempDir, GamePaths.LauncherSettingsFileName), settings.SettingsPath);
    }

    [Fact]
    public async Task SaveAsync_ThroughTheInjectedRoot_WritesTheDeclaredSettingsFile()
    {
        var dataRoot = new LauncherDataRoot(tempDir);

        using var settings = new LauncherSettingsService(dataRoot);
        await settings.SaveAsync(LauncherSettings.CreateDefaults());

        Assert.True(File.Exists(dataRoot.SettingsPath));
        Assert.Equal([dataRoot.SettingsPath], Directory.GetFiles(tempDir));
    }

    [Fact]
    public void Constructor_WhenDataRootIsNull_Throws()
    {
        // 消费方的守卫：数据根是必需依赖，缺了要在构造点炸而不是运行期拿默认路径。
        Assert.Throws<ArgumentNullException>(() => new NoticeStateService(null!));
        Assert.Throws<ArgumentNullException>(() => new CrashReportStore(null!));
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
