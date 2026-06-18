using System.Text;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class OldLauncherDetectionServiceTests : IDisposable
{
    private readonly string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public OldLauncherDetectionServiceTests()
    {
        Directory.CreateDirectory(tempDir);
    }

    [Fact]
    public void ResolveOldUserDataPath_Windows_ReturnsCorrectPath()
    {
        var path = OldLauncherDetectionService.ResolveOldUserDataPath();
        // On Linux (where tests run), this returns a Linux path
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            Assert.NotNull(path);
            Assert.Contains(".config", path);
            Assert.Contains(LauncherConstants.OldLauncherAppName, path);
        }
        else if (OperatingSystem.IsWindows())
        {
            Assert.NotNull(path);
            Assert.Contains("AppData", path);
            Assert.Contains(LauncherConstants.OldLauncherAppName, path);
        }
    }

    [Fact]
    public void Detect_NoOldLauncherDirectory_ReturnsNull()
    {
        // Point to a non-existent directory
        var nonExistentPath = Path.Combine(tempDir, "nonexistent_old_launcher");
        // The service resolves the real old launcher path, which shouldn't exist in tests
        // We test that the service handles non-existence gracefully by checking
        // that the resolved path is checked for existence.
        var path = OldLauncherDetectionService.ResolveOldUserDataPath();
        // This path may or may not exist in CI. If it doesn't, Detect() returns null.
        if (path is not null && !Directory.Exists(path))
        {
            var service = new OldLauncherDetectionService();
            var result = service.Detect();
            Assert.Null(result);
        }
    }

    [Fact]
    public void CopyClickCode_SourceDoesNotExist_NoError()
    {
        var sourceDir = Path.Combine(tempDir, "oldUserData");
        Directory.CreateDirectory(sourceDir);

        // Should not throw when source clickCode doesn't exist
        OldLauncherDetectionService.CopyClickCode(sourceDir);
    }

    [Fact]
    public void CopyClickCode_SourceExists_CopiesToTarget()
    {
        var sourceDir = Path.Combine(tempDir, "oldUserData");
        Directory.CreateDirectory(sourceDir);
        File.WriteAllText(Path.Combine(sourceDir, "clickCode"), "test_hash_12345");

        var targetDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            LauncherConstants.ProductName);
        var targetFile = Path.Combine(targetDir, "clickCode");

        // Clean up target first if it exists
        if (File.Exists(targetFile))
            File.Delete(targetFile);

        try
        {
            OldLauncherDetectionService.CopyClickCode(sourceDir);

            // Verify target was created with correct content
            Assert.True(File.Exists(targetFile));
            Assert.Equal("test_hash_12345", File.ReadAllText(targetFile).Trim());
        }
        finally
        {
            if (File.Exists(targetFile))
                File.Delete(targetFile);
        }
    }

    [Fact]
    public void CopyClickCode_TargetAlreadyExists_DoesNotOverwrite()
    {
        var sourceDir = Path.Combine(tempDir, "oldUserData");
        Directory.CreateDirectory(sourceDir);
        File.WriteAllText(Path.Combine(sourceDir, "clickCode"), "old_hash");

        var targetDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            LauncherConstants.ProductName);
        var targetFile = Path.Combine(targetDir, "clickCode");

        Directory.CreateDirectory(targetDir);
        File.WriteAllText(targetFile, "existing_hash");

        try
        {
            OldLauncherDetectionService.CopyClickCode(sourceDir);

            // Should NOT overwrite existing file
            Assert.Equal("existing_hash", File.ReadAllText(targetFile).Trim());
        }
        finally
        {
            if (File.Exists(targetFile))
                File.Delete(targetFile);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
