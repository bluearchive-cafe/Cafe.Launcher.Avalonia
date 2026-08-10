using System.Reflection;
using Avalonia.Headless.XUnit;
using Cafe.Launcher.Avalonia.ViewModels;

namespace Cafe.Launcher.Avalonia.HeadlessTests;

public sealed class ToastStackMotionTests
{
    [AvaloniaFact]
    public void CalculateInitialOffset_WhenLayoutMovesDown_ReturnsPreviousVisualPosition()
    {
        var behaviorType = typeof(ToastHostViewModel).Assembly.GetType(
            "Cafe.Launcher.Avalonia.Controls.ToastStackMotion");

        Assert.NotNull(behaviorType);
        var calculateInitialOffset = behaviorType.GetMethod(
            "CalculateInitialOffset",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(calculateInitialOffset);
        var offset = calculateInitialOffset.Invoke(null, [12d, 52d]);

        Assert.Equal(-40d, offset);
    }
}
