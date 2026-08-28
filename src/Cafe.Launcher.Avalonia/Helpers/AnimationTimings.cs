using System;

namespace Cafe.Launcher.Avalonia.Helpers;

public static class AnimationTimings
{
    /// <summary>
    /// 退场动画窗口，默认对齐 ADR-016 快速档。可写属性是测试接缝：
    /// headless/单元测试以此冻结或拉长退场窗口（见 TestAnimationSetup、MotionVisibilityTests），
    /// 生产代码不得改写。
    /// </summary>
    public static TimeSpan ExitAnimationDuration { get; set; } = MotionTokens.FastDuration;
}
