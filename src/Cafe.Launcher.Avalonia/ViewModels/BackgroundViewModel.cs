using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Cafe.Launcher.Avalonia.Features.Settings;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cafe.Launcher.Avalonia.ViewModels;

public partial class BackgroundViewModel : ViewModelBase, IDisposable
{
    private readonly ImageCacheService imageCacheService;
    private readonly LocalDiagnostics diagnostics;
    private readonly Action<LauncherSettings> wallpaperChanged;
    private readonly Func<string, IImage?> imageLoader;
    private readonly Func<IImage?> bundledImageLoader;
    private readonly IWindowMetricsService? windowMetrics;
    private LauncherSettings? lastBackgroundSettings;
    private LauncherStatusSnapshot? lastBackgroundSnapshot;
    private int lastDecodeTargetWidth;
    private int resizeReloadVersion;
    private bool disposed;

    [ObservableProperty]
    private IImage? backgroundImageSource;

    [ObservableProperty]
    private Stretch backgroundStretch = Stretch.UniformToFill;

    [ObservableProperty]
    private IBrush? backgroundFillBrush;

    /// <summary>Overlay layer showing the previous wallpaper while it fades out during a swap.</summary>
    [ObservableProperty]
    private IImage? wallpaperCrossFadeSource;

    [ObservableProperty]
    private double wallpaperCrossFadeOpacity;

    /// <summary>
    /// Raised right after the logical wallpaper source switches, carrying the previous image for
    /// the view to fade out. Raised only under full motion; subscribers must not block.
    /// </summary>
    internal event Action<IImage, CancellationToken>? PreviousWallpaperFadingOut;

    private bool isMotionReduced;
    private IDisposable? fadingOutWallpaper;
    private CancellationTokenSource? wallpaperFadeCancellation;

    public BackgroundViewModel(
        ImageCacheService imageCacheService,
        LocalDiagnostics diagnostics,
        SettingsViewModel settings,
        IWindowMetricsService? windowMetrics = null)
        : this(
            imageCacheService,
            diagnostics,
            previewSettings =>
            {
                // 壁纸像素立即切换；主题取色在后台线程执行，完成后主题 tokens 跟进。
                _ = settings.Appearance.RefreshThemeColorPaletteFromCurrentBackgroundAsync(
                    markDirty: false,
                    applySchemeAfter: true);
            },
            windowMetrics)
    {
    }

    internal BackgroundViewModel(
        ImageCacheService imageCacheService,
        LocalDiagnostics diagnostics,
        Action<LauncherSettings> wallpaperChanged,
        IWindowMetricsService? windowMetrics = null)
        : this(
            imageCacheService,
            diagnostics,
            wallpaperChanged,
            path => BackgroundImageDecoder.Decode(
                path,
                windowMetrics?.GetPhysicalClientSize() ?? BackgroundImageDecoder.FallbackTarget),
            LoadBundledBackground)
    {
    }

    internal BackgroundViewModel(
        ImageCacheService imageCacheService,
        LocalDiagnostics diagnostics,
        Action<LauncherSettings> wallpaperChanged,
        Func<string, IImage?> imageLoader,
        Func<IImage?> bundledImageLoader,
        IWindowMetricsService? windowMetrics = null)
    {
        this.imageCacheService = imageCacheService;
        this.diagnostics = diagnostics;
        this.wallpaperChanged = wallpaperChanged;
        this.imageLoader = imageLoader;
        this.bundledImageLoader = bundledImageLoader;
        this.windowMetrics = windowMetrics;
        if (windowMetrics is not null)
        {
            // 首次解码可能发生在窗口达到最终尺寸（布局/恢复保存状态/最大化）之前；
            // 尺寸随后显著变大时按需重解码，否则驻留位图被放大采样显示为模糊。
            windowMetrics.PhysicalSizeChanged += OnPhysicalSizeChanged;
        }

        backgroundImageSource = bundledImageLoader();
    }

