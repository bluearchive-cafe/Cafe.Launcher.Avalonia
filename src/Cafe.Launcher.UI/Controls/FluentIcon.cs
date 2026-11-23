using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;

namespace Cafe.Launcher.UI.Controls;

public sealed class FluentIcon : FAFontIcon
{
    private Geometry? _outline;
    private Size _glyphSize;

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_outline is not null)
        {
            return _glyphSize;
        }

        if (string.IsNullOrEmpty(Glyph))
        {
            return default;
        }

        var text = new FormattedText(Glyph, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily, FontStyle, FontWeight), FontSize, Brushes.Black);
        _outline = text.BuildGeometry(default);
        _glyphSize = new Size(text.Width, text.Height);
        return _glyphSize;
    }

    public override void Render(DrawingContext context)
    {
        if (_outline is null)
        {
            return;
        }

        // Draw the outline at the current DPI instead of using Skia's text glyph rasterization.
        var scale = LayoutHelper.GetLayoutScale(this);
        var x = LayoutHelper.RoundLayoutValue((Bounds.Width - _glyphSize.Width) / 2, scale);
        var y = LayoutHelper.RoundLayoutValue((Bounds.Height - _glyphSize.Height) / 2, scale);
        using var clip = context.PushClip(new Rect(Bounds.Size));
        using var offset = context.PushTransform(Matrix.CreateTranslation(x, y));
        context.DrawGeometry(Foreground, null, _outline);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == GlyphProperty || change.Property == FontFamilyProperty ||
            change.Property == FontSizeProperty || change.Property == FontStyleProperty ||
            change.Property == FontWeightProperty)
        {
            _outline = null;
            InvalidateVisual();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (e.RootVisual is TopLevel topLevel)
        {
            topLevel.ScalingChanged += OnScalingChanged;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (e.RootVisual is TopLevel topLevel)
        {
            topLevel.ScalingChanged -= OnScalingChanged;
        }

        base.OnDetachedFromVisualTree(e);
    }

    private void OnScalingChanged(object? sender, EventArgs e) => InvalidateVisual();
}
