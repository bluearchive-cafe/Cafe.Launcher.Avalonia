using System.Net;
using System.Net.Http;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.Services.VideoWallpaper;
using Cafe.Launcher.Avalonia.ViewModels;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class BackgroundViewModelTests : IDisposable
{
    private static readonly byte[] PngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII=");

    private readonly string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public BackgroundViewModelTests()
    {
        Directory.CreateDirectory(tempDir);
    }

    [Fact]
    public async Task UpdateBackgroundImageAsync_WhenRemoteImageIsValid_LoadsAndCachesImage()
    {
        var hash = await ComputeHashAsync(PngBytes);
        using var cache = CreateCache(new ImageHandler(PngBytes));
        var themeRefreshCount = 0;
        using var viewModel = new BackgroundViewModel(
            cache,
            new LocalDiagnostics(),
            _ => themeRefreshCount++,
            _ => new TestImage(),
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
        using var cache = CreateCache(new StatusHandler(HttpStatusCode.BadGateway));
        using var viewModel = new BackgroundViewModel(
            cache,
            new LocalDiagnostics(),
            _ => { },
            _ => new TestImage(),
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
    public async Task LoadCustomBackgroundAsync_WhenFileIsInvalid_ReturnsNull()
    {
        var path = Path.Combine(tempDir, "invalid.png");
        await File.WriteAllTextAsync(path, "not-an-image");
        using var cache = CreateCache(new ImageHandler(PngBytes));
        using var viewModel = new BackgroundViewModel(
            cache,
            new LocalDiagnostics(),
            _ => { });

        var result = await viewModel.LoadCustomBackgroundAsync(path);

        Assert.Null(result);
    }

    [Fact]
    public async Task LoadCustomBackgroundAsync_WhenPathDoesNotExist_ReturnsNull()
    {
        using var cache = CreateCache(new ImageHandler(PngBytes));
        using var viewModel = new BackgroundViewModel(
            cache,
            new LocalDiagnostics(),
            _ => { });

        var result = await viewModel.LoadCustomBackgroundAsync(
            Path.Combine(tempDir, "missing.png"));

        Assert.Null(result);
    }

    [Fact]
    public async Task LoadCustomBackgroundAsync_WhenFolderHasNoSupportedImages_ReturnsNull()
    {
        var folder = Path.Combine(tempDir, "empty");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "readme.txt"), "text");
        using var cache = CreateCache(new ImageHandler(PngBytes));
        using var viewModel = new BackgroundViewModel(
            cache,
            new LocalDiagnostics(),
            _ => { });

        var result = await viewModel.LoadCustomBackgroundAsync(folder);

        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateBackgroundImageAsync_WhenRemoteDownloadIsCanceled_PropagatesCancellation()
    {
        using var cache = CreateCache(new CancellationHandler());
        using var viewModel = new BackgroundViewModel(
            cache,
            new LocalDiagnostics(),
            _ => { },
            _ => new TestImage(),
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
        using var cache = CreateCache(new ImageHandler(PngBytes));
        var bundled = new TestImage();
        using var viewModel = new BackgroundViewModel(
            cache,
            new LocalDiagnostics(),
            _ => { },
            _ => new TestImage(),
            () => bundled);

        await viewModel.UpdateBackgroundImageAsync(
            new LauncherSettings { BackgroundSource = BackgroundSources.Remote },
            new LauncherStatusSnapshot(),
            CancellationToken.None);

        Assert.Same(bundled, viewModel.BackgroundImageSource);
    }

    [Fact]
    public async Task UpdateBackgroundImageAsync_WhenCustomImageIsValid_UsesCustomImage()
    {
        var path = Path.Combine(tempDir, "custom.png");
        await File.WriteAllBytesAsync(path, PngBytes);
        using var cache = CreateCache(new ImageHandler(PngBytes));
        var custom = new TestImage();
        using var viewModel = new BackgroundViewModel(
            cache,
            new LocalDiagnostics(),
            _ => { },
            _ => custom,
            () => new TestImage());

        await viewModel.UpdateBackgroundImageAsync(
            new LauncherSettings
            {
                BackgroundSource = BackgroundSources.Custom,
                CustomBackgroundPath = path
            },
            null,
            CancellationToken.None);

        Assert.Same(custom, viewModel.BackgroundImageSource);
    }

    [Fact]
    public void ApplyBackgroundPresentation_WhenUniform_UsesConfiguredFillColor()
    {
        using var cache = CreateCache(new ImageHandler(PngBytes));
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
        using var cache = CreateCache(new ImageHandler(PngBytes));
        var viewModel = new BackgroundViewModel(
            cache,
            new LocalDiagnostics(),
            _ => { },
            _ => new TestImage(),
            () => new TestImage());

        viewModel.Dispose();
        viewModel.Dispose();
    }

    [Fact]
    public async Task LoadCustomBackgroundAsync_WhenCanceled_PropagatesCancellation()
    {
        using var cache = CreateCache(new ImageHandler(PngBytes));
        using var viewModel = new BackgroundViewModel(
            cache,
            new LocalDiagnostics(),
            _ => { });
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => viewModel.LoadCustomBackgroundAsync(tempDir, cts.Token));
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

    [Fact]
    public async Task UpdateBackgroundImageAsync_WhenVideoSource_LoadsPlaysAndShowsFrame()
    {
        var fake = new FakeVideoWallpaperEngine();
        using var vm = CreateViewModelWithEngine(() => fake);
        var settings = new LauncherSettings
        {
            BackgroundSource = BackgroundSources.Video,
            VideoBackgroundPath = @"C:\v.mp4",
            VideoBackgroundVolume = 70,
            VideoBackgroundMuted = false,
        };

        await vm.UpdateBackgroundImageAsync(settings, null, CancellationToken.None);
        fake.RaiseFrameReady();

        Assert.Equal(@"C:\v.mp4", fake.LoadedPath);
        Assert.Equal(1, fake.PlayCount);
        Assert.Equal(70, fake.LastVolume);
        Assert.False(fake.LastMuted);
        Assert.Same(fake.CurrentFrame, vm.BackgroundImageSource);
    }

    [Fact]
    public async Task UpdateBackgroundImageAsync_WhenVideoLoadFails_FallsBackToBundled()
    {
        var fake = new FakeVideoWallpaperEngine { LoadResult = false };
        using var vm = CreateViewModelWithEngine(() => fake);
        var settings = new LauncherSettings
        {
            BackgroundSource = BackgroundSources.Video,
            VideoBackgroundPath = @"C:\missing.mp4",
        };

        await vm.UpdateBackgroundImageAsync(settings, null, CancellationToken.None);

        Assert.NotNull(vm.BackgroundImageSource);
        Assert.NotSame(fake.CurrentFrame, vm.BackgroundImageSource);
    }

    [Fact]
    public async Task UpdateBackgroundImageAsync_WhenSwitchingAwayFromVideo_DisposesEngine()
    {
        var fake = new FakeVideoWallpaperEngine();
        using var vm = CreateViewModelWithEngine(() => fake);
        await vm.UpdateBackgroundImageAsync(
            new LauncherSettings { BackgroundSource = BackgroundSources.Video, VideoBackgroundPath = @"C:\v.mp4" },
            null, CancellationToken.None);

        await vm.UpdateBackgroundImageAsync(
            new LauncherSettings { BackgroundSource = BackgroundSources.Bundled },
            null, CancellationToken.None);

        Assert.True(fake.Disposed);
        Assert.Equal(1, fake.StopCount);
    }

    [Fact]
    public async Task SetPlaybackActive_WhenToggled_PausesAndResumesVideo()
    {
        var fake = new FakeVideoWallpaperEngine();
        using var vm = CreateViewModelWithEngine(() => fake);
        await vm.UpdateBackgroundImageAsync(
            new LauncherSettings { BackgroundSource = BackgroundSources.Video, VideoBackgroundPath = @"C:\v.mp4" },
            null, CancellationToken.None);

        vm.SetPlaybackActive(false);
        vm.SetPlaybackActive(true);

        Assert.Equal(1, fake.PauseCount);
        Assert.Equal(2, fake.PlayCount);
    }

    [Fact]
    public async Task UpdateBackgroundImageAsync_WhenPlaybackInactive_DoesNotPlayOnLoad()
    {
        var fake = new FakeVideoWallpaperEngine();
        using var vm = CreateViewModelWithEngine(() => fake);
        vm.SetPlaybackActive(false);

        await vm.UpdateBackgroundImageAsync(
            new LauncherSettings { BackgroundSource = BackgroundSources.Video, VideoBackgroundPath = @"C:\v.mp4" },
            null, CancellationToken.None);

        Assert.Equal(0, fake.PlayCount);
        Assert.Same(fake.CurrentFrame, vm.BackgroundImageSource);
    }

    [Fact]
    public async Task UpdateBackgroundImageAsync_WhenSwitchingFromImageToVideo_DisposesPreviousImage()
    {
        var customImage = new TestImage();
        var fake = new FakeVideoWallpaperEngine();
        using var cache = CreateCache(new ImageHandler(PngBytes));
        using var vm = new BackgroundViewModel(
            cache,
            new LocalDiagnostics(),
            _ => { },
            _ => customImage,
            () => new TestImage(),
            () => fake)
        {
            ImageDisposeScheduler = image => image.Dispose(),
        };
        var customPath = Path.Combine(tempDir, "wallpaper.png");
        await File.WriteAllBytesAsync(customPath, PngBytes);

        await vm.UpdateBackgroundImageAsync(
            new LauncherSettings { BackgroundSource = BackgroundSources.Custom, CustomBackgroundPath = customPath },
            null,
            CancellationToken.None);
        Assert.Same(customImage, vm.BackgroundImageSource);

        await vm.UpdateBackgroundImageAsync(
            new LauncherSettings { BackgroundSource = BackgroundSources.Video, VideoBackgroundPath = @"C:\v.mp4" },
            null,
            CancellationToken.None);

        Assert.Same(fake.CurrentFrame, vm.BackgroundImageSource);
        Assert.True(customImage.Disposed);
    }

    private BackgroundViewModel CreateViewModelWithEngine(Func<IVideoWallpaperEngine> engineFactory)
    {
        return new BackgroundViewModel(
            CreateCache(new ImageHandler(PngBytes)),
            new LocalDiagnostics(),
            _ => { },
            _ => new TestImage(),
            () => new TestImage(),
            engineFactory);
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

    private ImageCacheService CreateCache(HttpMessageHandler handler) =>
        new(
            new FixedHttpClientLeaseSource(
                handler,
                baseAddress: null,
                timeout: Timeout.InfiniteTimeSpan),
            new Crc64Service(),
            RemoteHttpUrlValidator.CreateForTesting(),
            tempDir);

    private async Task<string> ComputeHashAsync(byte[] bytes)
    {
        var path = Path.Combine(tempDir, "hash-source.png");
        await File.WriteAllBytesAsync(path, bytes);
        return await new Crc64Service().ComputeFileAsync(path);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private sealed class ImageHandler(byte[] bytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes)
            });
    }

    private sealed class StatusHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode));
    }

    private sealed class CancellationHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromCanceled<HttpResponseMessage>(cancellationToken);
    }

    private sealed class TestImage : IImage, IDisposable
    {
        public bool Disposed { get; private set; }

        public Size Size => new(1, 1);

        public void Draw(DrawingContext context, Rect sourceRect, Rect destRect)
        {
        }

        public void Dispose() => Disposed = true;
    }
}
