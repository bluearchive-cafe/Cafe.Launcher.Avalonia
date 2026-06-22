using System.Globalization;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Cafe.Launcher.Avalonia.Converters;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.HeadlessTests;

public sealed class ConverterHeadlessTests
{
    [AvaloniaTheory]
    [InlineData(ToastSeverity.Info)]
    [InlineData(ToastSeverity.Success)]
    [InlineData(ToastSeverity.Warning)]
    [InlineData(ToastSeverity.Error)]
    public void ToastSeverityConverter_ReturnsConfiguredBrush(ToastSeverity severity)
    {
        var result = ToastSeverityToBrushConverter.Instance.Convert(
            severity,
            typeof(IBrush),
            null,
            CultureInfo.InvariantCulture);

        Assert.IsAssignableFrom<IBrush>(result);
    }

    [AvaloniaFact]
    public void ToastSeverityConverter_WhenValueIsInvalid_ReturnsNull()
    {
        Assert.Null(ToastSeverityToBrushConverter.Instance.Convert(
            "invalid",
            typeof(IBrush),
            null,
            CultureInfo.InvariantCulture));
    }

    [AvaloniaFact]
    public void ToastSeverityConverter_ConvertBackThrows()
    {
        Assert.Throws<NotSupportedException>(
            () => ToastSeverityToBrushConverter.Instance.ConvertBack(
                null,
                typeof(ToastSeverity),
                null,
                CultureInfo.InvariantCulture));
    }
}
