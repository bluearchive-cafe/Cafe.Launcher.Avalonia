using System.Net;
using System.Net.Http;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.Core.Services.Diagnostics;
using Cafe.Launcher.Testing;
using Cafe.Launcher.UI.ViewModels;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Services;

namespace Cafe.Launcher.Tests;

public sealed class BackgroundViewModelTests : IDisposable
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VideoThumbnail_WhenFileOrFolderSelectsVideo_UsesFirstFrameWithoutChangingWallpaper(bool fromFolder)
    {
        var path = Path.Combine(tempDir, "thumbnail.mp4");
        await File.WriteAllBytesAsync(path, []);
        using var cache = CreateCache(_ => PngBytes);
        var thumbnail = new TestImage();
        var paletteRefreshes = 0;
        string? requestedPath = null;
        using var viewModel = new BackgroundViewModel(cache, new LocalDiagnostics(), _ => paletteRefreshes++,
            (_, _) => new TestImage(), () => new TestImage(),
            videoThumbnailLoader: (selectedPath, _) =>
            {
                requestedPath = selectedPath;
                return Task.FromResult<IImage?>(thumbnail);
            });
        await viewModel.UpdateBackgroundImageAsync(new LauncherSettings
        {
            BackgroundSource = BackgroundSources.Custom,
            CustomBackgroundPath = fromFolder ? tempDir : path,
            ThemeColorMode = ThemeColorModes.Wallpaper
        }, null, CancellationToken.None);
        await viewModel.PendingVideoThumbnail.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(path, requestedPath);
        Assert.Same(thumbnail, viewModel.BackgroundPreviewImage);
        Assert.NotSame(thumbnail, viewModel.BackgroundImageSource);
        Assert.Equal(1, paletteRefreshes);

        await viewModel.UpdateBackgroundImageAsync(new LauncherSettings(), null, CancellationToken.None);
        Assert.Same(viewModel.BackgroundImageSource, viewModel.BackgroundPreviewImage);
    }

    [Fact]
    public async Task VideoThumbnail_WhenSourceReusedAtNewWindowSize_DoesNotRecaptureFirstFrame()
    {
        var path = Path.Combine(tempDir, "cached-thumbnail.mp4");
        await File.WriteAllBytesAsync(path, []);
        using var cache = CreateCache(_ => PngBytes);
        var thumbnail = new TestImage();
        var requests = 0;
        var metrics = new SteppedWindowMetrics();
        using var viewModel = new BackgroundViewModel(cache, new LocalDiagnostics(), _ => { },
            (_, _) => new TestImage(), () => new TestImage(), metrics,
            videoThumbnailLoader: (_, _) =>
            {
                requests++;
                return Task.FromResult<IImage?>(thumbnail);
            });
        var settings = new LauncherSettings { BackgroundSource = BackgroundSources.Custom, CustomBackgroundPath = path };
        await viewModel.UpdateBackgroundImageAsync(settings, null, CancellationToken.None);
        await viewModel.PendingVideoThumbnail.WaitAsync(TimeSpan.FromSeconds(5));
        settings.BackgroundVideoVolume = 35;
        viewModel.ApplyBackgroundPresentation(settings);
        metrics.ReportRealWindowSize();
        await viewModel.UpdateBackgroundImageAsync(settings, null, CancellationToken.None);
        await viewModel.PendingVideoThumbnail.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, requests);
        Assert.Same(thumbnail, viewModel.BackgroundPreviewImage);
        Assert.Equal(35, viewModel.BackgroundVideoVolume);
    }

    [Fact]
    public async Task VideoThumbnail_WhenOlderCaptureFinishesLast_DisposesItAndKeepsNewPreview()
    {
        var first = Path.Combine(tempDir, "old.mp4");
        var second = Path.Combine(tempDir, "new.mp4");
        await File.WriteAllBytesAsync(first, []);
        await File.WriteAllBytesAsync(second, []);
        using var cache = CreateCache(_ => PngBytes);
        var oldResult = new TaskCompletionSource<IImage?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldThumbnail = new TestImage();
        var newThumbnail = new TestImage();
        using var viewModel = new BackgroundViewModel(cache, new LocalDiagnostics(), _ => { },
            (_, _) => new TestImage(), () => new TestImage(),
            videoThumbnailLoader: (path, _) => path == first ? oldResult.Task : Task.FromResult<IImage?>(newThumbnail));
        var settings = new LauncherSettings { BackgroundSource = BackgroundSources.Custom, CustomBackgroundPath = first };
        await viewModel.UpdateBackgroundImageAsync(settings, null, CancellationToken.None);
        var oldCapture = viewModel.PendingVideoThumbnail;
        settings.CustomBackgroundPath = second;
        await viewModel.UpdateBackgroundImageAsync(settings, null, CancellationToken.None);
        await viewModel.PendingVideoThumbnail.WaitAsync(TimeSpan.FromSeconds(5));
        oldResult.SetResult(oldThumbnail);
        await oldCapture.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(oldThumbnail.IsDisposed);
        Assert.Same(newThumbnail, viewModel.BackgroundPreviewImage);
        Assert.Equal(second, viewModel.CurrentBackgroundFilePath);
    }

    [Fact]
    public async Task VideoThumbnail_WhenSelectionChanges_CancelsPendingCapture()
    {
        var path = Path.Combine(tempDir, "cancel-capture.mp4");
        await File.WriteAllBytesAsync(path, []);
        using var cache = CreateCache(_ => PngBytes);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var viewModel = new BackgroundViewModel(cache, new LocalDiagnostics(), _ => { },
            (_, _) => new TestImage(), () => new TestImage(), videoThumbnailLoader: async (_, cancellationToken) =>
            {
                started.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return null;
            });
        await viewModel.UpdateBackgroundImageAsync(new LauncherSettings
        {
            BackgroundSource = BackgroundSources.Custom, CustomBackgroundPath = path
        }, null, CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var capture = viewModel.PendingVideoThumbnail;

        await viewModel.UpdateBackgroundImageAsync(new LauncherSettings(), null, CancellationToken.None);
        await capture.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Same(viewModel.BackgroundImageSource, viewModel.BackgroundPreviewImage);
        Assert.False(viewModel.IsVideoBackgroundSelected);
    }

    [Fact]
    public async Task VideoThumbnail_WhenDisposedDuringCapture_ReleasesLateImage()
    {
        var path = Path.Combine(tempDir, "closing.mp4");
        await File.WriteAllBytesAsync(path, []);
        using var cache = CreateCache(_ => PngBytes);
        var completion = new TaskCompletionSource<IImage?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thumbnail = new TestImage();
        using var viewModel = new BackgroundViewModel(cache, new LocalDiagnostics(), _ => { },
            (_, _) => new TestImage(), () => new TestImage(), videoThumbnailLoader: (_, _) => completion.Task);
        await viewModel.UpdateBackgroundImageAsync(new LauncherSettings
        {
            BackgroundSource = BackgroundSources.Custom, CustomBackgroundPath = path
        }, null, CancellationToken.None);
        var capture = viewModel.PendingVideoThumbnail;
        viewModel.Dispose();
        completion.SetResult(thumbnail);
        await capture.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(thumbnail.IsDisposed);
        Assert.Null(viewModel.BackgroundPreviewImage);
    }

    [Theory]
    [InlineData(-10, 0)]
    [InlineData(0, 0)]
    [InlineData(35, 35)]
    [InlineData(110, 100)]
    public void ApplyBackgroundPresentation_WhenMuted_RetainsVolumeAndRestoresItOnUnmute(int volume, int expected)
    {
        using var cache = CreateCache(_ => PngBytes);
        using var viewModel = new BackgroundViewModel(cache, new LocalDiagnostics(), _ => { },
            (_, _) => new TestImage(), () => new TestImage());
        var settings = new LauncherSettings { BackgroundVideoVolume = volume, BackgroundVideoMuted = true };
        viewModel.ApplyBackgroundPresentation(settings);
        Assert.Equal(0, viewModel.BackgroundVideoVolume);
        Assert.Equal(volume, settings.BackgroundVideoVolume);
        settings.BackgroundVideoMuted = false;
        viewModel.ApplyBackgroundPresentation(settings);
        Assert.Equal(expected, viewModel.BackgroundVideoVolume);
    }

    private static readonly byte[] PngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII=");

    private readonly TestDirectory rootDir = TestDirectory.Create();
    private readonly string tempDir;

    public BackgroundViewModelTests()
    {
        // 缓存目录由数据根派生（<root>/image-cache），故工作目录取派生结果本身。
        tempDir = Path.Combine(rootDir, LauncherDataRoot.ImageCacheFolderName);
        Directory.CreateDirectory(tempDir);
    }

    [Fact]
    public void Constructor_WithWindowMetrics_SubscribesUntilDisposed()
    {
        using var cache = CreateCache(_ => PngBytes);
        var metrics = new SubscriptionWindowMetrics();
        using var viewModel = new BackgroundViewModel(cache, new LocalDiagnostics(), _ => { }, metrics);
        Assert.Equal(1, metrics.Subscriptions);
        viewModel.Dispose();
        Assert.Equal(0, metrics.Subscriptions);
    }

    private sealed class SubscriptionWindowMetrics : IWindowMetricsService
    {
        public int Subscriptions { get; private set; }
        public PixelSize GetPhysicalClientSize() => new(1300, 754);
        public event Action? PhysicalSizeChanged
        {
            add => Subscriptions++;
            remove => Subscriptions--;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VideoBackground_WhenSelected_UsesStaticFallbackAndClearsOnImageSelection(bool fromFolder)
    {
        var path = Path.Combine(tempDir, "background.MP4");
        await File.WriteAllBytesAsync(path, []);
        using var cache = CreateCache(_ => PngBytes);
        using var viewModel = new BackgroundViewModel(cache, new LocalDiagnostics(), _ => { },
            (_, _) => throw new InvalidOperationException("Video must not enter the bitmap decoder."),
            () => new TestImage());
        var settings = new LauncherSettings
        {
            BackgroundSource = BackgroundSources.Custom, CustomBackgroundPath = fromFolder ? tempDir : path, BackgroundVideoVolume = 35
        };

        await viewModel.UpdateBackgroundImageAsync(settings, null, CancellationToken.None);

        Assert.Equal(path, viewModel.BackgroundVideoPath);
        Assert.Equal(35, viewModel.BackgroundVideoVolume);
        Assert.Equal(path, viewModel.CurrentBackgroundFilePath);
        Assert.True(viewModel.IsVideoBackgroundSelected);
        Assert.IsType<TestImage>(viewModel.BackgroundImageSource);
        viewModel.ReportVideoFailure("superseded.mp4", new InvalidOperationException());
        Assert.Equal(path, viewModel.BackgroundVideoPath);
        Assert.False(viewModel.HasBackgroundLoadFailure);
        viewModel.ReportVideoFailure(path, new InvalidOperationException());
        Assert.Null(viewModel.BackgroundVideoPath);
        Assert.True(viewModel.HasBackgroundLoadFailure);
        await viewModel.UpdateBackgroundImageAsync(settings, null, CancellationToken.None);
        Assert.Null(viewModel.BackgroundVideoPath); // Same failed fingerprint does not retry on every refresh.

        settings.BackgroundSource = BackgroundSources.Bundled;
        await viewModel.UpdateBackgroundImageAsync(settings, null, CancellationToken.None);
        Assert.False(viewModel.HasBackgroundLoadFailure);
        settings.BackgroundSource = BackgroundSources.Custom;
        await viewModel.UpdateBackgroundImageAsync(settings, null, CancellationToken.None);
        Assert.Equal(path, viewModel.BackgroundVideoPath);
        settings.BackgroundSource = BackgroundSources.Bundled;
        await viewModel.UpdateBackgroundImageAsync(settings, null, CancellationToken.None);
        Assert.Null(viewModel.BackgroundVideoPath);
    }

    [Fact]
    public async Task UpdateBackgroundImageAsync_WhenCustomFileMissing_ExposesFallbackUntilSourceRecovers()
    {
        using var cache = CreateCache(_ => PngBytes);
        using var viewModel = new BackgroundViewModel(cache, new LocalDiagnostics(), _ => { },
            (_, _) => new TestImage(), () => new TestImage());
        var settings = new LauncherSettings
        {
            BackgroundSource = BackgroundSources.Custom,
            CustomBackgroundPath = Path.Combine(tempDir, "missing.png")
        };
        await viewModel.UpdateBackgroundImageAsync(settings, null, CancellationToken.None);
        Assert.True(viewModel.HasBackgroundLoadFailure);
        Assert.NotNull(viewModel.BackgroundImageSource);

        settings.BackgroundSource = BackgroundSources.Bundled;
        await viewModel.UpdateBackgroundImageAsync(settings, null, CancellationToken.None);
        Assert.False(viewModel.HasBackgroundLoadFailure);
        settings.BackgroundSource = BackgroundSources.Custom;
        settings.CustomBackgroundPath = string.Empty;
        await viewModel.UpdateBackgroundImageAsync(settings, null, CancellationToken.None);
        Assert.False(viewModel.HasBackgroundLoadFailure);
    }

    [Theory]
    [InlineData(false, "MP4")]
    [InlineData(true, "MP4")]
    [InlineData(false, "mkv")]
    [InlineData(true, "mkv")]
    [InlineData(false, "webm")]
    [InlineData(true, "webm")]
    public async Task UpdateBackgroundImageAsync_WhenFileOrFolderSelectsVideo_UsesVideoPipeline(bool fromFolder, string extension)
    {
        var folder = Path.Combine(tempDir, "videos");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"background.{extension}");
        await File.WriteAllBytesAsync(path, []);
        using var cache = CreateCache(_ => PngBytes);
        var imageLoads = 0;
        using var viewModel = new BackgroundViewModel(cache, new LocalDiagnostics(), _ => { },
            (_, _) => { imageLoads++; return new TestImage(); }, () => new TestImage());
        var settings = new LauncherSettings
        {
            BackgroundSource = BackgroundSources.Custom,
            CustomBackgroundPath = fromFolder ? folder : path,
            BackgroundVideoVolume = 35
        };

        await viewModel.UpdateBackgroundImageAsync(settings, null, CancellationToken.None);

        Assert.Equal(path, viewModel.BackgroundVideoPath);
        Assert.Equal(35, viewModel.BackgroundVideoVolume);
        Assert.Equal(0, imageLoads);
        Assert.False(viewModel.HasBackgroundLoadFailure);
        Assert.Equal(fromFolder ? folder : path, settings.CustomBackgroundPath);
    }

    [Fact]
    public async Task UpdateBackgroundImageAsync_WhenRemoteImageIsValid_LoadsAndCachesImage()
    {
        var hash = await ComputeHashAsync(PngBytes);
        using var cache = CreateCache(_ => PngBytes);
        var themeRefreshCount = 0;
        using var viewModel = new BackgroundViewModel(
            cache,
            new LocalDiagnostics(),
            _ => themeRefreshCount++,
            (path, _) => new TestImage(),
            () => new TestImage());
        var settings = new LauncherSettings
        {
            BackgroundSource = BackgroundSources.Remote,
            ThemeColorMode = ThemeColorModes.Wallpaper
        };

        await viewModel.UpdateBackgroundImageAsync(
            settings,
            CreateRemoteSnapshot(hash),
            CancellationToken.None);

        Assert.IsType<TestImage>(viewModel.BackgroundImageSource);
        Assert.Equal(1, themeRefreshCount);
        Assert.True(File.Exists(Path.Combine(tempDir, $"{hash}.cache")));
    }

    [Fact]
    public async Task UpdateBackgroundImageAsync_WhenRemoteImageFails_FallsBackToBundledImage()
    {
        using var cache = CreateCache(_ => new HttpRequestException("bad gateway", null, HttpStatusCode.BadGateway));
        using var viewModel = new BackgroundViewModel(
            cache,
            new LocalDiagnostics(),
            _ => { },
            (path, _) => new TestImage(),
            () => new TestImage());
        var initialImage = viewModel.BackgroundImageSource;
        var settings = new LauncherSettings
        {
            BackgroundSource = BackgroundSources.Remote
        };

        await viewModel.UpdateBackgroundImageAsync(
            settings,
            CreateRemoteSnapshot("1"),
            CancellationToken.None);

        Assert.NotSame(initialImage, viewModel.BackgroundImageSource);
        Assert.IsType<TestImage>(viewModel.BackgroundImageSource);
    }

    [Fact]
    public async Task UpdateBackgroundImageAsync_WhenCustomFileIsInvalid_UsesFallback()
    {
        var path = Path.Combine(tempDir, "invalid.png");
        await File.WriteAllTextAsync(path, "not-an-image");
        using var cache = CreateCache(_ => PngBytes);
        using var viewModel = new BackgroundViewModel(
            cache,
            new LocalDiagnostics(),
            _ => { });

        await viewModel.UpdateBackgroundImageAsync(new LauncherSettings
        {
            BackgroundSource = BackgroundSources.Custom,
            CustomBackgroundPath = path
        }, null, CancellationToken.None);

        Assert.True(viewModel.HasBackgroundLoadFailure);
        Assert.Equal(path, viewModel.CurrentBackgroundFilePath);
    }

    [Fact]
    public async Task UpdateBackgroundImageAsync_WhenCustomPathDoesNotExist_UsesFallback()
    {
        using var cache = CreateCache(_ => PngBytes);
        using var viewModel = new BackgroundViewModel(
            cache,
            new LocalDiagnostics(),
            _ => { });

        await viewModel.UpdateBackgroundImageAsync(new LauncherSettings
        {
            BackgroundSource = BackgroundSources.Custom,
            CustomBackgroundPath = Path.Combine(tempDir, "missing.png")
        }, null, CancellationToken.None);

        Assert.True(viewModel.HasBackgroundLoadFailure);
    }

    [Fact]
    public async Task UpdateBackgroundImageAsync_WhenFolderHasNoSupportedFiles_UsesFallback()
    {
        var folder = Path.Combine(tempDir, "empty");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "readme.txt"), "text");
        using var cache = CreateCache(_ => PngBytes);
        using var viewModel = new BackgroundViewModel(
            cache,
            new LocalDiagnostics(),
            _ => { });

        await viewModel.UpdateBackgroundImageAsync(new LauncherSettings
        {
            BackgroundSource = BackgroundSources.Custom,
            CustomBackgroundPath = folder
        }, null, CancellationToken.None);

        Assert.True(viewModel.HasBackgroundLoadFailure);
        Assert.Null(viewModel.CurrentBackgroundFilePath);
        Assert.False(viewModel.IsVideoBackgroundSelected);
    }

    [Fact]
    public async Task UpdateBackgroundImageAsync_WhenSourceUnchanged_SkipsReloadWithoutFade()
    {
        var hash = await ComputeHashAsync(PngBytes);
        using var cache = CreateCache(_ => PngBytes);
        using var viewModel = new BackgroundViewModel(
            cache,
            new LocalDiagnostics(),
            _ => { },
            (path, _) => new TestImage(),
            () => new TestImage());

        await viewModel.UpdateBackgroundImageAsync(
            new LauncherSettings { BackgroundSource = BackgroundSources.Remote },
            CreateRemoteSnapshot(hash),
            CancellationToken.None);
        var firstSwap = (TestImage)viewModel.BackgroundImageSource!;
        var fadingOut = new List<TestImage>();

        viewModel.PreviousWallpaperFadingOut += (image, _) => fadingOut.Add((TestImage)image);

        // 同一来源与同一解码目标再次刷新：跳过整条重载管线（缓存校验/解码/
        // 交叉淡化/取色），保留现有壁纸实例——这是刷新路径的性能契约。
        await viewModel.UpdateBackgroundImageAsync(
            new LauncherSettings { BackgroundSource = BackgroundSources.Remote },
            CreateRemoteSnapshot(hash),
            CancellationToken.None);

        Assert.Same(firstSwap, viewModel.BackgroundImageSource);
        Assert.Empty(fadingOut);
    }

    [Fact]
    public async Task UpdateBackgroundImageAsync_WhenBundledSource_FirstRefreshReusesTheConstructorsBitmap()
    {
        // AUD-PERF-005 残留：构造期已经按原生分辨率交付了内置壁纸（该解码忽略目标尺寸），
        // 首次刷新没有理由再解一遍同一张图——实测那次冗余解码 32–46 ms，并短暂多驻留一份
        // 2560×1388 位图。
        //
        // 用例必须复现生产的时序，否则测不到关键分支：构造点在窗口 Attach 之前（构造期只能
        // 取到兜底尺寸），首次刷新发生在窗口显示之后（取到真实尺寸）——两个目标框不同，
        // 光靠播种匹配不上，靠的是「内置来源不比较目标」那一条。假窗口在这里负责这个落差。
        var bundledDecodeCount = 0;
        var metrics = new SteppedWindowMetrics();
        using var cache = CreateCache(_ => PngBytes);
        using var viewModel = new BackgroundViewModel(
            cache,
            new LocalDiagnostics(),
            _ => { },
            (path, _) => new TestImage(),
            () =>
            {
                bundledDecodeCount++;
                return new TestImage();
            },
            metrics);
        var delivered = viewModel.BackgroundImageSource;

        metrics.ReportRealWindowSize();

        await viewModel.UpdateBackgroundImageAsync(
            new LauncherSettings { BackgroundSource = BackgroundSources.Bundled },
            snapshot: null,
            CancellationToken.None);

        Assert.Equal(1, bundledDecodeCount);
        Assert.Same(delivered, viewModel.BackgroundImageSource);
    }

    [Fact]
    public async Task UpdateBackgroundImageAsync_WhenBundledSourceButConstructorLoadFailed_RetriesTheDecode()
    {
        // 构造期加载失败（位图为 null）时不得把「已交付」记成事实：首次刷新必须重试。
        var bundledDecodeCount = 0;
        using var cache = CreateCache(_ => PngBytes);
        using var viewModel = new BackgroundViewModel(
            cache,
            new LocalDiagnostics(),
            _ => { },
            (path, _) => new TestImage(),
            () =>
            {
                bundledDecodeCount++;
                return bundledDecodeCount == 1 ? null : new TestImage();
            });

        await viewModel.UpdateBackgroundImageAsync(
            new LauncherSettings { BackgroundSource = BackgroundSources.Bundled },
            snapshot: null,
            CancellationToken.None);

        Assert.Equal(2, bundledDecodeCount);
        Assert.IsType<TestImage>(viewModel.BackgroundImageSource);
    }

    [Fact]
    public async Task UpdateBackgroundImageAsync_WhenRemoteSource_StillReplacesTheConstructorBitmap()
    {
        // 跳过只对内置来源成立：远端来源的键不同，首次刷新仍必须装载并替换占位图。
        var hash = await ComputeHashAsync(PngBytes);
        var bundledDecodeCount = 0;
        using var cache = CreateCache(_ => PngBytes);
        using var viewModel = new BackgroundViewModel(
            cache,
            new LocalDiagnostics(),
            _ => { },
            (path, _) => new TestImage(),
            () =>
            {
                bundledDecodeCount++;
                return new TestImage();
            });
        var placeholder = viewModel.BackgroundImageSource;

        await viewModel.UpdateBackgroundImageAsync(
            new LauncherSettings
            {
                BackgroundSource = BackgroundSources.Remote,
                ThemeColorMode = ThemeColorModes.Wallpaper
            },
            CreateRemoteSnapshot(hash),
            CancellationToken.None);

        Assert.NotSame(placeholder, viewModel.BackgroundImageSource);
        Assert.Equal(1, bundledDecodeCount);
    }

    [Fact]
    public async Task UpdateBackgroundImageAsync_WhenSourceChangedToBundled_FadesPreviousWallpaper()
    {
        using var cache = CreateCache(_ => PngBytes);
        using var viewModel = new BackgroundViewModel(
            cache,
            new LocalDiagnostics(),
            _ => { },
            (path, _) => new TestImage(),
            () => new TestImage());

        await viewModel.UpdateBackgroundImageAsync(
            new LauncherSettings { BackgroundSource = BackgroundSources.Remote },
            CreateRemoteSnapshot(await ComputeHashAsync(PngBytes)),
            CancellationToken.None);
        var firstSwap = (TestImage)viewModel.BackgroundImageSource!;
        var fadingOut = new List<TestImage>();

        viewModel.PreviousWallpaperFadingOut += (image, _) => fadingOut.Add((TestImage)image);

        await viewModel.UpdateBackgroundImageAsync(
            new LauncherSettings { BackgroundSource = BackgroundSources.Bundled },
            CreateRemoteSnapshot(await ComputeHashAsync(PngBytes)),
            CancellationToken.None);

        Assert.NotSame(firstSwap, viewModel.BackgroundImageSource);
        Assert.Equal([firstSwap], fadingOut);
    }

    [Fact]
    public async Task UpdateBackgroundImageAsync_WhileOverlayStillHoldsPreviousImage_DefersDisposalUntilOverlayReleases()
    {
        using var cache = CreateCache(_ => PngBytes);
        using var viewModel = new BackgroundViewModel(
            cache,
            new LocalDiagnostics(),
            _ => { },
            (path, _) => new TestImage(),
            () => new TestImage());

        await viewModel.UpdateBackgroundImageAsync(
            new LauncherSettings { BackgroundSource = BackgroundSources.Remote },
            CreateRemoteSnapshot("1"),
            CancellationToken.None);

        var fadingOut = new List<TestImage>();
        viewModel.PreviousWallpaperFadingOut += (image, _) => fadingOut.Add((TestImage)image);

        await viewModel.UpdateBackgroundImageAsync(
            new LauncherSettings { BackgroundSource = BackgroundSources.Remote },
            CreateRemoteSnapshot("1"),
            CancellationToken.None);
        var fading = Assert.Single(fadingOut);

        // 模拟合成器停滞：视图层迟迟不回调 OnWallpaperOverlayReleased。此时释放位图会让
        // 下一渲染帧在 Image.Render 读取已释放实现，抛 ObjectDisposedException 崩溃。
        // 700ms 是任意浸泡窗，不对应任何生产常量——生产端已无宽限定时器（2090db3 移除，
        // 释放完全由视图回调驱动，BackgroundViewModel 明文禁止固定延时宽限），本窗口
        // 守护的是「不得重新引入短于该窗的定时释放」这一负向不变量。
        await Task.Delay(TimeSpan.FromMilliseconds(700));

        Assert.False(
            fading.IsDisposed,
            "ViewModel 在覆盖层仍持有旧图时释放了位图；渲染帧会读到已释放位图。");

        viewModel.OnWallpaperOverlayReleased(fading);
        Assert.True(fading.IsDisposed);
    }

    [Fact]
    public async Task UpdateBackgroundImageAsync_WhenMotionReduced_SkipsCrossFadeOverlay()
    {
        var bundled = new TestImage();
        using var cache = CreateCache(_ => PngBytes);
        using var viewModel = new BackgroundViewModel(
            cache,
            new LocalDiagnostics(),
            _ => { },
            (path, _) => new TestImage(),
            () => bundled);
        var fadingOut = new List<IImage>();
        viewModel.PreviousWallpaperFadingOut += (image, _) => fadingOut.Add(image);
        viewModel.ApplyMotionPreference(reduceMotion: true);

        await viewModel.UpdateBackgroundImageAsync(
            new LauncherSettings { BackgroundSource = BackgroundSources.Remote },
            CreateRemoteSnapshot("1"),
            CancellationToken.None);

        Assert.Empty(fadingOut);
    }

    [Fact]
    public async Task UpdateBackgroundImageAsync_WhenRemoteDownloadIsCanceled_PropagatesCancellation()
    {
        using var cache = CreateCache(_ => new OperationCanceledException("canceled"));
        using var viewModel = new BackgroundViewModel(
            cache,
            new LocalDiagnostics(),
            _ => { },
            (path, _) => new TestImage(),
            () => new TestImage());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => viewModel.UpdateBackgroundImageAsync(
                new LauncherSettings { BackgroundSource = BackgroundSources.Remote },
                CreateRemoteSnapshot("1"),
                cts.Token));
    }

    [Fact]
    public async Task UpdateBackgroundImageAsync_WhenRemoteFieldsAreMissing_UsesBundledImage()
    {
        using var cache = CreateCache(_ => PngBytes);
        var bundled = new TestImage();
        using var viewModel = new BackgroundViewModel(
            cache,
            new LocalDiagnostics(),
            _ => { },
            (path, _) => new TestImage(),
            () => bundled);

        await viewModel.UpdateBackgroundImageAsync(
            new LauncherSettings { BackgroundSource = BackgroundSources.Remote },
            new LauncherStatusSnapshot(),
            CancellationToken.None);

        Assert.Same(bundled, viewModel.BackgroundImageSource);
    }

    [Theory]
    [InlineData(false, "png")]
    [InlineData(true, "png")]
    [InlineData(false, "JPG")]
    [InlineData(true, "JPG")]
    [InlineData(false, "jpeg")]
    [InlineData(true, "jpeg")]
    [InlineData(false, "bmp")]
    [InlineData(true, "bmp")]
    [InlineData(false, "webp")]
    [InlineData(true, "webp")]
    public async Task UpdateBackgroundImageAsync_WhenFileOrFolderSelectsImage_UsesImagePipeline(bool fromFolder, string extension)
    {
        var path = Path.Combine(tempDir, $"custom.{extension}");
        await File.WriteAllBytesAsync(path, PngBytes);
        using var cache = CreateCache(_ => PngBytes);
        var custom = new TestImage();
        using var viewModel = new BackgroundViewModel(
            cache,
            new LocalDiagnostics(),
            _ => { },
            (path, _) => custom,
            () => new TestImage());

        await viewModel.UpdateBackgroundImageAsync(
            new LauncherSettings
            {
                BackgroundSource = BackgroundSources.Custom,
                CustomBackgroundPath = fromFolder ? tempDir : path
            },
            null,
            CancellationToken.None);

        Assert.Same(custom, viewModel.BackgroundImageSource);
        Assert.Equal(path, viewModel.CurrentBackgroundFilePath);
        Assert.False(viewModel.IsVideoBackgroundSelected);
        Assert.Null(viewModel.BackgroundVideoPath);
    }

    [Theory]
    [InlineData("png")]
    [InlineData("mp4")]
    public async Task UpdateBackgroundImageAsync_WhenFolderAndManualSelectionResolveSameFile_ReusesMedia(string extension)
    {
        var path = Path.Combine(tempDir, $"same-media.{extension}");
        await File.WriteAllBytesAsync(path, []);
        using var cache = CreateCache(_ => PngBytes);
        using var viewModel = new BackgroundViewModel(cache, new LocalDiagnostics(), _ => { },
            (_, _) => new TestImage(), () => new TestImage());
        var settings = new LauncherSettings
        {
            BackgroundSource = BackgroundSources.Custom,
            CustomBackgroundPath = tempDir
        };
        await viewModel.UpdateBackgroundImageAsync(settings, null, CancellationToken.None);
        var first = viewModel.BackgroundImageSource;

        settings.CustomBackgroundPath = path;
        await viewModel.UpdateBackgroundImageAsync(settings, null, CancellationToken.None);

        Assert.Same(first, viewModel.BackgroundImageSource);
        Assert.Equal(path, viewModel.CurrentBackgroundFilePath);
        Assert.False(viewModel.HasBackgroundLoadFailure);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpdateBackgroundImageAsync_WhenFileOrFolderDecodeIsCancelled_ReleasesImageAndPreservesCurrentFile(bool fromFolder)
    {
        var path = Path.Combine(tempDir, "cancelled.png");
        await File.WriteAllBytesAsync(path, []);
        using var cache = CreateCache(_ => PngBytes);
        using var cancellation = new CancellationTokenSource();
        var decoded = new TestImage();
        using var viewModel = new BackgroundViewModel(cache, new LocalDiagnostics(), _ => { },
            (_, _) => { cancellation.Cancel(); return decoded; }, () => new TestImage());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => viewModel.UpdateBackgroundImageAsync(new LauncherSettings
        {
            BackgroundSource = BackgroundSources.Custom,
            CustomBackgroundPath = fromFolder ? tempDir : path
        }, null, cancellation.Token));

        Assert.True(decoded.IsDisposed);
        Assert.Null(viewModel.CurrentBackgroundFilePath);
        Assert.False(viewModel.IsVideoBackgroundSelected);
    }

    [Fact]
    public async Task UpdateBackgroundImageAsync_WhenReturningFromFallback_ReloadsPreviousFile()
    {
        var path = Path.Combine(tempDir, "previous.png");
        await File.WriteAllBytesAsync(path, []);
        using var cache = CreateCache(_ => PngBytes);
        var imageLoads = 0;
        using var viewModel = new BackgroundViewModel(cache, new LocalDiagnostics(), _ => { },
            (_, _) => { imageLoads++; return new TestImage(); }, () => new TestImage());
        var settings = new LauncherSettings { BackgroundSource = BackgroundSources.Custom, CustomBackgroundPath = path };
        await viewModel.UpdateBackgroundImageAsync(settings, null, CancellationToken.None);
        settings.CustomBackgroundPath = Path.Combine(tempDir, "missing.png");
        await viewModel.UpdateBackgroundImageAsync(settings, null, CancellationToken.None);
        Assert.True(viewModel.HasBackgroundLoadFailure);

        settings.CustomBackgroundPath = path;
        await viewModel.UpdateBackgroundImageAsync(settings, null, CancellationToken.None);

        Assert.Equal(2, imageLoads);
        Assert.Equal(path, viewModel.CurrentBackgroundFilePath);
        Assert.False(viewModel.HasBackgroundLoadFailure);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpdateBackgroundImageAsync_WhenCustomFileChangesAtSamePath_DoesNotSkipReload(bool fromFolder)
    {
        var path = Path.Combine(tempDir, "custom-reswap.png");
        await File.WriteAllBytesAsync(path, PngBytes);
        using var cache = CreateCache(_ => PngBytes);
        using var viewModel = new BackgroundViewModel(
            cache,
            new LocalDiagnostics(),
            _ => { },
            (path, _) => new TestImage(),
            () => new TestImage());

        await viewModel.UpdateBackgroundImageAsync(
            new LauncherSettings
            {
                BackgroundSource = BackgroundSources.Custom,
                CustomBackgroundPath = fromFolder ? tempDir : path
            },
            null,
            CancellationToken.None);
        var firstSwap = (TestImage)viewModel.BackgroundImageSource!;

        // 用户在原路径覆盖图片文件：路径不变但内容指纹（长度/写入时间）变化，
        // 来源 key 必须失效——否则跳过守卫会让壁纸停留旧图。
        await File.WriteAllBytesAsync(path, [..PngBytes, 0x0A]);
        await viewModel.UpdateBackgroundImageAsync(
            new LauncherSettings
            {
                BackgroundSource = BackgroundSources.Custom,
                CustomBackgroundPath = fromFolder ? tempDir : path
            },
            null,
            CancellationToken.None);

        Assert.NotSame(firstSwap, viewModel.BackgroundImageSource);
    }

    [Fact]
    public async Task UpdateBackgroundImageAsync_WhenCustomFileUnchanged_SkipsReload()
    {
        var path = Path.Combine(tempDir, "custom-skip.png");
        await File.WriteAllBytesAsync(path, PngBytes);
        using var cache = CreateCache(_ => PngBytes);
        using var viewModel = new BackgroundViewModel(
            cache,
            new LocalDiagnostics(),
            _ => { },
            (path, _) => new TestImage(),
            () => new TestImage());

        await viewModel.UpdateBackgroundImageAsync(
            new LauncherSettings
            {
                BackgroundSource = BackgroundSources.Custom,
                CustomBackgroundPath = path
            },
            null,
            CancellationToken.None);
        var firstSwap = (TestImage)viewModel.BackgroundImageSource!;

        await viewModel.UpdateBackgroundImageAsync(
            new LauncherSettings
            {
                BackgroundSource = BackgroundSources.Custom,
                CustomBackgroundPath = path
            },
            null,
            CancellationToken.None);

        // 同一路径同一文件再次刷新：跳过契约对自定义文件来源同样成立。
        Assert.Same(firstSwap, viewModel.BackgroundImageSource);
    }

    [Fact]
    public void ApplyBackgroundPresentation_WhenUniform_UsesConfiguredFillColor()
    {
        using var cache = CreateCache(_ => PngBytes);
        using var viewModel = new BackgroundViewModel(
            cache,
            new LocalDiagnostics(),
            _ => { });

        viewModel.ApplyBackgroundPresentation(new LauncherSettings
        {
            BackgroundFit = BackgroundFits.Uniform,
            BackgroundFillColor = "#FF123456"
        });

        Assert.Equal(Stretch.Uniform, viewModel.BackgroundStretch);
        var brush = Assert.IsType<SolidColorBrush>(viewModel.BackgroundFillBrush);
        Assert.Equal(Color.Parse("#FF123456"), brush.Color);
    }

    [Fact]
    public void Dispose_WhenCalledTwice_DoesNotThrow()
    {
        using var cache = CreateCache(_ => PngBytes);
        var viewModel = new BackgroundViewModel(
            cache,
            new LocalDiagnostics(),
            _ => { },
            (path, _) => new TestImage(),
            () => new TestImage());

        viewModel.Dispose();
        viewModel.Dispose();
    }

    [Fact]
    public async Task UpdateBackgroundImageAsync_WhenCustomLoadIsCanceled_PropagatesCancellation()
    {
        using var cache = CreateCache(_ => PngBytes);
        using var viewModel = new BackgroundViewModel(
            cache,
            new LocalDiagnostics(),
            _ => { });
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => viewModel.UpdateBackgroundImageAsync(new LauncherSettings
            {
                BackgroundSource = BackgroundSources.Custom,
                CustomBackgroundPath = tempDir
            }, null, cts.Token));
    }

    [Theory]
    [InlineData("image.png", true)]
    [InlineData("image.JPG", true)]
    [InlineData("image.jpeg", true)]
    [InlineData("image.bmp", true)]
    [InlineData("image.webp", true)]
    [InlineData("image.gif", false)]
    public void IsSupportedBackgroundImage_RecognizesExactExtensions(
        string path,
        bool expected)
    {
        Assert.Equal(expected, BackgroundViewModel.IsSupportedBackgroundImage(path));
    }

    [Theory]
    [InlineData(BackgroundFits.Fill, Stretch.Fill)]
    [InlineData(BackgroundFits.Uniform, Stretch.Uniform)]
    [InlineData(BackgroundFits.UniformToFill, Stretch.UniformToFill)]
    [InlineData("invalid", Stretch.UniformToFill)]
    public void ToStretch_MapsFitCodes(string fit, Stretch expected)
    {
        Assert.Equal(expected, BackgroundViewModel.ToStretch(fit));
    }

    private LauncherStatusSnapshot CreateRemoteSnapshot(string hash) =>
        new()
        {
            Settings = new LauncherSettings { ProxyMode = ProxyModes.Direct },
            Remote = new LauncherRemoteState
            {
                BaseConfig = new BaseConfigResponse
                {
                    LauncherBackgroundImg = "https://images.example.invalid/background.png",
                    LauncherBackgroundImgCrc64 = hash
                }
            }
        };

    private ImageCacheService CreateCache(Func<Uri, object?> responder) =>
        new(new StubRemoteHttpTransport(responder), new Crc64Service(), TestDataRoot.ForDirectory(rootDir));

    private async Task<string> ComputeHashAsync(byte[] bytes)
    {
        var path = Path.Combine(tempDir, "hash-source.png");
        await File.WriteAllBytesAsync(path, bytes);
        return await new Crc64Service().ComputeFileAsync(path);
    }

    public void Dispose()
    {
        rootDir.Dispose();
    }

    /// <summary>
    /// 模拟「构造点在窗口 Attach 之前」的时序：起初报兜底尺寸，窗口显示后报真实尺寸。
    /// 这正是生产里构造期与首次刷新目标框不同的原因（App.axaml.cs 先解析 VM 再构造 MainWindow）。
    /// </summary>
    private sealed class SteppedWindowMetrics : IWindowMetricsService
    {
        private PixelSize size = Cafe.Launcher.UI.Helpers.BackgroundImageDecoder.FallbackTarget;

        public event Action? PhysicalSizeChanged
        {
            add { }
            remove { }
        }

        public void ReportRealWindowSize() => size = new PixelSize(1300, 754);

        public PixelSize GetPhysicalClientSize() => size;
    }

    private sealed class TestImage : IImage, IDisposable
    {
        public Size Size => new(1, 1);

        public void Draw(DrawingContext context, Rect sourceRect, Rect destRect)
        {
        }

        public void Dispose()
        {
            IsDisposed = true;
        }

        public bool IsDisposed { get; private set; }
    }
}
