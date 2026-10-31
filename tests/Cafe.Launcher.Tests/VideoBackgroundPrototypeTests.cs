#if DEBUG
using MpvPrototypeNative = Cafe.Launcher.UI.Controls.VideoBackground.MpvNative;
using System.Runtime.InteropServices;
using Avalonia;
using Cafe.Launcher.UI.Controls.VideoBackgroundPrototype;

namespace Cafe.Launcher.Tests;

public sealed class VideoBackgroundPrototypeTests
{
    [Theory]
    [InlineData(1.0, 1300, 754)]
    [InlineData(1.25, 1625, 942)]
    [InlineData(1.5, 1950, 1131)]
    [InlineData(2.0, 2600, 1508)]
    public void FramebufferSize_UsesTheSameTruncationAsOpenGlControlBase(double scale, int width, int height)
    {
        Assert.Equal(new PixelSize(width, height),
            VideoBackgroundPrototypeControl.GetFramebufferSize(new Size(1300, 754), scale));
    }

    [Fact]
    public void NativeRuntime_WhenAnExportIsMissing_ReleasesTheLoadedLibrary()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Uses a known Windows DLL with no mpv exports.");
        var before = MpvPrototypeNative.ActiveLibraryCount;
        Assert.Throws<EntryPointNotFoundException>(() => new MpvPrototypeNative(Path.Combine(Environment.SystemDirectory, "kernel32.dll")));
        Assert.Equal(before, MpvPrototypeNative.ActiveLibraryCount);
    }

    [Fact]
    public void NativeRuntime_DisposeTwice_ReleasesArenaAndLibraryExactlyOnce()
    {
        var library = Environment.GetEnvironmentVariable("CAFE_VIDEO_PROTOTYPE_TEST_LIBRARY");
        Assert.SkipUnless(!string.IsNullOrWhiteSpace(library) && File.Exists(library), "Requires the opt-in native runtime.");
        var before = MpvPrototypeNative.ActiveLibraryCount;
        var native = new MpvPrototypeNative(library!);
        native.Allocate(123);
        native.String("test");
        native.Parameters(new MpvPrototypeNative.Parameter(1, 0));
        Assert.Equal(3, native.AllocationCount);
        native.Dispose();
        native.Dispose();
        Assert.Equal(0, native.AllocationCount);
        Assert.Equal(before, MpvPrototypeNative.ActiveLibraryCount);
    }

    [Fact]
    public void NativeRenderParameters_MatchCAbiAlignment()
    {
        // mpv_render_param is { enum, void* }; an incorrectly packed struct corrupts
        // the parameter list before a managed exception can be raised.
        Assert.Equal(IntPtr.Size == 8 ? 16 : 8, Marshal.SizeOf<MpvPrototypeNative.Parameter>());
        Assert.Equal(IntPtr.Size, Marshal.OffsetOf<MpvPrototypeNative.Parameter>("Data").ToInt32());
        Assert.Equal(16, Marshal.SizeOf<MpvPrototypeNative.Framebuffer>());
        Assert.Equal(IntPtr.Size * 2, Marshal.SizeOf<MpvPrototypeNative.GlInit>());
    }

    [Fact]
    public void NativePlayer_CanLoadTheSelectedRuntimeAndApplyPause()
    {
        var library = Environment.GetEnvironmentVariable("CAFE_VIDEO_PROTOTYPE_TEST_LIBRARY");
        Assert.SkipUnless(!string.IsNullOrWhiteSpace(library) && File.Exists(library),
            "Set CAFE_VIDEO_PROTOTYPE_TEST_LIBRARY to explicitly run the native libmpv smoke test.");
        using var native = new MpvPrototypeNative(library!);
        var player = native.NewPlayer();
        Assert.NotEqual(0, player);
        try
        {
            MpvPrototypeNative.Check(native.SetOption(player, "config", "no"), "config");
            MpvPrototypeNative.Check(native.SetOption(player, "vo", "null"), "vo");
            MpvPrototypeNative.Check(native.SetOption(player, "audio", "no"), "audio");
            MpvPrototypeNative.Check(native.InitializePlayer(player), "initialize");
            MpvPrototypeNative.Check(native.SetProperty(player, "pause", "yes"), "pause");
            Assert.Equal("yes", native.Read(player, "pause"));
            MpvPrototypeNative.Check(native.SetProperty(player, "pause", "no"), "resume");
            Assert.Equal("no", native.Read(player, "pause"));
        }
        finally
        {
            native.DestroyPlayer(player);
        }
    }
}
#endif
