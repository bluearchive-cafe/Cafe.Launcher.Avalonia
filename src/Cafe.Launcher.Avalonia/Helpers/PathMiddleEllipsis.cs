using System.Collections.Generic;
using System.Text;

namespace Cafe.Launcher.Avalonia.Helpers;

/// <summary>
/// 长路径的展示文本：段感知的中间省略（向导复核行与设置页游戏路径行共用）。
/// 保留首段（盘符/根）与末两段，中间各段自前向后按整段贪心装入字符预算，
/// 装不下的段以「…」+ 分隔符收束；首段 + 省略号 + 末两段仍超出预算时末尾
/// 退为一段，再退化到字符级中切。输出保持单行，视图层可再以 TextTrimming
/// 兜底极端超长段（预算按字符估定，宽字形路径可能仍溢出）。
/// </summary>
public static class PathMiddleEllipsis
{
    /// <summary>默认字符预算：按 520 内容列减去图标 chip 与编辑钮后的宽度估定。</summary>
    public const int DefaultMaxCharacters = 48;

    /// <summary>省略号：与文本系统 TextTrimming 的末尾省略号同形（排版符号，不入词表）。</summary>
    public const string Ellipsis = "…";

    /// <summary>StringBuilder 单字符追加用（CA1834）；与 <see cref="Ellipsis"/> 保持同形。</summary>
    private const char EllipsisChar = '…';

    public static string MiddleEllipsize(string? path, int maxCharacters = DefaultMaxCharacters)
    {
        if (string.IsNullOrEmpty(path) || path.Length <= maxCharacters)
        {
            return path ?? string.Empty;
        }

        var segments = SplitSegments(path);
        if (segments.Count > 3)
        {
            // 末两段优先（保留 YostarGames\BlueArchive_JP 这类有语义的尾部）；
            // 装不下退为末一段，再不行才字符级中切。
            var display = Compose(segments, tailSegmentCount: 2, maxCharacters)
                ?? Compose(segments, tailSegmentCount: 1, maxCharacters);
            if (display is not null)
            {
                return display;
            }
        }

        return CharacterMiddle(path, maxCharacters);
    }

    /// <summary>首段 + 中间整段（贪心）+（若舍弃了段）省略号 + 末段；装不下返回 null。</summary>
    private static string? Compose(List<string> segments, int tailSegmentCount, int maxCharacters)
    {
        var tailStart = segments.Count - tailSegmentCount;
        var tailLength = 0;
        for (var i = tailStart; i < segments.Count; i++)
        {
            tailLength += segments[i].Length;
        }

        var head = segments[0];
        if (head.Length + tailLength + Ellipsis.Length > maxCharacters)
        {
            return null;
        }

        var builder = new StringBuilder(head);
        var used = head.Length;
        for (var i = 1; i < tailStart; i++)
        {
            var segmentLength = segments[i].Length;
            if (used + segmentLength + Ellipsis.Length + tailLength > maxCharacters)
            {
                // 省略号顶替被舍弃的中间段，其后补回路径分隔符，保持「段」的可读边界。
                builder.Append(EllipsisChar);
                builder.Append(segments[tailStart - 1][^1]);
                break;
            }

            builder.Append(segments[i]);
            used += segmentLength;
        }

        for (var i = tailStart; i < segments.Count; i++)
        {
            builder.Append(segments[i]);
        }

        return builder.ToString();
    }

    /// <summary>字符级中切：预算减去省略号后前后对半，可能截断段内字符。</summary>
    private static string CharacterMiddle(string path, int maxCharacters)
    {
        if (maxCharacters <= Ellipsis.Length)
        {
            return Ellipsis;
        }

        var keep = maxCharacters - Ellipsis.Length;
        var front = keep / 2;
        var back = keep - front;
        return string.Concat(
            path[..front],
            Ellipsis,
            path[^back..]);
    }

    /// <summary>按分隔符切段，各段保留其尾部分隔符（段序列合起来即原路径）。</summary>
    private static List<string> SplitSegments(string path)
    {
        var segments = new List<string>();
        var start = 0;
        for (var i = 0; i < path.Length; i++)
        {
            if (path[i] is '\\' or '/')
            {
                segments.Add(path[start..(i + 1)]);
                start = i + 1;
            }
        }

        if (start < path.Length)
        {
            segments.Add(path[start..]);
        }

        return segments;
    }
}
