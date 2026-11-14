using System.ComponentModel;
using System.Diagnostics;
using Cafe.Launcher.Core.Services;

namespace Cafe.Launcher.Tests;

public sealed class ShellFolderOpenerTests
{
    [Fact]
    public void OpenInFileManager_WhenProcessStarts_PassesPlatformTargetAndReturnsTrue()
    {
        ProcessStartInfo? captured = null;

        var opened = ShellFolderOpener.OpenInFileManager("/data/cafe-launcher", startInfo =>
        {
            captured = startInfo;
            return new Process();
        });

        Assert.True(opened);
        Assert.NotNull(captured);
        Assert.True(captured.UseShellExecute);
        Assert.Equal(OperatingSystem.IsWindows() ? "explorer.exe" : "/data/cafe-launcher", captured.FileName);
    }

    [Fact]
    public void OpenInFileManager_WhenShellReturnsNoProcess_ReturnsFalse()
    {
        Assert.False(ShellFolderOpener.OpenInFileManager("/data/cafe-launcher", _ => null));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(13)]
    public void OpenInFileManager_WhenShellIsMissingOrDenied_ReturnsFalse(int error)
    {
        Assert.False(ShellFolderOpener.OpenInFileManager("/data/cafe-launcher", _ => throw new Win32Exception(error)));
    }

    [Fact]
    public void OpenInFileManager_WhenShellLaunchIsUnavailable_ReturnsFalse()
    {
        Assert.False(ShellFolderOpener.OpenInFileManager("/data/cafe-launcher", _ => throw new InvalidOperationException()));
        Assert.False(ShellFolderOpener.OpenInFileManager("/data/cafe-launcher", _ => throw new IOException()));
        Assert.False(ShellFolderOpener.OpenInFileManager("/data/cafe-launcher", _ => throw new UnauthorizedAccessException()));
        Assert.False(ShellFolderOpener.OpenInFileManager("/data/cafe-launcher", _ => throw new PlatformNotSupportedException()));
    }
}
