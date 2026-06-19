using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class ToastServiceTests
{
    [Fact]
    public void ToastNotification_IsPureDataWithoutAvaloniaBrush()
    {
        var property = typeof(ToastNotification).GetProperty("IconBrush");

        Assert.Null(property);
    }

    [Theory]
    [InlineData(ToastSeverity.Info, "InformationOutline")]
    [InlineData(ToastSeverity.Success, "CheckCircle")]
    [InlineData(ToastSeverity.Warning, "AlertOutline")]
    [InlineData(ToastSeverity.Error, "AlertCircle")]
    public void IconKind_MapsSeverity(ToastSeverity severity, string expected)
    {
        var notification = new ToastNotification { Severity = severity };

        Assert.Equal(expected, notification.IconKind);
    }
}
