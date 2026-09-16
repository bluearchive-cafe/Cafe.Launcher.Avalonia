using System;

namespace Cafe.Launcher.Avalonia.Models;

/// <summary>
/// 一种提示条严重度的呈现属性：图标与主题色资源键。
/// </summary>
/// <remarks>
/// <para>这两个属性此前各写成一个 <c>switch</c>——图标在 <see cref="ToastNotification"/> 里，
/// 颜色键在 <c>ToastSeverityToBrushConverter</c> 里，两者分处不同文件夹且都带 <c>_</c> 兜底分支，
/// 于是加一档严重度时编译器不提醒，漏改一处就得到「图标变了、颜色没变」的半成品。</para>
/// <para>兜底分支刻意抛异常而不是回落到 Info：枚举可以有未命名值，编译器无法为 <c>switch</c>
/// 表达式要求穷尽（CS8524），所以完备性由 <c>ToastSeverityProfileTests</c> 遍历
/// <see cref="Enum.GetValues{T}"/> 来守——新增一档严重度而忘了补这里，该用例立刻红。</para>
/// </remarks>
public readonly record struct ToastSeverityProfile(string IconKind, string BrushResourceKey)
{
    /// <summary>返回该严重度的呈现属性。</summary>
    public static ToastSeverityProfile For(ToastSeverity severity) => severity switch
    {
        ToastSeverity.Info => new("InformationOutline", "Launcher.Color.Info"),
        ToastSeverity.Success => new("CheckCircle", "Launcher.Color.Success"),
        ToastSeverity.Warning => new("AlertOutline", "Launcher.Color.Warning"),
        ToastSeverity.Error => new("AlertCircle", "Launcher.Color.Danger"),
        _ => throw new ArgumentOutOfRangeException(
            nameof(severity),
            severity,
            "Unknown toast severity: it needs an entry in ToastSeverityProfile.For.")
    };
}
