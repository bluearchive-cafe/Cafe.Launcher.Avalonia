using System;
using System.Runtime.CompilerServices;
using Cafe.Launcher.Avalonia.Helpers;

namespace Cafe.Launcher.Avalonia.Testing;

/// <summary>
/// 模块初始化器：把退场动画时长清零，让单元/无头测试不必等待真实退场动画。
/// 本文件通过 csproj Link 同时编入两个测试工程，每个程序集各执行一次初始化，
/// 与此前每个工程各持一份文件的行文一致。
/// </summary>
public static class TestAnimationSetup
{
    [ModuleInitializer]
    public static void Initialize()
    {
        AnimationTimings.ExitAnimationDuration = TimeSpan.Zero;
    }
}
