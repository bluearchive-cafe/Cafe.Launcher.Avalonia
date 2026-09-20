using System.IO;
using System.Text;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Testing;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class SystemAnimationSettingsProviderTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GetSystemAnimationsEnabled_WhenReadSucceeds_ReturnsValue(bool enabled)
    {
        var provider = new SystemAnimationSettingsProvider(() => enabled);

        Assert.Equal(enabled, provider.GetSystemAnimationsEnabled());
    }

    [Fact]
    public void GetSystemAnimationsEnabled_WhenReadFails_ReturnsNull()
    {
        var provider = new SystemAnimationSettingsProvider(() => null);

        Assert.Null(provider.GetSystemAnimationsEnabled());
    }

    [Theory]
    [InlineData("0", false)]
    [InlineData("0.0", false)]
    [InlineData("1", true)]
    [InlineData("2", true)]
    [InlineData("0.5", true)]
    public void TryReadKdeAnimationEnabled_WhenFactorPresentUnderKdeSection_ParsesFactor(
        string factor,
        bool expectedEnabled)
    {
        using var directory = TestDirectory.Create();
        var path = Path.Combine(directory, "kdeglobals");
        File.WriteAllText(
            path,
            $"[General]\nXftHintStyle=hintslight\n\n[KDE]\nAnimationDurationFactor={factor}\n",
            new UTF8Encoding(false));

        Assert.Equal(expectedEnabled, SystemAnimationSettingsProvider.TryReadKdeAnimationEnabled(path));
    }

    [Fact]
    public void TryReadKdeAnimationEnabled_WhenKeyInOtherSection_ReturnsNull()
    {
        using var directory = TestDirectory.Create();
        var path = Path.Combine(directory, "kdeglobals");
        File.WriteAllText(
            path,
            "[General]\nAnimationDurationFactor=0\n",
            new UTF8Encoding(false));

        Assert.Null(SystemAnimationSettingsProvider.TryReadKdeAnimationEnabled(path));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("1", true)]
    [InlineData("0", false)]
    public void TryReadGtkAnimationEnabled_WhenKeyPresentUnderSettingsSection_ParsesValue(
        string value,
        bool expectedEnabled)
    {
        using var directory = TestDirectory.Create();
        var path = Path.Combine(directory.Sub("gtk-4.0"), "settings.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(
            path,
            $"[Settings]\ngtk-theme=Adwaita\ngtk-enable-animations={value}\n",
            new UTF8Encoding(false));

        Assert.Equal(expectedEnabled, SystemAnimationSettingsProvider.TryReadGtkAnimationEnabled(path));
    }

    [Fact]
    public void TryReadGtkAnimationEnabled_WhenKeyMissing_ReturnsNull()
    {
        using var directory = TestDirectory.Create();
        var path = Path.Combine(directory.Sub("gtk-3.0"), "settings.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(
            path,
            "[Settings]\ngtk-theme=Adwaita\n",
            new UTF8Encoding(false));

        Assert.Null(SystemAnimationSettingsProvider.TryReadGtkAnimationEnabled(path));
    }

    [Fact]
    public void TryReadKdeAnimationEnabled_WhenFileMissing_ReturnsNull()
    {
        using var directory = TestDirectory.Create();
        var path = Path.Combine(directory, "kdeglobals");

        Assert.Null(SystemAnimationSettingsProvider.TryReadKdeAnimationEnabled(path));
    }

    [Fact]
    public void TryReadGtkAnimationEnabled_WhenFileMissing_ReturnsNull()
    {
        using var directory = TestDirectory.Create();
        var path = Path.Combine(directory.Sub("gtk-3.0"), "settings.ini");

        Assert.Null(SystemAnimationSettingsProvider.TryReadGtkAnimationEnabled(path));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("true\n", true)]
    [InlineData("  true  ", true)]
    [InlineData("b'true'", null)]
    [InlineData("", null)]
    public void ParseGSettingsOutput_WithProbeOutput_ParsesBoolean(
        string output,
        bool? expectedEnabled)
    {
        Assert.Equal(expectedEnabled, SystemAnimationSettingsProvider.ParseGSettingsOutput(output));
    }
}
