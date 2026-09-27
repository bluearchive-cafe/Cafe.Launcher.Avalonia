using Cafe.Launcher.UI.Models;
using Cafe.Launcher.Core.Models;

namespace Cafe.Launcher.UI.Helpers;

internal static class MotionSettingsResolver
{
    public static bool ShouldReduceMotion(string mode, bool? systemAnimationsEnabled) => mode switch
    {
        MotionModes.Full => false,
        MotionModes.Reduced => true,
        MotionModes.System => systemAnimationsEnabled != true,
        _ => true
    };
}
