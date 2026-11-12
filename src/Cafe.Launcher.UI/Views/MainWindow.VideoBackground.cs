using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Cafe.Launcher.UI.Controls.VideoBackground;
using Cafe.Launcher.UI.ViewModels;

namespace Cafe.Launcher.UI.Views;

internal partial class MainWindow
{
    private VideoBackgroundControl? videoBackground;
    private CacheMode? previousShellCache;
    private bool shellCachedForVideo;

    private void InitializeVideoBackground()
    {
        PropertyChanged += OnVideoWindowPropertyChanged;
        Closed += (_, _) => RemoveVideoBackground(detach: true);
    }

    private void OnVideoWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty || e.Property == WindowStateProperty || e.Property == IsActiveProperty)
            UpdateVideoPlayback();
    }

    private void OnVideoBackgroundSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BackgroundViewModel.BackgroundVideoPath)) UpdateVideoBackground();
        else if (e.PropertyName is nameof(BackgroundViewModel.BackgroundVideoVolume)
                 or nameof(BackgroundViewModel.BackgroundStretch)
                 or nameof(BackgroundViewModel.BackgroundFillBrush)) UpdateVideoPlayback();
    }

    private void UpdateVideoBackground()
    {
        var background = configuredViewModel?.Background;
        var path = background?.BackgroundVideoPath;
        if (path is null)
        {
            RemoveVideoBackground();
            return;
        }
        if (videoBackground is null)
        {
            var library = VideoBackgroundRuntime.FindLibraryPath();
            if (library is null)
            {
                background!.ReportVideoFailure(path, new DllNotFoundException("Video background runtime is unavailable."));
                return;
            }
            videoBackground = new VideoBackgroundControl(library);
            videoBackground.FirstFrameReady += OnVideoBackgroundFirstFrame;
            videoBackground.PlaybackFailed += OnVideoBackgroundFailed;
            videoBackground.Source = path;
            UpdateVideoPlayback();
            // A zero-opacity OpenGlControlBase is culled before GL initialization.
            // Keep it drawable behind the opaque fallback until the first frame.
            ShellRoot.Children.Insert(1, videoBackground);
        }
        else
        {
            // Preserve the GL context and render surfaces across draft/source changes.
            if (videoBackground.Source != path) ShowVideoFallback();
            videoBackground.Source = path;
            UpdateVideoPlayback();
        }
    }

    private void UpdateVideoPlayback()
    {
        if (videoBackground is null || configuredViewModel is not { } vm) return;
        var unavailable = new VideoPlaybackConditions(
            videoBackground.Source is not null, IsVisible, WindowState, IsActive).IsUnavailable;
        // Reduced motion pauses playback but keeps the chosen video visible as a still frame.
        videoBackground.PlaybackPaused = unavailable || vm.IsMotionReduced;
        videoBackground.ShowStillFrame = !unavailable && vm.IsMotionReduced;
        videoBackground.Volume = vm.Background.BackgroundVideoVolume;
        videoBackground.Stretch = vm.Background.BackgroundStretch;
        videoBackground.FillColor = vm.Background.BackgroundFillBrush is ISolidColorBrush brush ? brush.Color : Colors.Black;
    }

    private void OnVideoBackgroundFirstFrame()
    {
        if (videoBackground is null) return;
        // The opaque video covers these images completely; avoid drawing/resampling
        // an invisible full-window wallpaper on every video frame.
        foreach (var image in ShellRoot.Children.OfType<Image>()) image.IsVisible = false;
        BackgroundCrossFade.IsVisible = false;
        if (!shellCachedForVideo)
        {
            previousShellCache = MainContentLayer.CacheMode;
            MainContentLayer.CacheMode = new BitmapCache();
            shellCachedForVideo = true;
        }
    }

    private void OnVideoBackgroundFailed(string path, Exception exception) =>
        configuredViewModel?.Background.ReportVideoFailure(path, exception);

    private void RemoveVideoBackground(bool detach = false)
    {
        if (videoBackground is not null)
        {
            videoBackground.PlaybackPaused = true;
            videoBackground.ShowStillFrame = false;
            videoBackground.Source = null;
            if (detach)
            {
                videoBackground.FirstFrameReady -= OnVideoBackgroundFirstFrame;
                videoBackground.PlaybackFailed -= OnVideoBackgroundFailed;
                // 窗口关闭才分离承载面；切回图片只释放 libmpv，并停止帧请求。
                ShellRoot.Children.Remove(videoBackground);
                videoBackground = null;
            }
        }
        ShowVideoFallback();
        if (detach && shellCachedForVideo)
        {
            // 缓存随视频控件存活，而不是每次切回静态背景就恢复：每轮新建全窗口
            // BitmapCache 会留下合成器侧的帧缓冲高水位（AUD-PERF-009）。
            MainContentLayer.CacheMode = previousShellCache;
            shellCachedForVideo = false;
        }
    }

    private void ShowVideoFallback()
    {
        foreach (var image in ShellRoot.Children.OfType<Image>()) image.IsVisible = true;
        BackgroundCrossFade.IsVisible = true;
    }
}

/// <summary>
/// 视频播放可用性：失焦同样暂停——渲染栈按已绘帧保留内存（AUD-PERF-009），
/// 只应在用户实际观看时增长。
/// </summary>
internal readonly record struct VideoPlaybackConditions(
    bool HasSource,
    bool IsVisible,
    WindowState State,
    bool IsActive)
{
    public bool IsUnavailable =>
        !HasSource || !IsVisible || State == WindowState.Minimized || !IsActive;
}
