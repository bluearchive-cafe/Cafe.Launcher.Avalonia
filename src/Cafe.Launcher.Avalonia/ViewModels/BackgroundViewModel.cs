using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
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
    private readonly Func<string, PixelSize, IImage?> imageLoader;
    private readonly Func<IImage?> bundledImageLoader;
    private readonly IWindowMetricsService? windowMetrics;
    private string? currentDecodedImagePath;
    private PixelSize lastDecodeTarget;
    private int backgroundLoadGeneration;
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
            (path, targetPhysicalSize) => BackgroundImageDecoder.Decode(path, targetPhysicalSize),
            LoadBundledBackground)
    {
    }

    /// <param name="imageLoader">
    /// Receives the image path and the window physical-size snapshot to decode against;
    /// implementations derive the constrained decode box from it (see
    /// <see cref="BackgroundImageDecoder.GetTargetBox"/>). Passing the snapshot the load
    /// decision was made on keeps <see cref="lastDecodeTarget"/> equal to the box the
    /// bitmap was actually decoded for.
    /// </param>
    internal BackgroundViewModel(
        ImageCacheService imageCacheService,
        LocalDiagnostics diagnostics,
        Action<LauncherSettings> wallpaperChanged,
        Func<string, PixelSize, IImage?> imageLoader,
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

    /// <summary>任一边（按维度）增长超过该比例才触发重解码：按维度而非面积，
    /// 覆盖纯高度、DPI 变化与竖长↔横宽的宽高比翻转（后者面积可能不变）。</summary>
    private const double SignificantGrowRatio = 1.2;

    /// <summary>当前窗口物理客户区尺寸；无窗口时退回默认目标。</summary>
    private PixelSize GetPhysicalSize() =>
        windowMetrics?.GetPhysicalClientSize() ?? BackgroundImageDecoder.FallbackTarget;

    public async Task UpdateBackgroundImageAsync(
        LauncherSettings settings,
        LauncherStatusSnapshot? snapshot,
        CancellationToken cancellationToken)
    {
        var loadGeneration = Interlocked.Increment(ref backgroundLoadGeneration);
        // 入口快照：解码与 lastDecodeTarget 记录共用同一份窗口尺寸，中途 resize
        // 不会让“已记录目标”与“位图实际解码目标”脱节（由重解码路径兜底）。
        var decodeSize = GetPhysicalSize();
        var decodeTarget = BackgroundImageDecoder.GetTargetBox(decodeSize);

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
                        var remoteImage = await Task.Run(() => imageLoader(cachedPath, decodeSize));
                        ThrowIfCancellationRequested(remoteImage, cancellationToken);
                        TrySetBackgroundImage(
                            remoteImage,
                            settings,
                            loadGeneration,
                            cachedPath,
                            decodeTarget);
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
                    var customBackground = await LoadCustomBackgroundImageResultAsync(
                        settings.CustomBackgroundPath,
                        decodeSize,
                        cancellationToken);
                    if (customBackground.Image is not null)
                    {
                        ThrowIfCancellationRequested(customBackground.Image, cancellationToken);
                        TrySetBackgroundImage(
                            customBackground.Image,
                            settings,
                            loadGeneration,
                            customBackground.DecodedPath,
                            decodeTarget);
                        return;
                    }
                }
                break;
        }

        cancellationToken.ThrowIfCancellationRequested();
        TrySetBackgroundImage(
            bundledImageLoader(),
            settings,
            loadGeneration,
            decodedPath: null,
            decodeTarget);
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
            || currentDecodedImagePath is null)
        {
            return;
        }

        var decodeSize = GetPhysicalSize();
        var target = BackgroundImageDecoder.GetTargetBox(decodeSize);
        if (target.Width <= lastDecodeTarget.Width * SignificantGrowRatio
            && target.Height <= lastDecodeTarget.Height * SignificantGrowRatio)
        {
            return;
        }

        // 只重解码当前已解析到的具体文件，不重放完整来源解析。这样文件夹壁纸不会因
        // resize / DPI 变化重新随机选图，远端来源也不会重复进入缓存与下载链路。
        var decodedPath = currentDecodedImagePath;
        var sourceGeneration = Volatile.Read(ref backgroundLoadGeneration);
        IImage? reloaded;
        try
        {
            reloaded = await Task.Run(() => imageLoader(decodedPath, decodeSize));
        }
        catch (Exception ex)
        {
            await diagnostics.MessageAsync(
                "Background image resize reload failed",
                $"path: {decodedPath}\nexception: {ex.Message}",
                CancellationToken.None);
            return;
        }

        if (reloaded is null)
        {
            return;
        }

        if (disposed
            || version != resizeReloadVersion
            || sourceGeneration != Volatile.Read(ref backgroundLoadGeneration)
            || !string.Equals(decodedPath, currentDecodedImagePath, StringComparison.Ordinal))
        {
            (reloaded as IDisposable)?.Dispose();
            return;
        }

        lastDecodeTarget = target;
        ReplaceBackgroundImageAfterResize(reloaded);
    }

    public Bitmap? GetBackgroundBitmap()
    {
        return BackgroundImageSource as Bitmap;
    }

    public async Task<Bitmap?> LoadCustomBackgroundAsync(
        string path,
        CancellationToken cancellationToken = default) =>
        (await LoadCustomBackgroundImageResultAsync(
            path,
            GetPhysicalSize(),
            cancellationToken)).Image as Bitmap;

    private async Task<BackgroundLoadResult> LoadCustomBackgroundImageResultAsync(
        string path,
        PixelSize decodeSize,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(path))
        {
            try
            {
                // 自定义背景图在 UI 线程外解码，避免大图卡帧。
                var bitmap = await Task.Run(() => imageLoader(path, decodeSize));
                if (bitmap is null)
                {
                    return default;
                }
                if (cancellationToken.IsCancellationRequested)
                {
                    (bitmap as IDisposable)?.Dispose();
                    cancellationToken.ThrowIfCancellationRequested();
                }

                return new BackgroundLoadResult(bitmap, path);
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
                return default;
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
                return default;
            }

            if (imagePath is null)
            {
                await diagnostics.MessageAsync(
                    "Custom background folder contains no supported images",
                    $"path: {path}",
                    CancellationToken.None);
                return default;
            }

            try
            {
                // 随机选中的背景图同样在 UI 线程外解码。
                var bitmap = await Task.Run(() => imageLoader(imagePath, decodeSize));
                if (bitmap is null)
                {
                    return default;
                }
                if (cancellationToken.IsCancellationRequested)
                {
                    (bitmap as IDisposable)?.Dispose();
                    cancellationToken.ThrowIfCancellationRequested();
                }

                return new BackgroundLoadResult(bitmap, imagePath);
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
                return default;
            }
        }

        await diagnostics.MessageAsync(
            "Custom background path does not exist",
            $"path: {path}",
            CancellationToken.None);
        return default;
    }

    private readonly record struct BackgroundLoadResult(IImage? Image, string? DecodedPath);

    private static void ThrowIfCancellationRequested(IImage? image, CancellationToken cancellationToken)
    {
        if (!cancellationToken.IsCancellationRequested)
        {
            return;
        }

        (image as IDisposable)?.Dispose();
        cancellationToken.ThrowIfCancellationRequested();
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

    private bool TrySetBackgroundImage(
        IImage? bitmap,
        LauncherSettings previewSettings,
        int loadGeneration,
        string? decodedPath,
        PixelSize decodeTarget)
    {
        if (disposed || loadGeneration != Volatile.Read(ref backgroundLoadGeneration))
        {
            (bitmap as IDisposable)?.Dispose();
            return false;
        }

        currentDecodedImagePath = decodedPath;
        // 记录解码实际使用的目标框（与 imageLoader 收到的同一快照），而不是位图尺寸；
        // 竖图按高解码后宽度可能小于目标框，以位图尺寸为基准会造成重复重解码。
        lastDecodeTarget = decodeTarget;
        SetBackgroundImage(bitmap, previewSettings);
        return true;
    }

    private void ReplaceBackgroundImageAfterResize(IImage? bitmap)
    {
        var old = BackgroundImageSource as IDisposable;
        BackgroundImageSource = bitmap;
        // 分辨率刷新没有改变逻辑壁纸，不重放交叉淡化或主题取色。
        Dispatcher.UIThread.Post(() => old?.Dispose(), DispatcherPriority.Background);
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
        Interlocked.Increment(ref backgroundLoadGeneration);
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
