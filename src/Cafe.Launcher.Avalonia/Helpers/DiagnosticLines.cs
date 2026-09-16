using System.Collections.Generic;

namespace Cafe.Launcher.Avalonia.Helpers;

/// <summary>
/// 诊断文本的「<c>Label: value</c>」行装配：值为空（null 或全空白）时整行不出现。
/// </summary>
/// <remarks>
/// 本类型只做这一件事：把「空白等于缺席」这条判据收在一处。此前它在
/// <c>RuntimeProbeResult.Describe</c> 与 <c>GameRuntimeDiagnosticSnapshot.Describe</c> 里各写若干遍，
/// 于是「某一行为什么没出现」要靠读者逐行比对 <c>IsNullOrWhiteSpace</c> 才知道。
/// </remarks>
internal static class DiagnosticLines
{
    /// <summary>
    /// 追加 <c>label: value</c>，<paramref name="value"/> 为空或全空白时整行不加。
    /// </summary>
    /// <remarks>
    /// 刻意不 trim：调用方对空白的要求不同（探测结果要 trim 掉命令输出的首尾空白，运行时快照
    /// 原样保留），由调用方决定传什么。
    /// </remarks>
    public static void Optional(List<string> lines, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            lines.Add($"{label}: {value}");
        }
    }
}
