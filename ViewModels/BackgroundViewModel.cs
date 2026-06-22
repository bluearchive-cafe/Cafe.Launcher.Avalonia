using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.Services.VideoWallpaper;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cafe.Launcher.Avalonia.ViewModels;

public partial class BackgroundViewModel : ViewModelBase, IDisposable
{
    private readonly ImageCacheService imageCacheService;
    private readonly LocalDiagnostics diagnostics;
    private readonly Action<LauncherSettings> wallpaperChanged;
    private readonly Func<string, IImage?> imageLoader;
    private readonly Func<IImage?> bundledImageLoader;
    private readonly Func<IVideoWallpaperEngine> engineFactory;
    private IVideoWallpaperEngine? videoEngine;
    private LauncherSettings? activeVideoSettings;
    private bool playbackActive = true;
    private bool disposed;

    [ObservableProperty]
    private IImage? backgroundImageSource;

    [ObservableProperty]
    private Stretch backgroundStretch = Stretch.UniformToFill;

    [ObservableProperty]
    private IBrush? backgroundFillBrush;

    public Func<Task<string?>>? PickBackgroundImageAsync { get; set; }

    public Func<Task<string?>>? PickBackgroundFolderAsync { get; set; }

    public string BackgroundImagePickerTitle { get; set; } = "";

    public string BackgroundFolderPickerTitle { get; set; } = "";

    public BackgroundViewModel(
        ImageCacheService imageCacheService,
        LocalDiagnostics diagnostics,
        SettingsViewModel settings)
        : this(
            imageCacheService,
            diagnostics,
            previewSettings =>
            {
                settings.Appearance.RefreshThemeColorPaletteFromCurrentBackground(markDirty: false);
                settings.Appearance.ApplyThemeColor(
                    previewSettings.ThemeColorMode,
                    settings.Appearance.SelectedCustomThemeColor);
            })
    {
    }

    internal BackgroundViewModel(
        ImageCacheService imageCacheService,
        LocalDiagnostics diagnostics,
        Action<LauncherSettings> wallpaperChanged)
        : this(
            imageCacheService,
            diagnostics,
            wallpaperChanged,
            static path => new Bitmap(path),
            LoadBundledBackground,
            static () => new NullVideoWallpaperEngine())
    {
    }

    internal BackgroundViewModel(
        ImageCacheService imageCacheService,
        LocalDiagnostics diagnostics,
        Action<LauncherSettings> wallpaperChanged,
        Func<string, IImage?> imageLoader,
        Func<IImage?> bundledImageLoader)
        : this(
            imageCacheService,
            diagnostics,
            wallpaperChanged,
            imageLoader,
            bundledImageLoader,
            static () => new NullVideoWallpaperEngine())
    {
    }

    internal BackgroundViewModel(
        ImageCacheService imageCacheService,
        LocalDiagnostics diagnostics,
        Action<LauncherSettings> wallpaperChanged,
        Func<string, IImage?> imageLoader,
        Func<IImage?> bundledImageLoader,
        Func<IVideoWallpaperEngine> engineFactory)
    {
        this.imageCacheService = imageCacheService;
        this.diagnostics = diagnostics;
        this.wallpaperChanged = wallpaperChanged;
        this.imageLoader = imageLoader;
        this.bundledImageLoader = bundledImageLoader;
        this.engineFactory = engineFactory;
        backgroundImageSource = bundledImageLoader();
    }

