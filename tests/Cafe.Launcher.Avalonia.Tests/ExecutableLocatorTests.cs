using System;
using System.IO;
using Cafe.Launcher.Avalonia.Services.GameRuntime;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class ExecutableLocatorTests : IDisposable
{
    private readonly string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    [Fact]
    public void FindInPath_WhenExplicitPathExists_ReturnsExplicitPath()
    {
        Directory.CreateDirectory(tempDir);
        var executablePath = Path.Combine(tempDir, "umu-run");
        File.WriteAllText(executablePath, "#!/bin/sh\n");

        var result = ExecutableLocator.FindInPath("umu-run", explicitPath: executablePath);

        Assert.Equal(executablePath, result);
    }

    [Fact]
    public void FindInPath_WhenExplicitPathMissing_ReturnsNullWithoutScanning()
    {
        Directory.CreateDirectory(tempDir);
        var executablePath = Path.Combine(tempDir, "umu-run");
        File.WriteAllText(executablePath, "#!/bin/sh\n");

        var result = ExecutableLocator.FindInPath(
            "umu-run",
            explicitPath: Path.Combine(tempDir, "missing"),
            pathVariable: tempDir);

        Assert.Null(result);
    }

    [Fact]
    public void FindInPath_ScansPathVariableEntriesInOrder()
    {
        var firstDir = Path.Combine(tempDir, "first");
        var secondDir = Path.Combine(tempDir, "second");
        Directory.CreateDirectory(firstDir);
        Directory.CreateDirectory(secondDir);
        File.WriteAllText(Path.Combine(secondDir, "umu-run"), "#!/bin/sh\n");

        var pathVariable = string.Join(Path.PathSeparator, firstDir, secondDir);

        var result = ExecutableLocator.FindInPath("umu-run", pathVariable: pathVariable);

        Assert.Equal(Path.Combine(secondDir, "umu-run"), result);
    }

    [Fact]
    public void FindInPath_WhenPathVariableIsEmpty_ReturnsNull()
    {
        Assert.Null(ExecutableLocator.FindInPath("umu-run", pathVariable: ""));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(tempDir, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }
}
