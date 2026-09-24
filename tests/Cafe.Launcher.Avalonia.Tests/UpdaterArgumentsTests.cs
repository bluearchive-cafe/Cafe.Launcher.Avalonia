using Cafe.Launcher.Updater;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class UpdaterArgumentsTests
{
    private static readonly string ValidSha = new('a', 64);

    [Fact]
    public void TryParse_WhenAllOptionsArePresent_ReturnsParsedArguments()
    {
        var parsed = UpdaterArguments.TryParse(ValidArguments(), out var arguments, out var error);

        Assert.True(parsed, error);
        Assert.NotNull(arguments);
        Assert.Equal(UpdateApplyMode.Portable, arguments!.Mode);
        Assert.Equal("/tmp/pkg.zip", arguments.PackagePath);
        Assert.Equal("/tmp/app", arguments.InstallDirectory);
        Assert.Equal("Cafe.Launcher.Avalonia.exe", arguments.ExecutableName);
        Assert.Equal(1234, arguments.ParentProcessId);
        Assert.Equal(ValidSha, arguments.ExpectedSha256);
        Assert.Equal("/tmp/log", arguments.LogPath);
    }

    [Theory]
    [InlineData("installer", UpdateApplyMode.Installer)]
    [InlineData("portable", UpdateApplyMode.Portable)]
    public void TryParse_ParsesBothModes(string mode, UpdateApplyMode expected)
    {
        var arguments = ValidArguments();
        arguments[1] = mode;

        Assert.True(UpdaterArguments.TryParse(arguments, out var parsed, out var error), error);
        Assert.Equal(expected, parsed!.Mode);
    }

    [Fact]
    public void TryParse_WhenAnOptionHasNoValue_Fails()
    {
        Assert.False(UpdaterArguments.TryParse(["--mode"], out _, out var error));
        Assert.Contains("missing a value", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryParse_WhenOptionIsUnknown_Fails()
    {
        var arguments = ValidArguments();
        arguments[0] = "--nope";

        Assert.False(UpdaterArguments.TryParse(arguments, out _, out var error));
        Assert.Contains("Unknown option", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryParse_WhenModeIsUnknown_Fails()
    {
        var arguments = ValidArguments();
        arguments[1] = "sideload";

        Assert.False(UpdaterArguments.TryParse(arguments, out _, out var error));
        Assert.Contains("Unknown mode", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("abc")]
    public void TryParse_WhenParentPidIsInvalid_Fails(string pid)
    {
        var arguments = ValidArguments();
        arguments[9] = pid;

        Assert.False(UpdaterArguments.TryParse(arguments, out _, out var error));
        Assert.Contains("parent process id", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryParse_WhenSha256IsInvalid_Fails()
    {
        var arguments = ValidArguments();
        arguments[11] = "not-a-hash";

        Assert.False(UpdaterArguments.TryParse(arguments, out _, out var error));
        Assert.Contains("SHA-256", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryParse_WhenRequiredOptionIsMissing_Fails()
    {
        var arguments = ValidArguments().Where(value => value != UpdaterArguments.LogOption && value != "/tmp/log").ToArray();

        Assert.False(UpdaterArguments.TryParse(arguments, out _, out var error));
        Assert.Contains(UpdaterArguments.LogOption, error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryParse_WhenAnOptionIsRepeated_Fails()
    {
        var arguments = ValidArguments().Concat(new[] { UpdaterArguments.LogOption, "/tmp/other" }).ToArray();

        Assert.False(UpdaterArguments.TryParse(arguments, out _, out var error));
        Assert.Contains("repeated", error, StringComparison.Ordinal);
    }

    private static string[] ValidArguments() =>
    [
        UpdaterArguments.ModeOption, "portable",
        UpdaterArguments.PackageOption, "/tmp/pkg.zip",
        UpdaterArguments.InstallDirOption, "/tmp/app",
        UpdaterArguments.ExeOption, "Cafe.Launcher.Avalonia.exe",
        UpdaterArguments.ParentPidOption, "1234",
        UpdaterArguments.Sha256Option, ValidSha,
        UpdaterArguments.LogOption, "/tmp/log"
    ];
}
