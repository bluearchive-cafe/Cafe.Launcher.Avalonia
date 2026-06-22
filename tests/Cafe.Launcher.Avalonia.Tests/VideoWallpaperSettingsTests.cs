using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.ViewModels;
using Xunit;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class VideoWallpaperSettingsTests
{
    static VideoWallpaperSettingsTests()
    {
        TestLocalizationHelper.Initialize();
    }


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
    public void NormalizeForTesting_WhenVideoSource_KeepsVideoSource()
    {
        var result = LauncherSettingsService
            .NormalizeForTesting(new LauncherSettings { BackgroundSource = BackgroundSources.Video });

        Assert.Equal(BackgroundSources.Video, result.BackgroundSource);
    }

    [Theory]
    [InlineData(-10, 0)]
    [InlineData(150, 100)]
    [InlineData(60, 60)]
    public void NormalizeForTesting_WhenVolumeOutOfRange_ClampsToValidRange(int input, int expected)
    {
        var result = LauncherSettingsService
            .NormalizeForTesting(new LauncherSettings { VideoBackgroundVolume = input });

        Assert.Equal(expected, result.VideoBackgroundVolume);
    }

    [Fact]
    public void NormalizeForTesting_WhenPathHasWhitespace_TrimsPath()
    {
        var result = LauncherSettingsService
            .NormalizeForTesting(new LauncherSettings { VideoBackgroundPath = "  C:\\v.mp4  " });

        Assert.Equal("C:\\v.mp4", result.VideoBackgroundPath);
    }

    [Fact]
    public void OptionsViewModel_BackgroundSource_IncludesVideo()
    {
        var options = new SettingsOptionsViewModel(
            new LocalizationService(),
            new DiskSpaceService());

        Assert.Contains(options.BackgroundSource, o => o.Code == BackgroundSources.Video);
    }

    [Fact]
    public void AppearanceViewModel_Load_SetsIsVideoBackgroundSelected()
    {
        var editor = new SettingsEditor();
        editor.ApplySnapshot(new LauncherSettings { BackgroundSource = BackgroundSources.Video });
        using var vm = new SettingsAppearanceViewModel(editor);

        vm.Load(editor.Current);

        Assert.True(vm.IsVideoBackgroundSelected);
    }

    [Fact]
    public void AppearanceViewModel_Load_SetsVideoVolumeAndMuted()
    {
        var editor = new SettingsEditor();
        editor.ApplySnapshot(new LauncherSettings
        {
            VideoBackgroundVolume = 75,
            VideoBackgroundMuted = false
        });
        using var vm = new SettingsAppearanceViewModel(editor);

        vm.Load(editor.Current);

        Assert.Equal(75, vm.VideoVolume);
        Assert.False(vm.IsVideoMuted);
    }

    [Fact]
    public void AppearanceViewModel_Load_DoesNotPushVideoFieldsToEditor()
    {
        var editor = new SettingsEditor();
        editor.ApplySnapshot(new LauncherSettings
        {
            VideoBackgroundVolume = 75,
            VideoBackgroundMuted = false
        });
        using var vm = new SettingsAppearanceViewModel(editor);

        vm.Load(editor.Current);

        // Load should not mark editor dirty
        Assert.False(editor.IsDirty);
    }

    [Fact]
    public void AppearanceViewModel_OnBackgroundSourceChanged_UpdatesIsVideoBackgroundSelected()
    {
        var editor = new SettingsEditor();
        editor.ApplySnapshot(new LauncherSettings { BackgroundSource = BackgroundSources.Bundled });
        using var vm = new SettingsAppearanceViewModel(editor);
        vm.Load(editor.Current);

        editor.Commit(s => s.BackgroundSource = BackgroundSources.Video);

        Assert.True(vm.IsVideoBackgroundSelected);
    }

    [Fact]
    public void AppearanceViewModel_VideoVolumeChange_PushesToEditor()
    {
        var editor = new SettingsEditor();
        editor.ApplySnapshot(new LauncherSettings { VideoBackgroundVolume = 50 });
        using var vm = new SettingsAppearanceViewModel(editor);
        vm.Load(editor.Current);

        vm.VideoVolume = 80;

        Assert.Equal(80, editor.Current.VideoBackgroundVolume);
    }

    [Fact]
    public void AppearanceViewModel_IsVideoMutedChange_PushesToEditor()
    {
        var editor = new SettingsEditor();
        editor.ApplySnapshot(new LauncherSettings { VideoBackgroundMuted = true });
        using var vm = new SettingsAppearanceViewModel(editor);
        vm.Load(editor.Current);

        vm.IsVideoMuted = false;

        Assert.False(editor.Current.VideoBackgroundMuted);
    }
}
