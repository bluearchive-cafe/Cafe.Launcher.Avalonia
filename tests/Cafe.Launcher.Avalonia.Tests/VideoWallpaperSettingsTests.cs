using Cafe.Launcher.Avalonia.Models;
using Xunit;

namespace Cafe.Launcher.Avalonia.Tests;

public class VideoWallpaperSettingsTests
{
    [Fact]
    public void BackgroundSources_Video_HasExpectedCode()
    {
        Assert.Equal("video", BackgroundSources.Video);
    }

    [Fact]
    public void LauncherSettings_VideoDefaults_AreMutedHalfVolumeEmptyPath()
    {
        var settings = new LauncherSettings();

        Assert.Equal("", settings.VideoBackgroundPath);
        Assert.True(settings.VideoBackgroundMuted);
        Assert.Equal(50, settings.VideoBackgroundVolume);
    }

    [Fact]
    public void LauncherSettings_DeepClone_PreservesVideoFields()
    {
        var settings = new LauncherSettings
        {
            VideoBackgroundPath = @"C:\videos\bg.mp4",
            VideoBackgroundMuted = false,
            VideoBackgroundVolume = 80,
        };

        var clone = settings.DeepClone();

        Assert.Equal(@"C:\videos\bg.mp4", clone.VideoBackgroundPath);
        Assert.False(clone.VideoBackgroundMuted);
        Assert.Equal(80, clone.VideoBackgroundVolume);
    }
}
