using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Cafe.Launcher.Core.Services.Diagnostics;
using Cafe.Launcher.UI.Controls.VideoBackground;
using Cafe.Launcher.UI.Helpers;

namespace Cafe.Launcher.UI.Services;

/// <summary>Loads embedded MP4 artwork or a small, silent first frame. The caller owns the returned image.</summary>
internal sealed class VideoThumbnailService(ILauncherDiagnostics diagnostics)
{
    /// <summary>First-frame capture budget; tests shorten it to exercise timeout cleanup without waiting.</summary>
    internal static TimeSpan CaptureTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Prefers MP4 artwork, then the first frame; returns null when neither is available.</summary>
    public async Task<IImage?> LoadThumbnailAsync(string? path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(path) || !VideoBackgroundRuntime.IsVideoFile(path) || !File.Exists(path)) return null;
        try
        {
            var image = await Task.Run(() => LoadThumbnail(path, cancellationToken), cancellationToken);
            BitmapLifetime.ThrowIfCancellationRequested(image, cancellationToken);
            return image;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            await diagnostics.WarningAsync("Background",
                $"Video thumbnail capture failed\npath: {path}\nexception: {exception.Message}", CancellationToken.None);
            return null;
        }
    }

    private static Bitmap? LoadThumbnail(string path, CancellationToken cancellationToken)
    {
        if (Path.GetExtension(path).Equals(".mp4", StringComparison.OrdinalIgnoreCase)
            && TryLoadEmbeddedCover(path, cancellationToken) is { } cover)
            return cover;
        cancellationToken.ThrowIfCancellationRequested();
        var library = VideoBackgroundRuntime.FindLibraryPath();
        if (library is null) return null;
        var frame = CaptureFirstFrame(library, path, cancellationToken);
        if (frame is null) return null;
        cancellationToken.ThrowIfCancellationRequested();
        var pinned = GCHandle.Alloc(frame.Pixels, GCHandleType.Pinned);
        try
        {
            // bgr0's padding byte is undefined, so it must never become an alpha channel.
            return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Opaque, pinned.AddrOfPinnedObject(),
                new PixelSize(frame.Width, frame.Height), new Vector(96, 96), frame.Width * 4);
        }
        finally
        {
            pinned.Free();
        }
    }

    private static Bitmap? TryLoadEmbeddedCover(string path, CancellationToken cancellationToken)
    {
        try
        {
            using var file = File.OpenRead(path);
            var bytes = Mp4CoverReader.Read(file, cancellationToken);
            if (bytes is null) return null;
            using var encoded = new MemoryStream(bytes, writable: false);
            var image = Bitmap.DecodeToHeight(encoded, 176, BitmapInterpolationMode.HighQuality);
            if (image.PixelSize.Width > 256)
            {
                image.Dispose();
                encoded.Position = 0;
                image = Bitmap.DecodeToWidth(encoded, 256, BitmapInterpolationMode.HighQuality);
            }
            BitmapLifetime.ThrowIfCancellationRequested(image, cancellationToken);
            return image;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Artwork is optional. Invalid metadata or an undecodable image still allows a first-frame thumbnail.
            return null;
        }
    }

    private static MpvNative.VideoFrame? CaptureFirstFrame(string library, string path, CancellationToken cancellationToken)
    {
        using var native = new MpvNative(library);
        var player = native.NewPlayer();
        if (player == 0) throw new InvalidOperationException("mpv_create failed.");
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
            native.Load(player, path);
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed < CaptureTimeout)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var playerEvent = Marshal.PtrToStructure<MpvNative.PlayerEvent>(native.WaitEvent(player, 0.05));
                if (playerEvent.Id == 7) return null;
                if (native.Read(player, "video-params/w") != "unavailable"
                    && native.CaptureVideoFrame(player) is { } frame)
                    return frame;
            }
            throw new TimeoutException("Video thumbnail capture timed out.");
        }
        finally
        {
            native.DestroyPlayer(player);
        }
    }
}
