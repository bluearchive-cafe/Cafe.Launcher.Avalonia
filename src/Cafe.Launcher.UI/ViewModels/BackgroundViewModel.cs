using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Cafe.Launcher.UI.Controls.VideoBackground;
using Cafe.Launcher.UI.Constants;
using Avalonia.Platform;
using Avalonia.Threading;
using Cafe.Launcher.UI.Features.Settings;
using Cafe.Launcher.UI.Helpers;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.Core.Services.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Services;
using Cafe.Launcher.Core.Helpers;

namespace Cafe.Launcher.UI.ViewModels;

internal partial class BackgroundViewModel : ObservableObject, IDisposable
{
    private readonly IRemoteImageStore remoteImageStore;
    private readonly ILauncherDiagnostics diagnostics;
    private readonly Action<LauncherSettings> wallpaperChanged;
    private readonly Func<string, PixelSize, IImage?> imageLoader;
    private readonly Func<IImage?> bundledImageLoader;
    private readonly IWindowMetricsService? windowMetrics;
    private readonly VideoThumbnailFlow videoThumbnails;
    private string? currentDecodedImagePath;
    private PixelSize lastDecodeTarget;
    private string? lastBackgroundSourceKey;
    private string appliedBackgroundSource = BackgroundSources.Bundled;
    private string? appliedCustomBackgroundPath;
    private int backgroundLoadGeneration;
    private int resizeReloadVersion;
    private bool disposed;

