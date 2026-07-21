using System;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Services.VideoWallpaper;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class VideoWallpaperEngineSmokeTests
{
    private static bool LibVlcAvailable()
    {
        try
        {
            LibVLCSharp.Shared.Core.Initialize();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    [Fact]
    public async Task Create_WhenLibVlcAvailable_ReturnsUsableEngine()
    {
        if (!LibVlcAvailable())
            Assert.Skip("libvlc native libraries not available in this environment.");

        using var engine = VideoWallpaperEngineFactory.Create();

        Assert.NotNull(engine);

        var ok = await engine.LoadAsync("C:\\__nonexistent_video__.mp4", default);
        Assert.False(ok);
    }
}
