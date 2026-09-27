using System.Globalization;
using Cafe.Launcher.Updater.Core;

namespace Cafe.Launcher.Tests;

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
        Assert.Equal("Cafe.Launcher.exe", arguments.ExecutableName);
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
        SetOption(arguments, UpdaterArguments.ModeOption, mode);

        Assert.True(UpdaterArguments.TryParse(arguments, out var parsed, out var error), error);
        Assert.Equal(expected, parsed!.Mode);
    }

    [Fact]
    public void TryParse_WhenAnOptionHasNoValue_Fails()
    {
        Assert.False(UpdaterArguments.TryParse([UpdaterArguments.ApplyCommand, "--mode"], out _, out var error));
        Assert.Contains("missing a value", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryParse_WhenCommandIsUnknown_Fails()
    {
        var arguments = ValidArguments();
        arguments[0] = "inspect";

        Assert.False(UpdaterArguments.TryParse(arguments, out _, out var error));
        Assert.Contains("Unknown command", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryParse_WhenOptionIsUnknown_Fails()
    {
        var arguments = ValidArguments();
        arguments[1] = "--nope";

        Assert.False(UpdaterArguments.TryParse(arguments, out _, out var error));
        Assert.Contains("Unknown option", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryParse_WhenModeIsUnknown_Fails()
    {
        var arguments = ValidArguments();
        SetOption(arguments, UpdaterArguments.ModeOption, "sideload");

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
        SetOption(arguments, UpdaterArguments.ParentPidOption, pid);

        Assert.False(UpdaterArguments.TryParse(arguments, out _, out var error));
        Assert.Contains("parent process id", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryParse_WhenSha256IsInvalid_Fails()
    {
        var arguments = ValidArguments();
        SetOption(arguments, UpdaterArguments.Sha256Option, "not-a-hash");

        Assert.False(UpdaterArguments.TryParse(arguments, out _, out var error));
        Assert.Contains("SHA-256", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("2")]
    [InlineData("next")]
    public void TryParse_WhenProtocolVersionIsUnsupported_Fails(string protocolVersion)
    {
        var arguments = ValidArguments();
        SetOption(arguments, UpdaterArguments.ProtocolVersionOption, protocolVersion);

        Assert.False(UpdaterArguments.TryParse(arguments, out _, out var error));
        Assert.Contains("Unsupported updater protocol version", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryParse_WhenProtocolVersionIsMissing_Fails()
    {
        var arguments = ValidArguments()
            .Where(value => value != UpdaterArguments.ProtocolVersionOption
                && value != UpdaterArguments.CurrentProtocolVersion.ToString(CultureInfo.InvariantCulture))
            .ToArray();

        Assert.False(UpdaterArguments.TryParse(arguments, out _, out var error));
        Assert.Contains(UpdaterArguments.ProtocolVersionOption, error, StringComparison.Ordinal);
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
        UpdaterArguments.ApplyCommand,
        UpdaterArguments.ProtocolVersionOption,
        UpdaterArguments.CurrentProtocolVersion.ToString(CultureInfo.InvariantCulture),
        UpdaterArguments.ModeOption, "portable",
        UpdaterArguments.PackageOption, "/tmp/pkg.zip",
        UpdaterArguments.InstallDirOption, "/tmp/app",
        UpdaterArguments.ExeOption, "Cafe.Launcher.exe",
        UpdaterArguments.ParentPidOption, "1234",
        UpdaterArguments.Sha256Option, ValidSha,
        UpdaterArguments.LogOption, "/tmp/log"
    ];

    private static void SetOption(string[] arguments, string option, string value)
    {
        var optionIndex = Array.IndexOf(arguments, option);
        Assert.True(optionIndex >= 0, $"Option '{option}' was not present in the test arguments.");
        arguments[optionIndex + 1] = value;
    }
}
