using System;
using System.IO;
using Cafe.Launcher.Avalonia.Features.GameOperations;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class GameLaunchTargetResolutionTests : IDisposable
{
    private readonly string tempDirectory;

    public GameLaunchTargetResolutionTests()
    {
        tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
    }

    public void Dispose()
    {
        Directory.Delete(tempDirectory, recursive: true);
    }

    [Fact]
    public void Resolve_WhenLocalExecutableExists_ReturnsTargetFromLocalInstallation()
    {
        var gameDirectory = Directory.CreateDirectory(Path.Combine(tempDirectory, "game")).FullName;
        File.WriteAllText(Path.Combine(gameDirectory, "LocalGame.exe"), string.Empty);
        var snapshot = new LauncherStatusSnapshot
        {
            LocalGame = new LocalInstallationState
            {
                GamePath = gameDirectory,
                GameConfig = new GameLauncherConfig
                {
                    Name = "LocalGame",
                    Params = ["-windowed"]
                }
            }
        };

        var resolution = GameLaunchTargetResolution.Resolve(snapshot);

        Assert.True(resolution.Resolved);
        var target = resolution.Target!;
        Assert.Equal("LocalGame", target.ExecutableName);
        Assert.Equal(Path.Combine(gameDirectory, "LocalGame.exe"), target.ExecutablePath);
        Assert.Equal(gameDirectory, target.WorkingDirectory);
        Assert.Equal(["-windowed"], target.Arguments);
    }

    [Fact]
    public void Resolve_WhenLocalNameMissing_DoesNotFallBackToRemoteGameStartExeName()
    {
        var gameDirectory = Directory.CreateDirectory(Path.Combine(tempDirectory, "game")).FullName;
        File.WriteAllText(Path.Combine(gameDirectory, "RemoteGame.exe"), string.Empty);
        var snapshot = new LauncherStatusSnapshot
        {
            LocalGame = new LocalInstallationState
            {
                GamePath = gameDirectory,
                GameConfig = new GameLauncherConfig()
            },
            Remote = new LauncherRemoteState
            {
                GameConfig = new GameConfigResponse { GameStartExeName = "RemoteGame" }
            }
        };

        var resolution = GameLaunchTargetResolution.Resolve(snapshot);

        Assert.False(resolution.Resolved);
        Assert.Equal(GameLaunchTargetStatus.ExecutableNameEmpty, resolution.Status);
    }

    [Fact]
    public void Resolve_WhenGamePathMissing_ReportsExecutableNameEmpty()
    {
        var snapshot = new LauncherStatusSnapshot
        {
            LocalGame = new LocalInstallationState
            {
                GameConfig = new GameLauncherConfig { Name = "LocalGame" }
            }
        };

        var resolution = GameLaunchTargetResolution.Resolve(snapshot);

        Assert.False(resolution.Resolved);
        Assert.Equal(GameLaunchTargetStatus.ExecutableNameEmpty, resolution.Status);
    }

    [Fact]
    public void Resolve_WhenNameContainsSeparator_RejectsResolution()
    {
        var gameDirectory = Directory.CreateDirectory(Path.Combine(tempDirectory, "game")).FullName;
        var snapshot = new LauncherStatusSnapshot
        {
            LocalGame = new LocalInstallationState
            {
                GamePath = gameDirectory,
                GameConfig = new GameLauncherConfig { Name = "..\\evil" }
            }
        };

        var resolution = GameLaunchTargetResolution.Resolve(snapshot);

        Assert.False(resolution.Resolved);
        Assert.Equal(GameLaunchTargetStatus.ExecutableNameInvalid, resolution.Status);
    }

    [Fact]
    public void Resolve_WhenExecutableMissingOnDisk_ReportsExpectedExecutablePath()
    {
        var gameDirectory = Directory.CreateDirectory(Path.Combine(tempDirectory, "game")).FullName;
        var snapshot = new LauncherStatusSnapshot
        {
            LocalGame = new LocalInstallationState
            {
                GamePath = gameDirectory,
                GameConfig = new GameLauncherConfig { Name = "AbsentGame" }
            }
        };

        var resolution = GameLaunchTargetResolution.Resolve(snapshot);

        Assert.False(resolution.Resolved);
        Assert.Equal(GameLaunchTargetStatus.ExecutableMissing, resolution.Status);
        Assert.Equal(Path.Combine(gameDirectory, "AbsentGame.exe"), resolution.ExpectedExecutablePath);
    }
}