    public async Task UpdateBackgroundImageAsync(
        LauncherSettings settings,
        LauncherStatusSnapshot? snapshot,
        CancellationToken cancellationToken)
    {
        ApplyBackgroundPresentation(settings);

        if (settings.BackgroundSource != BackgroundSources.Video)
        {
            StopVideo();
        }

        switch (settings.BackgroundSource)
        {
            case BackgroundSources.Video:
                if (!string.IsNullOrWhiteSpace(settings.VideoBackgroundPath))
                {
                    if (await TryStartVideoAsync(settings, cancellationToken))
                    {
                        return;
                    }
                }
                StopVideo();
                break;

            case BackgroundSources.Remote:
                var bgImg = snapshot?.Remote.BaseConfig?.LauncherBackgroundImg;
                var crc64 = snapshot?.Remote.BaseConfig?.LauncherBackgroundImgCrc64;
                if (!string.IsNullOrWhiteSpace(bgImg) && !string.IsNullOrWhiteSpace(crc64))
                {
                    try
                    {
                        var proxyMode = snapshot?.Settings.ProxyMode ?? ProxyModes.Direct;
                        var cachedPath = await imageCacheService.GetCachedPathAsync(crc64, cancellationToken)
                            ?? await imageCacheService.CacheImageAsync(bgImg, crc64, proxyMode, cancellationToken);
                        cancellationToken.ThrowIfCancellationRequested();
                        SetBackgroundImage(imageLoader(cachedPath), settings);
                        return;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _ = diagnostics.MessageAsync(
                            "Remote background image download failed",
                            $"url: {bgImg}\ncrc64: {crc64}\nexception: {ex.Message}",
                            CancellationToken.None);
                    }
                }
                break;

            case BackgroundSources.Custom:
                if (!string.IsNullOrWhiteSpace(settings.CustomBackgroundPath))
                {
                    var customBitmap = await LoadCustomBackgroundImageAsync(
                        settings.CustomBackgroundPath,
                        cancellationToken);
                    if (customBitmap is not null)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        SetBackgroundImage(customBitmap, settings);
                        return;
                    }
                }
                break;
        }

