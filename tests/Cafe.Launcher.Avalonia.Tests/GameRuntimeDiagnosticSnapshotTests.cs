using System;
using Cafe.Launcher.Avalonia.Services.GameRuntime;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class GameRuntimeDiagnosticSnapshotTests
{
    [Fact]
    public void Describe_IncludesAllProvidedRuntimeFacts()
    {
        var snapshot = new GameRuntimeDiagnosticSnapshot(
            RunnerId: "umu",
            RunnerVersion: "1.4.4",
            RunnerExecutable: "/usr/bin/umu-run",
            PrefixPath: "/home/user/.local/share/cafe-launcher/compatibility/blue-archive-jp/umu/prefix",
            ProtonPath: "auto",
            GameId: "blue-archive-jp",
            GameExecutable: "/home/user/Games/BlueArchive/BlueArchive.exe",
            WorkingDirectory: "/home/user/Games/BlueArchive");

        var description = snapshot.Describe();

        Assert.Contains("[GameRuntime]", description, StringComparison.Ordinal);
        Assert.Contains("Runner: umu", description, StringComparison.Ordinal);
        Assert.Contains("RunnerVersion: 1.4.4", description, StringComparison.Ordinal);
        Assert.Contains("Executable: /usr/bin/umu-run", description, StringComparison.Ordinal);
        Assert.Contains("GameId: blue-archive-jp", description, StringComparison.Ordinal);
        Assert.Contains(
            "GameExecutable: /home/user/Games/BlueArchive/BlueArchive.exe",
            description,
            StringComparison.Ordinal);
        Assert.Contains("Prefix: /home/user/.local/share/cafe-launcher/compatibility/blue-archive-jp/umu/prefix",
            description,
            StringComparison.Ordinal);
        Assert.Contains("Proton: auto", description, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_OmitsEntriesThatDoNotApplyToTheRunner()
    {
        var snapshot = new GameRuntimeDiagnosticSnapshot(
            RunnerId: "native",
            RunnerVersion: null,
            RunnerExecutable: null,
            PrefixPath: null,
            ProtonPath: null,
            GameId: "blue-archive-jp",
            GameExecutable: @"C:\Games\BlueArchive\BlueArchive.exe",
            WorkingDirectory: @"C:\Games\BlueArchive");

        var description = snapshot.Describe();

        // "Executable:" is a substring of "GameExecutable:", so the omission
        // assertions must anchor on the preceding line break.
        Assert.DoesNotContain($"{Environment.NewLine}RunnerVersion:", description, StringComparison.Ordinal);
        Assert.DoesNotContain($"{Environment.NewLine}Executable:", description, StringComparison.Ordinal);
        Assert.DoesNotContain($"{Environment.NewLine}Prefix:", description, StringComparison.Ordinal);
        Assert.DoesNotContain($"{Environment.NewLine}Proton:", description, StringComparison.Ordinal);
        Assert.Contains("Runner: native", description, StringComparison.Ordinal);
        Assert.Contains("GameId: blue-archive-jp", description, StringComparison.Ordinal);
    }
}
