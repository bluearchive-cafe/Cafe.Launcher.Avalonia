using System.Runtime.InteropServices;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Avalonia;
using Avalonia.Media;
using Cafe.Launcher.Testing;
using Cafe.Launcher.UI.Controls.VideoBackground;

namespace Cafe.Launcher.Tests;

public sealed class VideoBackgroundRuntimeTests
{
    [Fact]
    public void GetLibraryPaths_UsesArchitectureSpecificTrustedLocations()
    {
        var linux = VideoBackgroundRuntime.GetLibraryPaths(OSPlatform.Linux, Architecture.X64, "base");
        Assert.Equal(Path.Combine("base", "libmpv", "linux-x64", "libmpv.so.2"), linux[0]);
        Assert.Contains("/usr/lib/x86_64-linux-gnu/libmpv.so.2", linux);
        var mac = VideoBackgroundRuntime.GetLibraryPaths(OSPlatform.OSX, Architecture.Arm64, "base");
        Assert.Equal(Path.Combine("base", "libmpv", "osx-arm64", "libmpv.2.dylib"), mac[0]);
        Assert.Contains("/opt/homebrew/lib/libmpv.2.dylib", mac);
        Assert.DoesNotContain("/usr/local/lib/libmpv.2.dylib", mac);
        Assert.Empty(VideoBackgroundRuntime.GetLibraryPaths(OSPlatform.Linux, Architecture.X86, "base"));
    }

    [Fact]
    public void LinuxSystemLibraryPaths_UseOnlyAbsoluteArchitectureSpecificLocations()
    {
        var paths = VideoBackgroundRuntime.GetLinuxSystemLibraryPaths(Architecture.X64, "libasound.so.2");
        Assert.Contains("/usr/lib/x86_64-linux-gnu/libasound.so.2", paths);
        Assert.All(paths, path => Assert.StartsWith("/", path, StringComparison.Ordinal));
        Assert.Empty(VideoBackgroundRuntime.GetLinuxSystemLibraryPaths(Architecture.X86, "libasound.so.2"));
    }

    [Fact]
    public async Task OpenLibraryCandidates_WhenFirstLibraryIsInvalid_LoadsTheNextRuntime()
    {
        var available = VideoBackgroundRuntime.FindLibraryPath();
        Assert.SkipUnless(available is not null, "Requires an available libmpv runtime.");
        Assert.NotNull(available);
        using var directory = TestDirectory.Create();
        var invalid = directory.Sub("invalid-library");
        await File.WriteAllTextAsync(invalid, "not a native library");
        var before = MpvNative.ActiveLibraryCount;
        using (var native = VideoBackgroundRuntime.OpenLibraryCandidates([invalid, invalid, available]))
        {
            var player = native.NewPlayer();
            Assert.NotEqual(0, player);
            native.DestroyPlayer(player);
        }
        Assert.Equal(before, MpvNative.ActiveLibraryCount);
    }

    [Fact]
    public void OpenLibraryCandidates_WhenNoneExist_ReportsUnavailable()
    {
        using var directory = TestDirectory.Create();
        Assert.Throws<DllNotFoundException>(() => VideoBackgroundRuntime.OpenLibraryCandidates([directory.Sub("missing")]));
    }

    [Fact]
    public void CaptureVideoFrame_WhenFrameUnavailable_ReusesCommandArgumentsAndReleasesThem()
    {
        var library = VideoBackgroundRuntime.FindLibraryPath();
        Assert.SkipUnless(library is not null, "Requires an available libmpv runtime.");
        Assert.NotNull(library);
        var native = new MpvNative(library);
        var player = native.NewPlayer();
        Assert.NotEqual(0, player);
        try
        {
            MpvNative.Check(native.SetOption(player, "vo", "null"), "vo");
            MpvNative.Check(native.InitializePlayer(player), "initialize");
            Assert.Null(native.CaptureVideoFrame(player));
            var allocations = native.AllocationCount;
            Assert.Null(native.CaptureVideoFrame(player));
            Assert.Equal(allocations, native.AllocationCount);
        }
        finally
        {
            native.DestroyPlayer(player);
            native.Dispose();
        }
        Assert.Equal(0, native.AllocationCount);
        Assert.Throws<ObjectDisposedException>(() => native.CaptureVideoFrame(0));
    }