        cancellationToken.ThrowIfCancellationRequested();
        SetBackgroundImage(bundledImageLoader(), settings);
    }

    public void ApplyBackgroundPresentation(LauncherSettings settings)
    {
        BackgroundStretch = ToStretch(settings.BackgroundFit);
        BackgroundFillBrush = settings.BackgroundFit == BackgroundFits.Uniform
            ? new SolidColorBrush(SettingsAppearanceViewModel.ParseColorOrDefault(settings.BackgroundFillColor))
            : null;
    }

    public Bitmap? GetBackgroundBitmap()
    {
        return videoEngine?.CaptureFrame() ?? BackgroundImageSource as Bitmap;
    }

    public async Task<Bitmap?> LoadCustomBackgroundAsync(
        string path,
        CancellationToken cancellationToken = default) =>
        await LoadCustomBackgroundImageAsync(path, cancellationToken) as Bitmap;

    private async Task<IImage?> LoadCustomBackgroundImageAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(path))
        {
            try
            {
                var bitmap = imageLoader(path);
                if (bitmap is null)
                {
                    return null;
                }
                if (cancellationToken.IsCancellationRequested)
                {
                    (bitmap as IDisposable)?.Dispose();
                    cancellationToken.ThrowIfCancellationRequested();
                }

                return bitmap;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                await diagnostics.MessageAsync(
                    "Custom background image load failed",
                    $"path: {path}\nexception: {ex.Message}",
                    CancellationToken.None);
                return null;
            }
        }

        if (Directory.Exists(path))
        {
            string? imagePath;
            try
            {
                imagePath = ResolveRandomBackgroundImage(path);
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                await diagnostics.MessageAsync(
                    "Custom background folder scan failed",
                    $"path: {path}\nexception: {ex.Message}",
                    CancellationToken.None);
                return null;
            }

            if (imagePath is null)
            {
                await diagnostics.MessageAsync(
                    "Custom background folder contains no supported images",
                    $"path: {path}",
                    CancellationToken.None);
                return null;
            }

            try
            {
                var bitmap = imageLoader(imagePath);
                if (bitmap is null)
                {
                    return null;
                }
                if (cancellationToken.IsCancellationRequested)
                {
                    (bitmap as IDisposable)?.Dispose();
                    cancellationToken.ThrowIfCancellationRequested();
                }

                return bitmap;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                await diagnostics.MessageAsync(
                    "Custom background folder image load failed",
                    $"folder: {path}\npath: {imagePath}\nexception: {ex.Message}",
                    CancellationToken.None);
                return null;
            }
        }

        await diagnostics.MessageAsync(
            "Custom background path does not exist",
            $"path: {path}",
            CancellationToken.None);
        return null;
    }

    public static string? ResolveRandomBackgroundImage(string folderPath)
    {
        if (!Directory.Exists(folderPath))
            return null;

        var imagePaths = Directory
            .EnumerateFiles(folderPath)
            .Where(IsSupportedBackgroundImage)
            .ToArray();

        return imagePaths.Length == 0
            ? null
            : imagePaths[Random.Shared.Next(imagePaths.Length)];
    }

    public static bool IsSupportedBackgroundImage(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".webp", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> TryStartVideoAsync(
        LauncherSettings settings,
        CancellationToken cancellationToken)
    {
        try
        {
            StopVideo();
            var engine = engineFactory();
            var ok = await engine.LoadAsync(settings.VideoBackgroundPath, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!ok)
            {
                engine.Dispose();
                return false;
            }

            videoEngine = engine;
            activeVideoSettings = settings;
            engine.SetVolume(settings.VideoBackgroundVolume);
            engine.SetMuted(settings.VideoBackgroundMuted);
            engine.FrameReady += OnVideoFrameReady;
            if (playbackActive)
            {
                engine.Play();
            }

            OnVideoFrameReady();
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _ = diagnostics.MessageAsync(
                "Video wallpaper load failed",
                $"path: {settings.VideoBackgroundPath}\nexception: {ex.Message}",
                CancellationToken.None);
            return false;
        }
    }

    private void OnVideoFrameReady()
    {
        var frame = videoEngine?.CurrentFrame;
        if (frame is not null && activeVideoSettings is not null)
        {
            // 直接赋值，不走 SetBackgroundImage（后者会 Dispose 旧帧）；视频帧由引擎双缓冲拥有。
            BackgroundImageSource = frame;
            if (activeVideoSettings.ThemeColorMode == ThemeColorModes.Wallpaper)
            {
                wallpaperChanged(activeVideoSettings);
            }
        }
    }

    private void StopVideo()
    {
        if (videoEngine is null)
        {
            return;
        }

        videoEngine.FrameReady -= OnVideoFrameReady;
        videoEngine.Stop();
        videoEngine.Dispose();
        videoEngine = null;
        activeVideoSettings = null;
        BackgroundImageSource = null;
    }

    public void SetPlaybackActive(bool active)
    {
        playbackActive = active;
        if (videoEngine is null)
        {
            return;
        }

        if (active)
        {
            videoEngine.Play();
        }
        else
        {
            videoEngine.Pause();
        }
    }

    private void SetBackgroundImage(IImage? bitmap, LauncherSettings previewSettings)
    {
        var old = BackgroundImageSource as IDisposable;
        BackgroundImageSource = bitmap;
        if (previewSettings.ThemeColorMode == ThemeColorModes.Wallpaper)
        {
            wallpaperChanged(previewSettings);
        }

        if (old is not null)
        {
            Dispatcher.UIThread.Post(() => old.Dispose(), DispatcherPriority.Background);
        }
    }

    private static Bitmap? LoadBundledBackground()
    {
        try
        {
            var uri = new Uri("avares://Cafe.Launcher.Avalonia/Assets/bg-7b36e4e0.png");
            using var stream = AssetLoader.Open(uri);
            return new Bitmap(stream);
        }
        catch (Exception ex)
        {
            LocalDiagnostics.LogSync(
                "LoadBundledBackground",
                $"Failed to load bundled background image: {ex.Message}");
            return null;
        }
    }

    public static Stretch ToStretch(string fit) => fit switch
    {
        BackgroundFits.Fill => Stretch.Fill,
        BackgroundFits.Uniform => Stretch.Uniform,
        _ => Stretch.UniformToFill
    };

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        StopVideo();
        (BackgroundImageSource as IDisposable)?.Dispose();
    }
}
