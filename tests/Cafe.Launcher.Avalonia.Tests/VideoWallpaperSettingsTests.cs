using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Xunit;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class VideoWallpaperSettingsTests
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

    [Fact]
    public void Normalize_KeepsVideoBackgroundSource()
    {
        var result = LauncherSettingsService
            .NormalizeForTesting(new LauncherSettings { BackgroundSource = BackgroundSources.Video });

        Assert.Equal(BackgroundSources.Video, result.BackgroundSource);
    }

    [Theory]
    [InlineData(-10, 0)]
    [InlineData(150, 100)]
    [InlineData(60, 60)]
    public void Normalize_ClampsVideoVolume(int input, int expected)
    {
        var result = LauncherSettingsService
            .NormalizeForTesting(new LauncherSettings { VideoBackgroundVolume = input });

        Assert.Equal(expected, result.VideoBackgroundVolume);
    }

    [Fact]
    public void Normalize_TrimsVideoPath()
    {
        var result = LauncherSettingsService
            .NormalizeForTesting(new LauncherSettings { VideoBackgroundPath = "  C:\\v.mp4  " });

        Assert.Equal("C:\\v.mp4", result.VideoBackgroundPath);
    }
}
