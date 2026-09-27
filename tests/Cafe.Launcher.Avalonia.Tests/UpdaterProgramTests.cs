using Cafe.Launcher.Avalonia.Testing;
using Cafe.Launcher.Updater;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class UpdaterProgramTests
{
    [Fact]
    public async Task RunAsync_WhenArgumentsAreMissing_ReturnsUsageExitCode()
    {
        var exitCode = await UpdaterProgram.RunAsync([]);

        Assert.Equal((int)UpdaterExitCode.Usage, exitCode);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public async Task RunAsync_WhenHelpIsRequested_ReturnsSuccess(string helpOption)
    {
        var exitCode = await UpdaterProgram.RunAsync([helpOption]);

        Assert.Equal((int)UpdaterExitCode.Success, exitCode);
        Assert.Contains("internal protocol", UpdaterProgram.HelpText, StringComparison.Ordinal);
        Assert.Contains(UpdaterArguments.ApplyCommand, UpdaterProgram.HelpText, StringComparison.Ordinal);
        Assert.Contains(UpdaterArguments.ProtocolVersionOption, UpdaterProgram.HelpText, StringComparison.Ordinal);
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

        Assert.Equal((int)UpdaterExitCode.UnexpectedFailure, exitCode);
        Assert.Contains("Fatal:", await File.ReadAllTextAsync(logPath), StringComparison.Ordinal);
    }
}
