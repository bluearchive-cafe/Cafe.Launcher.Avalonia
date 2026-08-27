using System;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace Cafe.Launcher.Avalonia.Helpers;

/// <summary>
/// 把 x:String 形态的 Launcher.Elevation.Shadow.* token 解析为 BoxShadows。
/// BoxShadows 没有 XAML TypeConverter，Setter 无法直接引用字符串资源（AVLN3000），
/// 因此经本扩展在加载期完成解析，使阴影值保持单一 token 来源。
/// </summary>
public sealed class BoxShadowsExtension : MarkupExtension
{
    public BoxShadowsExtension()
    {
    }

    public BoxShadowsExtension(object value)
    {
        Value = value;
    }

    public object? Value { get; }

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        BoxShadows.Parse(Value?.ToString() ?? string.Empty);
}
