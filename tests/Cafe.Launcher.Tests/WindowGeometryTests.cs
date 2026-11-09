using Avalonia;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.UI.Features.Shell;

namespace Cafe.Launcher.Tests;

public sealed class WindowGeometryTests
{
    [Fact]
    public void Restore_WhenMemoryDisabled_ReturnsNothing()
    {
        var settings = new LauncherSettings
        {
            RememberWindowPositionAndSize = false,
            WindowPositionX = 120,
            WindowPositionY = 240,
            WindowWidth = 1200,
            WindowHeight = 700
        };

        var restore = WindowGeometryPolicy.Restore(settings, minWidth: 1024, minHeight: 640);

        Assert.Null(restore.Width);
        Assert.Null(restore.Height);
        Assert.Null(restore.Position);
    }

    [Fact]
    public void Restore_ClampsSavedSizeToMinimumAndKeepsPosition()
    {
        var settings = new LauncherSettings
        {
            RememberWindowPositionAndSize = true,
            WindowPositionX = 120,
            WindowPositionY = 240,
            WindowWidth = 800,
            WindowHeight = 500
        };

        var restore = WindowGeometryPolicy.Restore(settings, minWidth: 1024, minHeight: 640);

        Assert.Equal(1024d, restore.Width);
        Assert.Equal(640d, restore.Height);
        Assert.Equal(new PixelPoint(120, 240), restore.Position);
    }

    [Fact]
    public void Restore_DropsNonFiniteAndNonPositiveDimensions()
    {
        var settings = new LauncherSettings
        {
            RememberWindowPositionAndSize = true,
            WindowWidth = double.NaN,
            WindowHeight = -1
        };

        var restore = WindowGeometryPolicy.Restore(settings, minWidth: 1024, minHeight: 640);

        Assert.Null(restore.Width);
        Assert.Null(restore.Height);
        Assert.Null(restore.Position);
    }

    [Fact]
    public void TryCapture_OnlyCapturesTheNormalState()
    {
        var position = new PixelPoint(10, 20);

        Assert.Null(WindowGeometry.TryCapture(isNormalState: false, position, 1200, 700));

        var captured = WindowGeometry.TryCapture(isNormalState: true, position, 1200, 700);
        Assert.True(captured.HasValue);
        Assert.Equal(position, captured.Value.Position);
        Assert.Equal(1200d, captured.Value.Width);
        Assert.Equal(700d, captured.Value.Height);
    }

    [Fact]
    public void ApplyTo_WhenMemoryDisabled_LeavesSettingsUntouched()
    {
        var settings = new LauncherSettings
        {
            RememberWindowPositionAndSize = false,
            WindowPositionX = 1,
            WindowPositionY = 2,
            WindowWidth = 1300,
            WindowHeight = 754
        };

        new WindowGeometry(new PixelPoint(120, 240), 1200, 700).ApplyTo(settings);

        Assert.Equal(1, settings.WindowPositionX);
        Assert.Equal(2, settings.WindowPositionY);
        Assert.Equal(1300d, settings.WindowWidth);
        Assert.Equal(754d, settings.WindowHeight);
    }

    [Fact]
    public void ApplyTo_WhenNormal_WritesEveryValue()
    {
        var settings = new LauncherSettings { RememberWindowPositionAndSize = true };

        new WindowGeometry(new PixelPoint(120, 240), 1200, 700).ApplyTo(settings);

        Assert.Equal(120, settings.WindowPositionX);
        Assert.Equal(240, settings.WindowPositionY);
        Assert.Equal(1200d, settings.WindowWidth);
        Assert.Equal(700d, settings.WindowHeight);
    }

    [Fact]
    public void ApplyTo_KeepsExistingSizeWhenCapturedValueIsInvalid()
    {
        var settings = new LauncherSettings
        {
            RememberWindowPositionAndSize = true,
            WindowWidth = 1300,
            WindowHeight = 754
        };

        new WindowGeometry(new PixelPoint(120, 240), double.NaN, 0).ApplyTo(settings);

        Assert.Equal(120, settings.WindowPositionX);
        Assert.Equal(240, settings.WindowPositionY);
        Assert.Equal(1300d, settings.WindowWidth);
        Assert.Equal(754d, settings.WindowHeight);
    }
}