    /// <summary>窗口显著变大后的壁纸重解码去抖窗口；测试可调小。</summary>
    internal static TimeSpan ResizeReloadDebounce = TimeSpan.FromMilliseconds(500);

    public async Task UpdateBackgroundImageAsync(
        LauncherSettings settings,
        LauncherStatusSnapshot? snapshot,
        CancellationToken cancellationToken)
    {
        lastBackgroundSettings = settings;
        if (snapshot is not null)
        {
            lastBackgroundSnapshot = snapshot;
        }

        // 记录本次解码使用的目标宽：窗口变大后的重解码以它为基准比较，
        // 而不是位图实际宽（竖图按高钳制后宽必然小于目标宽，会造成误判死循环）。
        lastDecodeTargetWidth = windowMetrics is null
            ? 0
            : Math.Clamp(
                windowMetrics.GetPhysicalClientSize().Width,
                BackgroundImageDecoder.MinDecodeWidthPixels,
                BackgroundImageDecoder.MaxDecodeSidePixels);

        ApplyBackgroundPresentation(settings);

        switch (settings.BackgroundSource)
        {
            case BackgroundSources.Remote:
                var bgImg = snapshot?.Remote.BaseConfig?.LauncherBackgroundImg;
                var crc64 = snapshot?.Remote.BaseConfig?.LauncherBackgroundImgCrc64;
                if (!string.IsNullOrWhiteSpace(bgImg) && !string.IsNullOrWhiteSpace(crc64))
                {
                    try
                    {
                        var proxyMode = snapshot?.Settings.ProxyMode ?? ProxyModes.Auto;
                        var cachedPath = await imageCacheService.GetCachedPathAsync(crc64, cancellationToken)
                            ?? await imageCacheService.CacheImageAsync(bgImg, crc64, proxyMode, cancellationToken);
                        cancellationToken.ThrowIfCancellationRequested();
                        // 远端背景图可能很大；解码放线程池，避免续体回到 UI 线程后卡帧。
                        SetBackgroundImage(await Task.Run(() => imageLoader(cachedPath)), settings);
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

    /// <summary>ADR-016: reduced motion cancels any in-flight wallpaper cross-fade immediately.</summary>
    public void ApplyMotionPreference(bool reduceMotion)
    {
        if (disposed) return;
        isMotionReduced = reduceMotion;
        if (!reduceMotion)
        {
            return;
        }

        wallpaperFadeCancellation?.Cancel();
        FinishWallpaperFade();
    }

    private void OnPhysicalSizeChanged()
    {
        if (disposed)
        {
            return;
        }

        var version = ++resizeReloadVersion;
        _ = ReloadAtNewSizeAfterDebounceAsync(version);
    }

    private async Task ReloadAtNewSizeAfterDebounceAsync(int version)
    {
        try
        {
            await Task.Delay(ResizeReloadDebounce);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        if (disposed
            || version != resizeReloadVersion
            || windowMetrics is null
            || lastBackgroundSettings is null)
        {
            return;
        }

        var targetWidth = Math.Clamp(
            windowMetrics.GetPhysicalClientSize().Width,
            BackgroundImageDecoder.MinDecodeWidthPixels,
            BackgroundImageDecoder.MaxDecodeSidePixels);
        if (targetWidth <= lastDecodeTargetWidth * 1.2)
        {
            return;
        }

        // 窗口显著变大：按新目标重放当前壁纸加载。事件在 UI 线程触发，
        // continuation 回到 UI 上下文，满足 SetBackgroundImage 的线程要求。
        await UpdateBackgroundImageAsync(
            lastBackgroundSettings,
            lastBackgroundSnapshot,
            CancellationToken.None);
    }

    public Bitmap? GetBackgroundBitmap()
    {
        return BackgroundImageSource as Bitmap;
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
                // 自定义背景图在 UI 线程外解码，避免大图卡帧。
                var bitmap = await Task.Run(() => imageLoader(path));
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
                // 随机选中的背景图同样在 UI 线程外解码。
                var bitmap = await Task.Run(() => imageLoader(imagePath));
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

    private void SetBackgroundImage(IImage? bitmap, LauncherSettings previewSettings)
    {
        var old = BackgroundImageSource as IDisposable;
        BackgroundImageSource = bitmap;
        if (previewSettings.ThemeColorMode == ThemeColorModes.Wallpaper)
        {
            // 主题取色在后台执行（不再阻塞 UI 线程），完成后 tokens 落色；
            // 期间沿用旧色板渲染，不会闪到默认色。
            wallpaperChanged(previewSettings);
        }

        if (!isMotionReduced && old is not null && bitmap is not null && PreviousWallpaperFadingOut is not null)
        {
            StartWallpaperCrossFade(old);
        }
        else
        {
            Dispatcher.UIThread.Post(() => old?.Dispose(), DispatcherPriority.Background);
        }
    }

    /// <summary>
    /// ADR-016 壁纸交叉淡化：逻辑源立即切换（主题取色后台进行），旧图所有权移交视图层作为
    /// 覆盖层淡出，视图在摘除引用后经 <see cref="OnWallpaperOverlayReleased"/> 归还释放权。
    /// 快速连续更换时以“最新状态优先”直接释放上一张在途旧图（此刻同一调度回调内
    /// 覆盖层 Source 会被新事件同步替换，不存在残留引用的渲染帧）。
    /// </summary>
    private void StartWallpaperCrossFade(IDisposable oldBitmap)
    {
        var superseded = Interlocked.Exchange(ref fadingOutWallpaper, oldBitmap);
        if (superseded is not null && !ReferenceEquals(superseded, oldBitmap))
        {
            superseded.Dispose();
        }

        PreviousWallpaperFadingOut?.Invoke((IImage)oldBitmap, GetFadeToken());
    }

    /// <summary>
    /// 视图层在覆盖层已摘除旧图引用（Source 置空）后回调；只有此刻释放才保证没有
    /// 任何视觉树引用残留——否则渲染帧会在 Image.Render 读取已释放位图抛
    /// ObjectDisposedException 使进程崩溃。禁止用固定延时“宽限”替代该确认。
    /// </summary>
    internal void OnWallpaperOverlayReleased(IImage previousImage)
    {
        if (disposed
            || previousImage is not IDisposable fading
            || !ReferenceEquals(fadingOutWallpaper, fading))
        {
            return;
        }

        fadingOutWallpaper = null;
        fading.Dispose();
    }

    private CancellationToken GetFadeToken()
    {
        wallpaperFadeCancellation?.Cancel();
        wallpaperFadeCancellation?.Dispose();
        wallpaperFadeCancellation = new CancellationTokenSource();
        return wallpaperFadeCancellation.Token;
    }

    /// <summary>ADR-016：降动效切换时取消在途淡化动画；释放仍由视图摘除引用后回调完成。</summary>
    private void FinishWallpaperFade()
    {
        wallpaperFadeCancellation?.Cancel();
    }

    private static Bitmap? LoadBundledBackground()
    {
        try
        {
            var uri = new Uri("avares://Cafe.Launcher.Avalonia/Assets/launcher-background.png");
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
        resizeReloadVersion++;
        windowMetrics?.PhysicalSizeChanged -= OnPhysicalSizeChanged;
        wallpaperFadeCancellation?.Cancel();
        // 先摘除属性引用（绑定同步清空 Image.Source），再释放位图：即使窗口尚在收尾
        // 渲染，视觉树也不会拿到已释放的位图实现。
        var current = BackgroundImageSource as IDisposable;
        BackgroundImageSource = null;
        var fading = fadingOutWallpaper;
        fadingOutWallpaper = null;
        current?.Dispose();
        fading?.Dispose();
    }
}
