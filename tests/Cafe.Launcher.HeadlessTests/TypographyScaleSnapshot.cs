using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.VisualTree;

namespace Cafe.Launcher.HeadlessTests;

/// <summary>放大已实现的控件文字，释放时还原；不缩放窗口、边距或命中区域。</summary>
internal sealed class TypographyScaleSnapshot : IDisposable
{
    private readonly (Control Control, double Size)[] previous;

    public TypographyScaleSnapshot(Control root, double scale)
    {
        // 先拍下所有继承值再写入，避免父控件先放大后子控件二次放大。
        previous = root.GetVisualDescendants().OfType<Control>().Prepend(root)
            .Select(control => (control, TextElement.GetFontSize(control)))
            .ToArray();
        foreach (var (control, size) in previous)
        {
            TextElement.SetFontSize(control, size * scale);
        }
    }

    public void Dispose()
    {
        foreach (var (control, size) in previous)
        {
            TextElement.SetFontSize(control, size);
        }
    }
}