    /// <summary>
    /// 按原生分辨率解码的内置壁纸，与窗口尺寸无关，实例生命周期内像素恒定。
    /// 每次回落到内置图都重新解码一份 2560×1388 位图，会让 Skia/原生分配随
    /// 切换次数留下阶梯式增长（AUD-PERF-009）；这里只解码一次并复用。
    /// </summary>
    private IImage? bundledImage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BackgroundPreviewImage))]
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
    private readonly ToastService? toastService;
    private readonly LocalizationService? localizer;

    [ObservableProperty]
    private string? backgroundVideoPath;

    /// <summary>The concrete file selected for the custom background; a folder stays in the settings draft.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVideoBackgroundSelected))]
    [NotifyPropertyChangedFor(nameof(BackgroundPreviewImage))]
    [NotifyPropertyChangedFor(nameof(IsRemoteBackgroundDisplayed))]
    private string? currentBackgroundFilePath;

    /// <summary>The displayed remote URL or resolved custom path; null identifies the bundled wallpaper.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRemoteBackgroundDisplayed))]
    private string? currentBackgroundLocation;

    /// <summary>Remote wallpaper names are localized separately from their URL tooltip.</summary>
    public bool IsRemoteBackgroundDisplayed => CurrentBackgroundLocation is not null && CurrentBackgroundFilePath is null;

    /// <summary>Audio controls follow the resolved file, including videos selected from a folder.</summary>
    public bool IsVideoBackgroundSelected => VideoBackgroundRuntime.IsVideoFile(CurrentBackgroundFilePath);

    /// <summary>Video previews and palette extraction share cover art or the first frame; the playback fallback stays static.</summary>
    public IImage? BackgroundPreviewImage => IsVideoBackgroundSelected
        ? videoThumbnails.Thumbnail ?? BackgroundImageSource
        : BackgroundImageSource;

    /// <summary>The current thumbnail request, independent of presentation-only settings previews.</summary>
    internal Task PendingVideoThumbnail => videoThumbnails.Pending;

    [ObservableProperty]
    private int backgroundVideoVolume;

    /// <summary>The selected source failed to load; the shared static fallback is being displayed.</summary>
    [ObservableProperty]
    private bool hasBackgroundLoadFailure;

    public BackgroundViewModel(
        IRemoteImageStore remoteImageStore,
        ILauncherDiagnostics diagnostics,
        SettingsViewModel settings,
        VideoThumbnailService videoThumbnailService,
        IWindowMetricsService? windowMetrics = null,
        ToastService? toastService = null,
        LocalizationService? localizer = null)
        : this(remoteImageStore, diagnostics, settings, windowMetrics, toastService, localizer,
            videoThumbnailService.LoadThumbnailAsync)
    {
    }

    internal BackgroundViewModel(
        IRemoteImageStore remoteImageStore,
        ILauncherDiagnostics diagnostics,
        SettingsViewModel settings,
        IWindowMetricsService? windowMetrics = null,
        ToastService? toastService = null,
        LocalizationService? localizer = null,
        Func<string, CancellationToken, Task<IImage?>>? videoThumbnailLoader = null)
        : this(
            remoteImageStore,
            diagnostics,
            previewSettings =>
            {
                // Every source previews the current wallpaper; only the selected
                // wallpaper source may apply its palette to the live scheme.
                _ = settings.Appearance.RefreshThemeColorPaletteFromCurrentBackgroundAsync(
                    markDirty: false,
                    applySchemeAfter: true);
            },
            windowMetrics,
            videoThumbnailLoader)
    {
        this.toastService = toastService;
        this.localizer = localizer;
    }

    internal BackgroundViewModel(
        IRemoteImageStore remoteImageStore,
        ILauncherDiagnostics diagnostics,
        Action<LauncherSettings> wallpaperChanged,
        IWindowMetricsService? windowMetrics = null,
        Func<string, CancellationToken, Task<IImage?>>? videoThumbnailLoader = null)
        : this(
            remoteImageStore,
            diagnostics,
            wallpaperChanged,
            (path, targetPhysicalSize) => BackgroundImageDecoder.Decode(path, targetPhysicalSize),
            () => LoadBundledBackground(diagnostics),
            windowMetrics,
            videoThumbnailLoader)
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
        IRemoteImageStore remoteImageStore,
        ILauncherDiagnostics diagnostics,
        Action<LauncherSettings> wallpaperChanged,
        Func<string, PixelSize, IImage?> imageLoader,
        Func<IImage?> bundledImageLoader,
        IWindowMetricsService? windowMetrics = null,
        Func<string, CancellationToken, Task<IImage?>>? videoThumbnailLoader = null)
    {
        this.remoteImageStore = remoteImageStore;
        this.diagnostics = diagnostics;
        this.wallpaperChanged = wallpaperChanged;
        this.imageLoader = imageLoader;
        this.bundledImageLoader = bundledImageLoader;
        this.windowMetrics = windowMetrics;
        videoThumbnails = new VideoThumbnailFlow(videoThumbnailLoader);
        videoThumbnails.ThumbnailChanged += OnVideoThumbnailChanged;
        if (windowMetrics is not null)
        {
            // 首次解码可能发生在窗口达到最终尺寸（布局/恢复保存状态/最大化）之前；
            // 尺寸随后显著变大时按需重解码，否则驻留位图被放大采样显示为模糊。
            windowMetrics.PhysicalSizeChanged += OnPhysicalSizeChanged;
        }

        backgroundImageSource = LoadBundledImage();

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

    /// <summary>
    /// Whether the displayed selection already represents these source settings. A presentation-only
    /// preview must finish a source change if it cancelled the preview that was still loading it.
    /// </summary>
    public bool IsCurrentBackgroundSelection(LauncherSettings settings) =>
        appliedBackgroundSource == settings.BackgroundSource
        && (settings.BackgroundSource != BackgroundSources.Custom
            || appliedCustomBackgroundPath == settings.CustomBackgroundPath);

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

        // 文件夹只负责选出一个具体文件；此后的类型识别、指纹、加载和回退与手动选择共用。
        var customFilePath = settings.BackgroundSource == BackgroundSources.Custom
            ? await ResolveCustomBackgroundFileAsync(settings.CustomBackgroundPath, cancellationToken)
            : null;
        cancellationToken.ThrowIfCancellationRequested();
        if (disposed || loadGeneration != Volatile.Read(ref backgroundLoadGeneration))
        {
            return;
        }

        // 来源与解码目标均未变化时跳过整条「缓存校验 + 解码 + 交叉淡化 + 取色」
        // 管线：此前每次刷新（启动、保存设置、每个游戏操作完成后）都会重解码
        // 壁纸并重放交叉淡化与主题取色，用户可感知卡顿。文件夹先随机选图，
        // 与手动选择命中同一文件时同样复用；遥源失败回落仍会重试。
        var sourceKey = ResolveStableBackgroundSourceKey(settings, snapshot, customFilePath);
        // 目标比较只对「按目标解码」的来源有意义。内置图的解码不看目标
        // （`LoadBundledBackground` 原生解码：该图没有更大的一档可解），重解码永远产出同一张
        // 位图，所以对它比较目标只会判出「白解一遍」——构造点早于窗口 Attach，构造期记下的
        // 目标必然与首次刷新算出的不同（实测兜底 1920×1080 → 2400×1350 vs 实际 1300×754 → 1625×943）。
        var targetDecidesReuse = !string.Equals(
            sourceKey,
            BackgroundSources.Bundled,
            StringComparison.Ordinal)
            && !VideoBackgroundRuntime.IsVideoFile(customFilePath);
        if (sourceKey is not null
            && sourceKey == lastBackgroundSourceKey
            && (!targetDecidesReuse || decodeTarget == lastDecodeTarget)
            && BackgroundImageSource is not null)
        {
            // A previous fallback may already be the bundled bitmap requested now.
            // Reuse the pixels while clearing the warning for this successful choice.
            if (settings.BackgroundSource == BackgroundSources.Bundled)
                HasBackgroundLoadFailure = false;
            RememberBackgroundSelection(settings);
            return;
        }

        var videoPath = VideoBackgroundRuntime.IsVideoFile(customFilePath)
                        && File.Exists(customFilePath)
            ? customFilePath
            : null;
        if (videoPath is not null)
        {
            // Keep the bundled image underneath playback. Start the cover/first-frame request
            // before notifying palette consumers so they await this selection's image once.
            var fallback = await Task.Run(() => LoadBundledImage());
            ThrowIfCancellationRequestedUnlessBundled(fallback, cancellationToken);
            if (TrySetBackgroundImage(fallback, settings, loadGeneration, null, decodeTarget,
                    currentFilePath: customFilePath, notifyPalette: false))
            {
                lastBackgroundSourceKey = sourceKey;
                videoThumbnails.Request(videoPath, sourceKey);
                wallpaperChanged(settings);
            }
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
                        var cachedPath = await remoteImageStore.GetCachedPathAsync(crc64, cancellationToken)
                            ?? await remoteImageStore.CacheImageAsync(bgImg, crc64, cancellationToken);
                        cancellationToken.ThrowIfCancellationRequested();
                        // 远端背景图可能很大；解码放线程池，避免续体回到 UI 线程后卡帧。
                        var remoteImage = await Task.Run(() => imageLoader(cachedPath, decodeSize));
                        BitmapLifetime.ThrowIfCancellationRequested(remoteImage, cancellationToken);
                        if (TrySetBackgroundImage(
                                remoteImage,
                                settings,
                                loadGeneration,
                                cachedPath,
                                decodeTarget,
                                currentLocation: bgImg))
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
                if (!string.IsNullOrWhiteSpace(customFilePath))
                {
                    var customImage = await LoadCustomBackgroundImageAsync(
                        customFilePath,
                        decodeSize,
                        cancellationToken);
                    if (customImage is not null)
                    {
                        BitmapLifetime.ThrowIfCancellationRequested(customImage, cancellationToken);
                        if (TrySetBackgroundImage(
                                customImage,
                                settings,
                                loadGeneration,
                                customFilePath,
                                decodeTarget,
                                currentFilePath: customFilePath))
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
        var bundled = await Task.Run(() => LoadBundledImage());
        ThrowIfCancellationRequestedUnlessBundled(bundled, cancellationToken);
        var applied = TrySetBackgroundImage(
            bundled,
            settings,
            loadGeneration,
            decodedPath: null,
            decodeTarget,
            isFallback: settings.BackgroundSource == BackgroundSources.Remote
                || (settings.BackgroundSource == BackgroundSources.Custom
                    && !string.IsNullOrWhiteSpace(settings.CustomBackgroundPath)),
            currentFilePath: customFilePath);
        if (applied && settings.BackgroundSource == BackgroundSources.Bundled)
        {
            // 仅当内置图是用户选择的来源（而非遥源/自定义失败回落）时才记录：
            // 回落保持 key 为空，下次刷新仍会重试原来源。
            lastBackgroundSourceKey = sourceKey;
        }
    }

    /// <summary>
    /// 稳定来源的可跳过标识：相同标识 + 相同解码目标即可复用现有壁纸。
    /// 自定义来源使用已解析的具体文件；遥源配置缺失时返回 null（永不跳过）。
    /// </summary>
    private static string? ResolveStableBackgroundSourceKey(
        LauncherSettings settings,
        LauncherStatusSnapshot? snapshot,
        string? customFilePath)
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
                // 两种选择方式均以具体文件的路径 + 内容指纹（长度 + 最后写入时间）
                // 为标识；用户覆盖原文件时路径不变，仅凭路径会误复用旧图。
                return TryGetCustomFileFingerprint(customFilePath, out var fingerprint)
                    ? $"{BackgroundSources.Custom}|{customFilePath}|{fingerprint}"
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
        BackgroundVideoVolume = settings.BackgroundVideoMuted ? 0 : Math.Clamp(settings.BackgroundVideoVolume, 0, 100);
        BackgroundStretch = ToStretch(settings.BackgroundFit);
        BackgroundFillBrush = settings.BackgroundFit == BackgroundFits.Uniform
            ? new SolidColorBrush(ColorUtils.ParseColorOrDefault(settings.BackgroundFillColor))
            : null;
    }

    private void OnVideoThumbnailChanged() => OnPropertyChanged(nameof(BackgroundPreviewImage));

    /// <summary>只接受当前文件的失败，保留静态回退；相同文件指纹不会在每次刷新时重复报错。</summary>
    internal void ReportVideoFailure(string path, Exception exception)
    {
        if (BackgroundVideoPath != path) return;
        BackgroundVideoPath = null;
        HasBackgroundLoadFailure = true;
        _ = diagnostics.ErrorAsync("Background", $"Video background failed: {path}", exception, CancellationToken.None);
        if (toastService is not null && localizer is not null)
            toastService.ShowWarning(localizer.T(LocalizationKeys.VideoBackgroundFailed));
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

    /// <summary>
    /// Gets the palette source, awaiting the current video's cover/first frame without cancelling
    /// the shared thumbnail request when a palette refresh is superseded. The caller borrows the bitmap.
    /// </summary>
    public async Task<Bitmap?> GetBackgroundBitmapAsync(CancellationToken cancellationToken)
    {
        if (IsVideoBackgroundSelected
            && await videoThumbnails.GetThumbnailAsync(cancellationToken) is { } thumbnail)
        {
            return thumbnail;
        }

        return BackgroundImageSource as Bitmap;
    }

    private async Task<string?> ResolveCustomBackgroundFileAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (!Directory.Exists(path))
        {
            return path;
        }

        try
        {
            var filePath = await Task.Run(() => ResolveRandomBackgroundFile(path), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (filePath is null)
            {
                await diagnostics.MessageAsync(
                    "Background",
                    $"Custom background folder contains no supported images or videos\npath: {path}",
                    CancellationToken.None);
            }

            return filePath;
        }
        catch (Exception ex) when (StorageFailure.IsRecoverable(ex))
        {
            await diagnostics.MessageAsync("Background",
                $"Custom background folder scan failed\npath: {path}\nexception: {ex.Message}", CancellationToken.None);
            return null;
        }
    }

    private async Task<IImage?> LoadCustomBackgroundImageAsync(string path, PixelSize decodeSize,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(path))
        {
            await diagnostics.MessageAsync("Background", $"Custom background path does not exist\npath: {path}",
                CancellationToken.None);
            return null;
        }

        try
        {
            // 手动文件和随机文件共用一次后台解码及相同的取消释放规则。
            var image = await Task.Run(() => imageLoader(path, decodeSize));
            BitmapLifetime.ThrowIfCancellationRequested(image, cancellationToken);
            return image;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            await diagnostics.MessageAsync("Background",
                $"Custom background image load failed\npath: {path}\nexception: {ex.Message}", CancellationToken.None);
            return null;
        }
    }

    /// <summary>Selects one supported image or video from the folder itself, without scanning subfolders.</summary>
    public static string? ResolveRandomBackgroundFile(string folderPath)
    {
        if (!Directory.Exists(folderPath))
            return null;

        var filePaths = Directory
            .EnumerateFiles(folderPath)
            .Where(path => IsSupportedBackgroundImage(path) || VideoBackgroundRuntime.IsVideoFile(path))
            .ToArray();

        return filePaths.Length == 0
            ? null
            : filePaths[Random.Shared.Next(filePaths.Length)];
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

    private void SetBackgroundImage(IImage? bitmap, LauncherSettings previewSettings, bool notifyPalette)
    {
        var old = BackgroundImageSource;
        if (ReferenceEquals(old, bitmap))
        {
            // 重新显示同一张内置图时像素没有变化。不能把仍在使用的缓存位图交给
            // 淡化或延迟释放路径：那会让下一次显示或渲染帧读到已释放实现。
            if (notifyPalette) wallpaperChanged(previewSettings);
            return;
        }

        BackgroundImageSource = bitmap;
        if (notifyPalette)
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
            ReleaseAfterBindings(old);
        }
    }

    /// <summary>
    /// 首次解码内置壁纸后缓存同一实例；解码与窗口尺寸无关，结果恒定。加载失败不缓存，
    /// 让后续刷新仍可重试；并发首次解码时保留先到的一份。
    /// </summary>
    private IImage? LoadBundledImage()
    {
        if (Volatile.Read(ref bundledImage) is { } cached)
        {
            return cached;
        }

        var loaded = bundledImageLoader();
        if (loaded is null)
        {
            return null;
        }

        var winner = Interlocked.CompareExchange(ref bundledImage, loaded, null);
        if (winner is null || ReferenceEquals(winner, loaded))
        {
            return loaded;
        }

        // 本次解码结果从未对外发布，直接释放。
        (loaded as IDisposable)?.Dispose();
        return winner;
    }

    /// <summary>缓存的内置图由 ViewModel 持有到 Dispose，任何换图路径都不得释放它。</summary>
    private bool IsBundledImage(object? image) =>
        image is not null && ReferenceEquals(image, Volatile.Read(ref bundledImage));

    /// <summary>释放调用方交出的位图；内置壁纸的释放权归本类，所有路径统一经此判定。</summary>
    private void DisposeUnlessBundled(object? image)
    {
        if (!IsBundledImage(image))
        {
            (image as IDisposable)?.Dispose();
        }
    }

    private void ReleaseAfterBindings(IImage? image)
    {
        if (!IsBundledImage(image))
        {
            BitmapLifetime.ReleaseAfterBindingsSettle(image);
        }
    }

    private void ThrowIfCancellationRequestedUnlessBundled(IImage? image, CancellationToken cancellationToken)
    {
        if (!cancellationToken.IsCancellationRequested)
        {
            return;
        }

        DisposeUnlessBundled(image);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private bool TrySetBackgroundImage(
        IImage? bitmap,
        LauncherSettings previewSettings,
        int loadGeneration,
        string? decodedPath,
        PixelSize decodeTarget,
        bool isFallback = false,
        string? currentFilePath = null,
        string? currentLocation = null,
        bool notifyPalette = true)
    {
        if (disposed || loadGeneration != Volatile.Read(ref backgroundLoadGeneration))
        {
            DisposeUnlessBundled(bitmap);
            return false;
        }

        currentDecodedImagePath = decodedPath;
        // 切到静态背景时只取消在途截帧，已交付的缩略图按文件指纹保留：
        // 预览只在选中视频时使用它，跨切换复用可避免每次重新截帧（AUD-PERF-009）。
        if (isFallback || !VideoBackgroundRuntime.IsVideoFile(currentFilePath)) videoThumbnails.CancelPending();
        RememberBackgroundSelection(previewSettings);
        lastBackgroundSourceKey = null;
        CurrentBackgroundFilePath = currentFilePath;
        CurrentBackgroundLocation = currentLocation ?? currentFilePath;
        // 记录解码实际使用的目标框（与 imageLoader 收到的同一快照），而不是位图尺寸；
        // 竖图按高解码后宽度可能小于目标框，以位图尺寸为基准会造成重复重解码。
        lastDecodeTarget = decodeTarget;
        HasBackgroundLoadFailure = isFallback;
        BackgroundVideoPath = !isFallback && VideoBackgroundRuntime.IsVideoFile(currentFilePath) ? currentFilePath : null;
        SetBackgroundImage(bitmap, previewSettings, notifyPalette);
        return true;
    }

    private void RememberBackgroundSelection(LauncherSettings settings)
    {
        appliedBackgroundSource = settings.BackgroundSource;
        appliedCustomBackgroundPath = settings.CustomBackgroundPath;
    }

    private void ReplaceBackgroundImageAfterResize(IImage? bitmap)
    {
        var old = BackgroundImageSource;
        BackgroundImageSource = bitmap;
        // 分辨率刷新没有改变逻辑壁纸，不重放交叉淡化或主题取色。
        ReleaseAfterBindings(old);
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
            DisposeUnlessBundled(superseded);
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
        DisposeUnlessBundled(fading);
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

    private static Bitmap? LoadBundledBackground(ILauncherDiagnostics diagnostics)
    {
        try
        {
            var uri = new Uri("avares://Cafe.Launcher.UI/Assets/launcher-background.png");
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
        videoThumbnails.Dispose();
        BackgroundVideoPath = null;
        CurrentBackgroundFilePath = null;
        CurrentBackgroundLocation = null;
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
        var bundled = Interlocked.Exchange(ref bundledImage, null);
        if (!ReferenceEquals(current, bundled)) current?.Dispose();
        if (!ReferenceEquals(fading, current) && !ReferenceEquals(fading, bundled)) fading?.Dispose();
        (bundled as IDisposable)?.Dispose();
    }
}
