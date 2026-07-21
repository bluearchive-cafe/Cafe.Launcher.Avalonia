using System;
using System.Runtime.CompilerServices;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.HeadlessTests;

public static class TestAnimationSetup
{
    [ModuleInitializer]
    public static void Initialize()
    {
        AnimationTimings.ExitAnimationDuration = TimeSpan.Zero;
    }
}
