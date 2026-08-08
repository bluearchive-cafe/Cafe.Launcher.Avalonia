namespace Cafe.Launcher.Avalonia.Tests;

public sealed class TestUserDataIsolationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Resolve_WhenTestOverrideIsMissing_UsesProductLocalApplicationData(
        string? testOverride)
    {
        var localApplicationData = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString("N"));

        var result = Services.LauncherUserDataDirectory.Resolve(
            testOverride,
            localApplicationData);

        Assert.Equal(
            Path.Combine(localApplicationData, Constants.LauncherConstants.ProductName),
            result);
    }

    [Fact]
    public void Resolve_WhenTestOverrideIsConfigured_UsesFullOverridePath()
    {
        var relativeOverride = Path.Combine(
            ".",
            Guid.NewGuid().ToString("N"),
            "..",
            "isolated-user-data");

        var result = Services.LauncherUserDataDirectory.Resolve(
            relativeOverride,
            "unused");

        Assert.Equal(Path.GetFullPath(relativeOverride), result);
    }

    [Fact]
    public void TestProcess_DefaultSettingsPathUsesIsolatedUserDataDirectory()
    {
        var isolatedDirectory = Environment.GetEnvironmentVariable(
            Services.LauncherUserDataDirectory.TestOverrideEnvironmentVariable);

        Assert.False(string.IsNullOrWhiteSpace(isolatedDirectory));
        Assert.StartsWith(
            Path.GetFullPath(Path.GetTempPath()),
            Path.GetFullPath(isolatedDirectory),
            StringComparison.OrdinalIgnoreCase);

        using var settingsService = new Services.LauncherSettingsService();
        Assert.Equal(
            Path.Combine(
                Path.GetFullPath(isolatedDirectory),
                Constants.GamePaths.LauncherSettingsFileName),
            settingsService.SettingsPath);
    }

    [Fact]
    public void PersistentUserDataPaths_UseCentralDirectoryProvider()
    {
        var projectRoot = FindProjectRoot();
        var allowedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine(projectRoot, "Services", "LauncherUserDataDirectory.cs"),
            Path.Combine(projectRoot, "Features", "GameOperations", "GameUninstallService.cs")
        };
        var offenders = Directory
            .EnumerateFiles(projectRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
            {
                var relativePath = Path.GetRelativePath(projectRoot, path);
                return !relativePath.StartsWith($"tests{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                    && !relativePath.StartsWith($".claude{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                    && !relativePath.StartsWith($".worktrees{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                    && !relativePath.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                    && !relativePath.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
            })
            .Where(path => !allowedFiles.Contains(path))
            .Where(path => File
                .ReadAllText(path)
                .Contains(
                    "Environment.SpecialFolder.LocalApplicationData",
                    StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(projectRoot, path))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offenders);
    }

    private static bool IsBuildOrTestArtifact(string projectRoot, string path)
    {
        var relativePath = Path.GetRelativePath(projectRoot, path);
        return relativePath.StartsWith($"tests{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            || relativePath.StartsWith($".claude{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            || relativePath.StartsWith($".worktrees{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            || relativePath.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            || relativePath.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Cafe.Launcher.Avalonia.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Cafe.Launcher.Avalonia.csproj was not found.");
    }
}
