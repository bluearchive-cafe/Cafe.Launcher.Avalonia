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
    private string? lastBackgroundSourceKey;
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
            () => LoadBundledBackground(diagnostics))
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

        // 构造期交付的那张内置图，就是首次刷新要的同一张：内置图由 `LoadBundledBackground`
        // 按原生分辨率解码、**忽略目标尺寸**（该图也没有更大的一档可解），因此把来源键与解码
        // 目标一并记为「已满足」，首次刷新即可走跳过卫、不再重解码一遍。
        // 此前两个字段都为空，窗口尺寸落定后的第一次刷新必然重跑整条管线——实测那次冗余
        // 解码 32–46 ms，并短暂多驻留一份 2560×1388 位图（约 14 MB），外加一次无意义的
        // 交叉淡化与一次重复的主题取色（AUD-PERF-005 残留）。
        // 注意构造点早于窗口 Attach（App.axaml.cs 先解析 VM 再构造 MainWindow），所以这里
        // 取到的是兜底尺寸——正因为如此，首次刷新的目标与它不同，光播种还不够，守卫必须
        // 知道内置来源的目标比较是无意义的（见下）。
        // 位图为 null（内置图加载失败）时不记：守卫的 not-null 条件自会让它重试。
        if (backgroundImageSource is not null)
        {
            lastBackgroundSourceKey = BackgroundSources.Bundled;
            // 只用作重解码路径的增长基准；内置来源的实际解码与目标无关。
            lastDecodeTarget = BackgroundImageDecoder.GetTargetBox(GetPhysicalSize());
        }
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

        // 来源与解码目标均未变化时跳过整条「缓存校验 + 解码 + 交叉淡化 + 取色」
        // 管线：此前每次刷新（启动、保存设置、每个游戏操作完成后）都会重解码
        // 壁纸并重放交叉淡化与主题取色，用户可感知卡顿。仅稳定来源可跳过——
        // 文件夹壁纸每次随机选图、遥源失败回落均不可跳过（见下）。
        var sourceKey = ResolveStableBackgroundSourceKey(settings, snapshot);
        // 目标比较只对「按目标解码」的来源有意义。内置图的解码不看目标
        // （`LoadBundledBackground` 原生解码：该图没有更大的一档可解），重解码永远产出同一张
        // 位图，所以对它比较目标只会判出「白解一遍」——构造点早于窗口 Attach，构造期记下的
        // 目标必然与首次刷新算出的不同（实测兜底 1920×1080 → 2400×1350 vs 实际 1300×754 → 1625×943）。
        var targetDecidesReuse = !string.Equals(
            sourceKey,
            BackgroundSources.Bundled,
            StringComparison.Ordinal);
        if (sourceKey is not null
            && sourceKey == lastBackgroundSourceKey
            && (!targetDecidesReuse || decodeTarget == lastDecodeTarget)
            && BackgroundImageSource is not null)
        {
            return;
        }

        switch (settings.BackgroundSource)
        {
            case BackgroundSources.Remote:
                var bgImg = snapshot?.Remote.BaseConfig?.LauncherBackgroundImg;
                var crc64 = snapshot?.Remote.BaseConfig?.LauncherBackgroundImgCrc64;
                if (!string.IsNullOrWhiteSpace(bgImg) && !string.IsNullOrWhiteSpace(crc64))
                {
                    try
                    {
                        var cachedPath = await imageCacheService.GetCachedPathAsync(crc64, cancellationToken)
                            ?? await imageCacheService.CacheImageAsync(bgImg, crc64, cancellationToken);
                        cancellationToken.ThrowIfCancellationRequested();
                        // 远端背景图可能很大；解码放线程池，避免续体回到 UI 线程后卡帧。
                        var remoteImage = await Task.Run(() => imageLoader(cachedPath, decodeSize));
                        BitmapLifetime.ThrowIfCancellationRequested(remoteImage, cancellationToken);
                        if (TrySetBackgroundImage(
                                remoteImage,
                                settings,
                                loadGeneration,
                                cachedPath,
                                decodeTarget))
                        {
                            lastBackgroundSourceKey = sourceKey;
                        }

                        return;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _ = diagnostics.MessageAsync(
                            "Background",
                            $"Remote background image download failed\nurl: {bgImg}\ncrc64: {crc64}\nexception: {ex.Message}",
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
                        BitmapLifetime.ThrowIfCancellationRequested(customBackground.Image, cancellationToken);
                        if (TrySetBackgroundImage(
                                customBackground.Image,
                                settings,
                                loadGeneration,
                                customBackground.DecodedPath,
                                decodeTarget))
                        {
                            lastBackgroundSourceKey = sourceKey;
                        }

                        return;
                    }
                }
                break;
        }

        cancellationToken.ThrowIfCancellationRequested();
        // 内置图与远端图同为全屏大图：解码同样放线程池，避免默认壁纸下每次
        // 回落都在 UI 线程重解码整图。
        var bundledImage = await Task.Run(() => bundledImageLoader());
        BitmapLifetime.ThrowIfCancellationRequested(bundledImage, cancellationToken);
        TrySetBackgroundImage(
            bundledImage,
            settings,
            loadGeneration,
            decodedPath: null,
            decodeTarget);
        if (settings.BackgroundSource == BackgroundSources.Bundled)
        {
            // 仅当内置图是用户选择的来源（而非遥源/自定义失败回落）时才记录：
            // 回落保持 key 为空，下次刷新仍会重试原来源。
            lastBackgroundSourceKey = sourceKey;
        }
    }

    /// <summary>
    /// 稳定来源的可跳过标识：相同标识 + 相同解码目标即可复用现有壁纸。
    /// 文件夹自定义壁纸每次随机选图、遥源配置缺失时返回 null（永不跳过）。
    /// </summary>
    private static string? ResolveStableBackgroundSourceKey(
        LauncherSettings settings,
        LauncherStatusSnapshot? snapshot)
    {
        switch (settings.BackgroundSource)
        {
            case BackgroundSources.Remote:
                var bgImg = snapshot?.Remote.BaseConfig?.LauncherBackgroundImg;
                var crc64 = snapshot?.Remote.BaseConfig?.LauncherBackgroundImgCrc64;
                return string.IsNullOrWhiteSpace(bgImg) || string.IsNullOrWhiteSpace(crc64)
                    ? null
                    : $"{BackgroundSources.Remote}|{bgImg}|{crc64}";
            case BackgroundSources.Custom:
                // 文件夹来源每次刷新随机选图是有意行为，不参与跳过；文件来源以
                // 路径 + 内容指纹（长度 + 最后写入时间）为标识——用户在原路径
                // 覆盖图片文件时路径不变，仅凭路径会误命中跳过守卫、停留旧图。
                return TryGetCustomFileFingerprint(settings.CustomBackgroundPath, out var fingerprint)
                    ? $"{BackgroundSources.Custom}|{settings.CustomBackgroundPath}|{fingerprint}"
                    : null;
            default:
                return BackgroundSources.Bundled;
        }
    }

    /// <summary>仅对存在的「文件」来源给出内容指纹；目录或不可读时返回 false（不参与跳过）。</summary>
    private static bool TryGetCustomFileFingerprint(string? path, out string fingerprint)
    {
        fingerprint = string.Empty;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        try
        {
            var info = new FileInfo(path);
            fingerprint = $"{info.Length}|{info.LastWriteTimeUtc.Ticks}";
            return true;
        }
        catch (Exception ex) when (StorageFailure.IsRecoverable(ex))
        {
            // File.Exists 与读取属性之间存在被占用/收回权限的窗口；读不到指纹
            // 就当来源不稳定，走完整重载管线。
            return false;
        }
    }

    public void ApplyBackgroundPresentation(LauncherSettings settings)
    {
        BackgroundStretch = ToStretch(settings.BackgroundFit);
        BackgroundFillBrush = settings.BackgroundFit == BackgroundFits.Uniform
            ? new SolidColorBrush(ColorUtils.ParseColorOrDefault(settings.BackgroundFillColor))
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
                "Background",
                $"Background image resize reload failed\npath: {decodedPath}\nexception: {ex.Message}",
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

    /// <summary>
    /// 测试缝：按给定路径解析并解码一张自定义背景。生产代码不走这里——它经由
    /// <c>Apply</c>/<c>ApplyBackgroundPresentation</c> 落到同一条私有实现上；本方法此前是
    /// public，而它的唯一调用者是测试（§5.3：仅供测试的接缝标 internal）。
    /// </summary>
    internal async Task<Bitmap?> LoadCustomBackgroundAsync(
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
                    "Background",
                    $"Custom background image load failed\npath: {path}\nexception: {ex.Message}",
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
            catch (Exception ex) when (StorageFailure.IsRecoverable(ex))
            {
                await diagnostics.MessageAsync(
                    "Background",
                    $"Custom background folder scan failed\npath: {path}\nexception: {ex.Message}",
                    CancellationToken.None);
                return default;
            }

            if (imagePath is null)
            {
                await diagnostics.MessageAsync(
                    "Background",
                    $"Custom background folder contains no supported images\npath: {path}",
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
                    "Background",
                    $"Custom background folder image load failed\nfolder: {path}\npath: {imagePath}\nexception: {ex.Message}",
                    CancellationToken.None);
                return default;
            }
        }

        await diagnostics.MessageAsync(
            "Background",
            $"Custom background path does not exist\npath: {path}",
            CancellationToken.None);
        return default;
    }

    private readonly record struct BackgroundLoadResult(IImage? Image, string? DecodedPath);

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
        var old = BackgroundImageSource;
        BackgroundImageSource = bitmap;
        if (previewSettings.ThemeColorMode == ThemeColorModes.Wallpaper)
        {
            // 主题取色在后台执行（不再阻塞 UI 线程），完成后 tokens 落色；
            // 期间沿用旧色板渲染，不会闪到默认色。
            wallpaperChanged(previewSettings);
        }

        if (!isMotionReduced && old is IDisposable oldDisposable && bitmap is not null && PreviousWallpaperFadingOut is not null)
        {
            StartWallpaperCrossFade(oldDisposable);
        }
        else
        {
            BitmapLifetime.ReleaseAfterBindingsSettle(old);
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
        var old = BackgroundImageSource;
        BackgroundImageSource = bitmap;
        // 分辨率刷新没有改变逻辑壁纸，不重放交叉淡化或主题取色。
        BitmapLifetime.ReleaseAfterBindingsSettle(old);
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

    private static Bitmap? LoadBundledBackground(LocalDiagnostics diagnostics)
    {
        try
        {
            var uri = new Uri("avares://Cafe.Launcher.Avalonia/Assets/launcher-background.png");
            using var stream = AssetLoader.Open(uri);
            return new Bitmap(stream);
        }
        catch (Exception ex)
        {
            _ = diagnostics.WarningAsync(
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
