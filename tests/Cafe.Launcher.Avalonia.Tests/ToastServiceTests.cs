using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class ToastServiceTests
{
    [Fact]
    public void Show_WithOptions_RaisesTitleAndOrderedActions()
    {
        var service = new ToastService();
        ToastNotification? raised = null;
        var primary = new ToastAction(
            "Retry",
            _ => Task.FromResult(ToastActionResult.Success()));
        var secondary = new ToastAction(
            "View log",
            _ => Task.FromResult(ToastActionResult.Success()));
        service.ToastRaised += notification => raised = notification;

        service.Show(new ToastOptions
        {
            Title = "Install failed",
            Message = "Network unavailable",
            Severity = ToastSeverity.Error,
            PrimaryAction = primary,
            SecondaryAction = secondary
        });

        Assert.NotNull(raised);
        Assert.Equal("Install failed", raised.Title);
        Assert.Same(primary, raised.PrimaryAction);
        Assert.Same(secondary, raised.SecondaryAction);
        Assert.True(raised.HasActions);
    }

    [Fact]
    public void Show_WithLegacyArguments_RaisesNotificationWithoutActions()
    {
        var service = new ToastService();
        ToastNotification? raised = null;
        service.ToastRaised += notification => raised = notification;

        service.Show("saved", ToastSeverity.Success, ToastDuration.Extended);

        Assert.NotNull(raised);
        Assert.Null(raised.Title);
        Assert.False(raised.HasActions);
        Assert.Equal(ToastDuration.Extended, raised.Duration);
    }

    [Fact]
    public void Show_WithoutExplicitTier_UsesTheSeverityTier()
    {
        var service = new ToastService();
        var raised = new List<ToastNotification>();
        service.ToastRaised += notification => raised.Add(notification);

        service.ShowSuccess("saved");
        service.ShowWarning("careful");
        service.ShowError("broke");
        service.Show("hint");
        service.Show("saved", ToastSeverity.Success);

        Assert.Equal(
            new[]
            {
                ToastDuration.Brief,
                ToastDuration.Medium,
                ToastDuration.Extended,
                ToastDuration.Brief,
                ToastDuration.Brief
            },
            raised.Select(notification => notification.Duration));
    }

    [Fact]
    public void Show_WithExplicitTier_OverridesTheSeverityTier()
    {
        var service = new ToastService();
        ToastNotification? raised = null;
        service.ToastRaised += notification => raised = notification;

        service.Show("saved", ToastSeverity.Error, ToastDuration.Brief);

        Assert.NotNull(raised);
        Assert.Equal(ToastDuration.Brief, raised.Duration);
    }

    [Fact]
    public void Show_WithTierInOptions_OverridesTheSeverityTier()
    {
        var service = new ToastService();
        ToastNotification? raised = null;
        service.ToastRaised += notification => raised = notification;

        service.Show(new ToastOptions
        {
            Message = "Install failed",
            Severity = ToastSeverity.Error,
            Duration = ToastDuration.Brief
        });

        Assert.NotNull(raised);
        Assert.Equal(ToastDuration.Brief, raised.Duration);
    }

    [Theory]
    [InlineData(ToastDuration.Brief, 4)]
    [InlineData(ToastDuration.Medium, 6)]
    [InlineData(ToastDuration.Extended, 8)]
    public void DurationTiers_ResolveToDistinctFixedLengths(ToastDuration duration, int expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), ToastDurations.Resolve(duration));
    }

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
