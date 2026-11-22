using Avalonia;
using FluentAvalonia.UI.Controls;

namespace Cafe.Launcher.UI.Controls;

internal sealed class FluentIconSource : FAIconSource
{
    public static readonly StyledProperty<string> GlyphProperty = FAFontIcon.GlyphProperty.AddOwner<FluentIconSource>();
    public static readonly StyledProperty<double> FontSizeProperty = FAFontIcon.FontSizeProperty.AddOwner<FluentIconSource>();

    static FluentIconSource()
    {
        FAIconHelpers.RegisterCustomIconSourceFactory(typeof(FluentIconSource), source =>
        {
            var icon = new FluentIcon
            {
                [!GlyphProperty] = source[!GlyphProperty],
                [!FontSizeProperty] = source[!FontSizeProperty]
            };
            if (source.IsSet(ForegroundProperty))
            {
                icon[!FluentIcon.ForegroundProperty] = source[!ForegroundProperty];
            }

            return icon;
        });
    }

    public string Glyph
    {
        get => GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }
}
