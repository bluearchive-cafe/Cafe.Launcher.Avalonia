using System;
using System.Runtime.CompilerServices;
using Cafe.Launcher.Avalonia.Helpers;

namespace Cafe.Launcher.Avalonia.Tests;

public static class TestAnimationSetup
{
    [ModuleInitializer]
    public static void Initialize()
    {
        AnimationTimings.ExitAnimationDuration = TimeSpan.Zero;
    }
}
