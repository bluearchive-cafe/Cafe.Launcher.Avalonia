using Cafe.Launcher.Avalonia.Testing;
using Cafe.Launcher.Updater;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class UpdaterProgramTests
{
    [Fact]
    public async Task RunAsync_WhenArgumentsAreMissing_ReturnsUsageExitCode()
    {
        var exitCode = await UpdaterProgram.RunAsync([]);

        Assert.Equal(64, exitCode);
    }

    [Fact]
    public async Task RunAsync_WhenThePackageCannotBeOpened_ReturnsFailureAndLogsTheException()
    {
        using var directory = TestDirectory.Create();
        var logPath = Path.Combine(directory.Path, "update-apply.log");
        var arguments = new UpdaterArguments(
            UpdateApplyMode.Portable,
            Path.Combine(directory.Path, "missing-package.zip"),
            Path.Combine(directory.Path, "install"),
            "Cafe.Launcher.Avalonia.exe",
            int.MaxValue,
            new string('a', 64),
            logPath);

        var exitCode = await UpdaterProgram.RunAsync(arguments.ToArgumentArray());

        Assert.Equal(1, exitCode);
        Assert.Contains("Fatal:", await File.ReadAllTextAsync(logPath), StringComparison.Ordinal);
    }
}
