using System;
using System.Text;

namespace Cafe.Launcher.Avalonia.Helpers;

/// <summary>
/// 发布说明进入展示控件的安全边界：剥掉图片与链接目标、仅留文字；
/// GFM 与 GitHub Alerts 的解析交给 MarkView/Markdig。
/// </summary>
internal static class ReleaseNotesMarkdownSanitizer
{
    public static string Sanitize(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return "";
        }

        var result = new StringBuilder(markdown.Length);
        var lineStart = 0;
        char fenceCharacter = '\0';
        var fenceLength = 0;

        while (lineStart < markdown.Length)
        {
            var lineEnd = markdown.IndexOf('\n', lineStart);
            var hasLineFeed = lineEnd >= 0;
            if (!hasLineFeed)
            {
                lineEnd = markdown.Length;
            }

            var line = markdown[lineStart..lineEnd];
            if (TryGetFence(line, out var currentFenceCharacter, out var currentFenceLength, out var fenceTail))
            {
                if (fenceCharacter == '\0')
                {
                    fenceCharacter = currentFenceCharacter;
                    fenceLength = currentFenceLength;
                }
                else if (currentFenceCharacter == fenceCharacter &&
                         currentFenceLength >= fenceLength &&
                         string.IsNullOrWhiteSpace(fenceTail))
                {
                    fenceCharacter = '\0';
                    fenceLength = 0;
                }

                result.Append(line);
            }
            else if (fenceCharacter != '\0')
            {
                result.Append(line);
            }
            else
            {
                result.Append(StripInlineLinkTargets(line));
            }

            if (hasLineFeed)
            {
                result.Append('\n');
            }

            lineStart = lineEnd + 1;
        }

        return result.ToString();
    }

    private static bool TryGetFence(
        string line,
        out char fenceCharacter,
        out int fenceLength,
        out string fenceTail)
    {
        var index = 0;
        while (index < line.Length && index < 3 && line[index] == ' ')
        {
            index++;
        }

        fenceCharacter = index < line.Length ? line[index] : '\0';
        if (fenceCharacter is not ('`' or '~'))
        {
            fenceLength = 0;
            fenceTail = string.Empty;
            return false;
        }

        var end = index;
        while (end < line.Length && line[end] == fenceCharacter)
        {
            end++;
        }

        fenceLength = end - index;
        fenceTail = line[end..];
        return fenceLength >= 3;
    }

    private static string StripInlineLinkTargets(string line)
    {
        var result = new StringBuilder(line.Length);
        for (var index = 0; index < line.Length;)
        {
            if (line[index] == '`')
            {
                var delimiterLength = CountRun(line, index, '`');
                var closingIndex = line.IndexOf(new string('`', delimiterLength), index + delimiterLength, StringComparison.Ordinal);
                if (closingIndex < 0)
                {
                    result.Append(line, index, delimiterLength);
                    index += delimiterLength;
                    continue;
                }

                var codeEnd = closingIndex + delimiterLength;
                result.Append(line, index, codeEnd - index);
                index = codeEnd;
                continue;
            }

            var isImage = line[index] == '!' && index + 1 < line.Length && line[index + 1] == '[';
            var labelStart = isImage ? index + 1 : index;
            if (line[labelStart] == '[' &&
                TryFindBalancedEnd(line, labelStart, '[', ']', out var labelEnd) &&
                TryFindLinkTargetEnd(line, labelEnd + 1, out var targetEnd))
            {
                result.Append(line, labelStart + 1, labelEnd - labelStart - 1);
                index = targetEnd + 1;
                continue;
            }

            result.Append(line[index]);
            index++;
        }

        return result.ToString();
    }

    private static bool TryFindLinkTargetEnd(string text, int start, out int end)
    {
        if (start < text.Length && text[start] == '(')
        {
            return TryFindBalancedEnd(text, start, '(', ')', out end);
        }

        if (start < text.Length && text[start] == '[')
        {
            return TryFindBalancedEnd(text, start, '[', ']', out end);
        }

        end = -1;
        return false;
    }

    private static bool TryFindBalancedEnd(string text, int start, char opening, char closing, out int end)
    {
        var depth = 0;
        for (var index = start; index < text.Length; index++)
        {
            if (text[index] == '\\')
            {
                index++;
                continue;
            }

            if (text[index] == opening)
            {
                depth++;
            }
            else if (text[index] == closing && --depth == 0)
            {
                end = index;
                return true;
            }
        }

        end = -1;
        return false;
    }

    private static int CountRun(string text, int start, char value)
    {
        var end = start;
        while (end < text.Length && text[end] == value)
        {
            end++;
        }

        return end - start;
    }

}
