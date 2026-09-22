using Cafe.Launcher.Avalonia.Services.Update;
using Cafe.Launcher.Avalonia.Testing;

namespace Cafe.Launcher.Avalonia.Tests;

/// <summary>
/// The Inno installer writes the ownership marker that decides installed-vs-portable;
/// the constant the applier reads must match the literal in the installer script, or
/// a self-update would pick the wrong package on an installed build.
/// </summary>
public sealed class LauncherUpdateHostInfoProviderContractTests
{
    [Fact]
    public void InstallMarker_MatchesTheInstallerScriptLiteral()
    {
        var installerScript = File.ReadAllText(
            Path.Combine(TestRepository.Root, "installer", "Cafe.Launcher.Avalonia.iss"));

        Assert.Contains(
            LauncherUpdateHostInfoProvider.InstallMarkerFileName,
            installerScript,
            StringComparison.Ordinal);
    }
}
