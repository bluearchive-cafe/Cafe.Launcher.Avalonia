using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Cafe.Launcher.UI.Constants;
using Cafe.Launcher.UI.Controls;

namespace Cafe.Launcher.HeadlessTests;

public sealed class FluentIconTests
{
    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void DrawsPixelAlignedOutlineAndUpdatesWhenGlyphChanges(double scale)
    {
        var icon = new FluentIcon { Glyph = FluentGlyphs.Setting, FontSize = 18, Foreground = Brushes.Black };
        var window = new Window { Content = icon, Width = 31, Height = 33 };
        window.Show();
        try
        {
            window.SetRenderScaling(scale);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(scale, window.RenderScaling);
            var (first, transform) = DrawOutline(icon);
            Assert.InRange(first.Geometry!.Bounds.Width, 8, 18);
            Assert.Equal(Math.Round(transform.M31 * scale), transform.M31 * scale, 6);
            Assert.Equal(Math.Round(transform.M32 * scale), transform.M32 * scale, 6);

            icon.Glyph = FluentGlyphs.ChevronDown;
            icon.Foreground = Brushes.White;
            Dispatcher.UIThread.RunJobs();
            var (second, _) = DrawOutline(icon);
            Assert.True(second.Geometry!.Bounds.Height < first.Geometry.Bounds.Height);
            Assert.Same(Brushes.White, second.Brush);

            icon.Glyph = string.Empty;
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(Draw(icon).Children);
        }
        finally
        {
            window.Close();
        }
    }

    private static (GeometryDrawing Drawing, Matrix Transform) DrawOutline(FluentIcon icon)
    {
        var clip = Assert.IsType<DrawingGroup>(Assert.Single(Draw(icon).Children));
        var translated = Assert.IsType<DrawingGroup>(Assert.Single(clip.Children));
        return (Assert.IsType<GeometryDrawing>(Assert.Single(translated.Children)), translated.Transform!.Value);
    }

    private static DrawingGroup Draw(FluentIcon icon)
    {
        var drawing = new DrawingGroup();
        using var context = drawing.Open();
        icon.Render(context);
        return drawing;
    }
}
