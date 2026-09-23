using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Cafe.Launcher.Avalonia.Helpers;

/// <summary>
/// 发布说明进入展示控件的唯一边界：GFM 结构适配（警示块、任务列表）与安全过滤
/// （剥掉图片与链接目标，仅留文字）都在这里完成，渲染库只负责排版。
/// </summary>
internal static partial class ReleaseNotesMarkdownSanitizer
{
    public static string Sanitize(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return "";
        }

        var adapted = GitHubAlertRegex().Replace(markdown, static match =>
            "> **" + AlertLabels[match.Groups[1].Value] + "**");
        adapted = TaskListRegex().Replace(adapted, static match =>
            match.Groups[1].Value + (match.Groups[2].Value == " " ? "☐" : "☑"));
        var withoutImages = ImageRegex().Replace(adapted, "$1");
        return LinkRegex().Replace(withoutImages, "$1");
    }

    private static readonly Dictionary<string, string> AlertLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NOTE"] = "ℹ️ Note",
        ["TIP"] = "💡 Tip",
        ["IMPORTANT"] = "❗ Important",
        ["WARNING"] = "⚠️ Warning",
        ["CAUTION"] = "🚫 Caution",
    };

    [GeneratedRegex(@"^>\s*\[!(NOTE|TIP|IMPORTANT|WARNING|CAUTION)\][ \t]*\r?$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex GitHubAlertRegex();

    [GeneratedRegex(@"^([ \t]*(?:[-*+]|\d+[.)])[ \t]+)\[([ xX])\](?=[ \t])", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex TaskListRegex();

    [GeneratedRegex(@"!\[([^\]]*)\]\([^)]*\)", RegexOptions.CultureInvariant)]
    private static partial Regex ImageRegex();

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]*\)", RegexOptions.CultureInvariant)]
    private static partial Regex LinkRegex();
}