    [Fact]
    public void FormatFillColor_WhenAvaloniaUsesNamedColor_ReturnsNumericMpvSyntax()
    {
        Assert.Equal("#FF000000", VideoBackgroundControl.FormatFillColor(Colors.Black));
        Assert.Equal("#80112233", VideoBackgroundControl.FormatFillColor(Color.FromArgb(128, 17, 34, 51)));
    }

    [Theory]
    [InlineData(false, true, false, false, true)]
    [InlineData(false, true, true, false, false)]
    [InlineData(false, true, true, true, true)]
    [InlineData(false, false, true, true, false)]
    [InlineData(true, true, false, false, false)]
    [InlineData(true, true, true, true, false)]
    public void CanPresentFirstFrame_RequiresKnownSizeAndEitherPlaybackOrStillFrame(
        bool alreadyPresented, bool sizeKnown, bool paused, bool showStillFrame, bool expected) =>
        Assert.Equal(expected,
            VideoBackgroundControl.CanPresentFirstFrame(alreadyPresented, sizeKnown, paused, showStillFrame));

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("wallpaper.png", false)]
    [InlineData("movie.MP4", true)]
    [InlineData("movie.mkv", true)]
    [InlineData("movie.webm", true)]
    public void IsVideoFile_RecognizesOnlySupportedExtensions(string? path, bool expected) =>
        Assert.Equal(expected, VideoBackgroundRuntime.IsVideoFile(path));

    [Theory]
    [InlineData(1, 1300, 754)]
    [InlineData(1.25, 1625, 942)]
    [InlineData(1.5, 1950, 1131)]
    [InlineData(2, 2600, 1508)]
    public void FramebufferSize_UsesAvaloniaPhysicalPixelTruncation(double scale, int width, int height) =>
        Assert.Equal(new PixelSize(width, height), VideoBackgroundControl.GetFramebufferSize(new Size(1300, 754), scale));

    [Fact]
    public void NativeRuntime_WhenExportIsMissing_ReleasesLibrary()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Uses a known Windows library without mpv exports.");
        var before = MpvNative.ActiveLibraryCount;
        Assert.Throws<EntryPointNotFoundException>(() => new MpvNative(Path.Combine(Environment.SystemDirectory, "kernel32.dll")));
        Assert.Equal(before, MpvNative.ActiveLibraryCount);
    }

    [Fact]
    public async Task BundledRuntime_AppliesPlaybackSettingsReportsInvalidMediaAndReleasesArena()
    {
        var library = VideoBackgroundRuntime.FindLibraryPath();
        Assert.SkipUnless(library is not null, "Requires an available libmpv runtime.");
        Assert.NotNull(library);
        var before = MpvNative.ActiveLibraryCount;
        var native = new MpvNative(library);
        using var directory = TestDirectory.Create();
        var invalidMedia = directory.Sub("invalid.mp4");
        await File.WriteAllTextAsync(invalidMedia, "not a movie");
        var player = native.NewPlayer();
        Assert.NotEqual(0, player);
        try
        {
            foreach (var option in new[] { ("config", "no"), ("vo", "null"), ("audio", "no") })
                MpvNative.Check(native.SetOption(player, option.Item1, option.Item2), option.Item1);
            MpvNative.Check(native.InitializePlayer(player), "initialize");
            foreach (var property in new[]
            {
                ("pause", "yes"), ("volume", "35"), ("keepaspect", "yes"),
                ("panscan", "1")
            }) MpvNative.Check(native.SetProperty(player, property.Item1, property.Item2), property.Item1);
            VideoBackgroundControl.SetBackgroundColor(native, player, Colors.Black);
            Assert.Equal("yes", native.Read(player, "pause"));
            Assert.Equal("unavailable", native.Read(player, "nonexistent-property"));
            Assert.Contains("mpv", native.Read(player, "mpv-version"), StringComparison.OrdinalIgnoreCase);
            // System libraries use their distribution's licensing configuration;
            // only the runtime that we redistribute must match our LGPL build.
            if (library.StartsWith(Path.Combine(AppContext.BaseDirectory, "libmpv") + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                Assert.Contains("-Dgpl=false", native.Read(player, "mpv-configuration"), StringComparison.Ordinal);
            native.Load(player, invalidMedia);
            var failed = false;
            await TestWait.UntilAsync(() =>
            {
                var value = Marshal.PtrToStructure<MpvNative.PlayerEvent>(native.WaitEvent(player, 0));
                failed = value.Id == 7 && value.Data != 0 && Marshal.ReadInt32(value.Data) == 4;
                return failed;
            }, TimeSpan.FromSeconds(5), "Invalid media should produce an END_FILE error");
            Assert.True(failed);
            native.Allocate(new MpvNative.Framebuffer(1, 100, 200));
            var utf8 = native.String("背景");
            Assert.Equal("背景", Marshal.PtrToStringUTF8(utf8));
            var parameters = native.Parameters(new MpvNative.Parameter(1, utf8));
            Assert.Equal(0, Marshal.PtrToStructure<MpvNative.Parameter>(parameters + Marshal.SizeOf<MpvNative.Parameter>()).Type);
            Assert.Equal(3, native.AllocationCount);
            Assert.Throws<InvalidOperationException>(() => MpvNative.Check(-1, "expected failure"));
        }
        finally
        {
            native.DestroyPlayer(player);
            native.Dispose();
            native.Dispose();
        }
        Assert.Equal(0, native.AllocationCount);
        Assert.Equal(before, MpvNative.ActiveLibraryCount);
        Assert.Throws<ObjectDisposedException>(() => native.String("disposed"));
        Assert.Throws<ObjectDisposedException>(() => native.Parameters());
        Assert.Throws<ObjectDisposedException>(() => native.Allocate(0));
        Assert.Throws<ObjectDisposedException>(() => native.Read(0, "pause"));
        Assert.Throws<ObjectDisposedException>(() => native.Load(0, invalidMedia));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BundledRuntime_WhenLocalVideoContainsNetworkReferences_RejectsWithoutHttpRequests(bool hls)
    {
        var library = VideoBackgroundRuntime.FindLibraryPath();
        Assert.SkipUnless(library is not null, "Requires an available libmpv runtime.");
        Assert.NotNull(library);
        using var directory = TestDirectory.Create();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var media = directory.Sub("background.mp4");
        await File.WriteAllTextAsync(media, hls
            ? $"#EXTM3U\n#EXT-X-TARGETDURATION:10\n#EXTINF:10,\nhttp://127.0.0.1:{port}/segment.ts\n#EXT-X-ENDLIST\n"
            : $"#EXTM3U\nhttp://127.0.0.1:{port}/video.mp4\n");
        using var cancellation = new CancellationTokenSource();
        var observation = ObserveHttpRequestAsync(listener, cancellation.Token);
        using var native = new MpvNative(library);
        var player = native.NewPlayer();
        Assert.NotEqual(0, player);
        try
        {
            // The production configuration is shared; only GPU/audio output is disabled here.
            VideoBackgroundControl.ConfigurePlayer(native, player, playbackPaused: false);
            MpvNative.Check(native.SetOption(player, "vo", "null"), "vo");
            MpvNative.Check(native.SetOption(player, "audio", "no"), "audio");
            MpvNative.Check(native.InitializePlayer(player), "initialize");
            native.Load(player, media);
            await TestWait.UntilAsync(() =>
            {
                var value = Marshal.PtrToStructure<MpvNative.PlayerEvent>(native.WaitEvent(player, 0));
                return value.Id == 7 && value.Data != 0 && Marshal.ReadInt32(value.Data) == 4;
            }, TimeSpan.FromSeconds(5), "Network references must end in a media error.");
        }
        finally
        {
            native.DestroyPlayer(player);
            await cancellation.CancelAsync();
        }

        Assert.False(await observation, "A local background must not issue native HTTP requests.");
    }

    [Fact]
    public async Task Runtime_WhenGivenHttpAddress_DoesNotOpenNetworkConnection()
    {
        var library = VideoBackgroundRuntime.FindLibraryPath();
        Assert.SkipUnless(library is not null, "Requires an available libmpv runtime.");
        Assert.NotNull(library);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var cancellation = new CancellationTokenSource();
        var observation = ObserveHttpRequestAsync(listener, cancellation.Token);
        using var native = new MpvNative(library);
        var player = native.NewPlayer();
        Assert.NotEqual(0, player);
        try
        {
            VideoBackgroundControl.ConfigurePlayer(native, player, playbackPaused: false);
            MpvNative.Check(native.SetOption(player, "vo", "null"), "vo");
            MpvNative.Check(native.SetOption(player, "audio", "no"), "audio");
            MpvNative.Check(native.InitializePlayer(player), "initialize");
            var rejectedByPath = false;
            try { native.Load(player, $"http://127.0.0.1:{port}/background.mp4"); }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
            {
                // Windows rejects URI syntax as an invalid filesystem path.
                rejectedByPath = true;
            }
            if (!rejectedByPath)
            {
                await TestWait.UntilAsync(() =>
                {
                    var value = Marshal.PtrToStructure<MpvNative.PlayerEvent>(native.WaitEvent(player, 0));
                    return value.Id == 7 && value.Data != 0 && Marshal.ReadInt32(value.Data) == 4;
                }, TimeSpan.FromSeconds(5), "A URI-like path must fail as a local file.");
            }
        }
        finally
        {
            native.DestroyPlayer(player);
            await cancellation.CancelAsync();
        }
        Assert.False(await observation, "The player must not open a top-level HTTP URL.");
    }

    [Fact]
    public async Task BundledRuntime_WithProductionAccessPolicy_DecodesLocalVideo()
    {
        var library = VideoBackgroundRuntime.FindLibraryPath();
        Assert.SkipUnless(library is not null, "Requires an available libmpv runtime.");
        Assert.NotNull(library);
        using var directory = TestDirectory.Create();
        var media = directory.Sub("local.mp4");
        // A self-contained 2x2 YUV frame exercises real demux/decode without an
        // external encoder or downloaded fixture; mpv detects the container by content.
        await File.WriteAllBytesAsync(media,
            [.. Encoding.ASCII.GetBytes("YUV4MPEG2 W2 H2 F1:1 Ip A1:1 C420jpeg\nFRAME\n"), 16, 16, 16, 16, 128, 128]);
        using var native = new MpvNative(library);
        var player = native.NewPlayer();
        Assert.NotEqual(0, player);
        try
        {
            VideoBackgroundControl.ConfigurePlayer(native, player, playbackPaused: true);
            MpvNative.Check(native.SetOption(player, "vo", "null"), "vo");
            MpvNative.Check(native.SetOption(player, "audio", "no"), "audio");
            MpvNative.Check(native.InitializePlayer(player), "initialize");
            native.Load(player, media);
            await TestWait.UntilAsync(() => native.Read(player, "video-params/w") == "2",
                TimeSpan.FromSeconds(5), "Local media must still decode with network references disabled.");
        }
        finally
        {
            native.DestroyPlayer(player);
        }
    }

    [Fact]
    public async Task Runtime_WithThumbnailOptions_CapturesTheFirstFrame()
    {
        var library = VideoBackgroundRuntime.FindLibraryPath();
        Assert.SkipUnless(library is not null, "Requires an available libmpv runtime.");
        Assert.NotNull(library);
        using var directory = TestDirectory.Create();
        var media = directory.Sub("frame.mp4");
        await File.WriteAllBytesAsync(media,
            [.. Encoding.ASCII.GetBytes("YUV4MPEG2 W2 H2 F1:1 Ip A1:1 C420jpeg\nFRAME\n"), 16, 16, 16, 16, 128, 128]);
        using var native = new MpvNative(library);
        var player = native.NewPlayer();
        Assert.NotEqual(0, player);
        try
        {
            VideoBackgroundControl.ConfigurePlayer(native, player, playbackPaused: true);
            foreach (var option in new[]
            {
                ("vo", "null"), ("audio", "no"), ("hwdec", "no"), ("loop-file", "no"),
                ("start", "0"), ("resume-playback", "no"),
                ("vf", "lavfi=[scale=256:176:force_original_aspect_ratio=decrease]")
            }) MpvNative.Check(native.SetOption(player, option.Item1, option.Item2), option.Item1);
            MpvNative.Check(native.InitializePlayer(player), "initialize thumbnail");
            native.Load(player, media);
            MpvNative.VideoFrame? frame = null;
            await TestWait.UntilAsync(() =>
            {
                _ = native.WaitEvent(player, 0);
                frame = native.CaptureVideoFrame(player);
                return frame is not null;
            }, TimeSpan.FromSeconds(5), "The thumbnail player must return a decoded frame.");
            Assert.NotNull(frame);
            Assert.InRange(frame.Width, 1, 256);
            Assert.InRange(frame.Height, 1, 176);
            Assert.Equal(frame.Width * frame.Height * 4, frame.Pixels.Length);
        }
        finally { native.DestroyPlayer(player); }
    }

    private static async Task<bool> ObserveHttpRequestAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        var connected = false;
        try
        {
            using var client = await listener.AcceptTcpClientAsync(cancellationToken);
            connected = true;
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, leaveOpen: true);
            while (!string.IsNullOrEmpty(await reader.ReadLineAsync(cancellationToken))) { }
            await stream.WriteAsync(Encoding.ASCII.GetBytes(
                "HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return connected;
        }
    }
}
