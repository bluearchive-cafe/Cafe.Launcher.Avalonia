using System.Globalization;
using Avalonia.Data;
using Cafe.Launcher.Avalonia.Converters;
using Xunit;

namespace Cafe.Launcher.Avalonia.Tests;

// ADR-041：分段来源按钮与 SelectedResourcePanelUidSource 之间的双向接缝。
public sealed class ResourcePanelSourceSegmentConverterTests
{
    private static readonly CultureInfo TestCulture = CultureInfo.InvariantCulture;

    private readonly ResourcePanelSourceSegmentConverter converter = new();

    [Fact]
    public void Convert_ReflectsWhetherTheValueMatchesTheSegment()
    {
        Assert.True(Assert.IsType<bool>(converter.Convert("custom", typeof(bool?), "custom", TestCulture)));
        Assert.False(Assert.IsType<bool>(converter.Convert("auto", typeof(bool?), "custom", TestCulture)));
        Assert.False(Assert.IsType<bool>(converter.Convert(null, typeof(bool?), "custom", TestCulture)));
    }

    [Fact]
    public void ConvertBack_WhenChecked_ReturnsTheSegmentConstant()
    {
        Assert.Equal("custom", converter.ConvertBack(true, typeof(string), "custom", TestCulture));
        Assert.Equal("auto", converter.ConvertBack(true, typeof(string), "auto", TestCulture));
    }

    [Fact]
    public void ConvertBack_WhenUnchecked_LeavesTheSourceUntouched()
    {
        // 分组的反选不写回：来源属性只由「被勾选的那一段」驱动。
        Assert.Same(BindingOperations.DoNothing, converter.ConvertBack(false, typeof(string), "custom", TestCulture));
        Assert.Same(BindingOperations.DoNothing, converter.ConvertBack(null, typeof(string), "auto", TestCulture));
    }
}

