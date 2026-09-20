using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Helpers;

public static class MotionSettingsResolver
{
    public static bool ShouldReduceMotion(string mode, bool? systemAnimationsEnabled) => mode switch
    {
        MotionModes.Full => false,
        MotionModes.Reduced => true,
        MotionModes.System => systemAnimationsEnabled != true,
        _ => true
    };
}
