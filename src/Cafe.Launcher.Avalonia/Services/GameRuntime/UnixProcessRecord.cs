using System;
using System.Collections.Generic;
using System.Text;

namespace Cafe.Launcher.Avalonia.Services.GameRuntime;

/// <summary>
/// 一次 <c>/proc/&lt;pid&gt;</c> 快照里用于判定「是不是这个游戏」的原始事实（Linux 进程识别
/// 判据设计稿，见 <c>docs/design/linux-process-identification-design-2026-09-22.md</c>）。
/// </summary>
/// <remarks>
/// 与 <see cref="GameProcessNames"/> 并列：Windows 上进程对象被反作弊保护、镜像路径读不到，
/// 判据只能落在名字上；Linux 的 <c>/proc</c> 不保护这些字段，于是可以拿到不截断的 argv、环境
/// 与映射文件，判据从「名字像不像」升级为「归不归这次游戏会话」。本类型只承载事实，不做判定，
/// 判定在 <see cref="UnixGameProcessMatcher"/>。
/// </remarks>
internal sealed record UnixProcessRecord(
    int ProcessId,
    int ParentProcessId,
    string Comm,
    IReadOnlyList<string> Arguments,
    IReadOnlyList<string> MappedFiles,
    IReadOnlyDictionary<string, string> Environment,
    string? ExecutablePath = null);

/// <summary>
/// 把 <c>/proc/&lt;pid&gt;</c> 的原始字节解析成 <see cref="UnixProcessRecord"/> 的各字段。这些格式
/// 由内核 ABI 决定、与具体游戏无关，因此可以先于实机样本定稿；哪些字段参与判定见设计稿 §3 的
/// 信号阶梯（那才是要靠样本收口的策略）。平台读取层（读文件、门控 <see cref="OperatingSystem.IsLinux"/>）
/// 留待接线时补。
/// </summary>
internal static class UnixProcessRecordParser
{
    /// <summary><c>/proc/&lt;pid&gt;/cmdline</c>：NUL 分隔的 argv，末尾通常多一个 NUL。</summary>
    public static IReadOnlyList<string> ParseArguments(ReadOnlySpan<byte> content) =>
        SplitNulTerminated(content);

    /// <summary>
    /// <c>/proc/&lt;pid&gt;/environ</c>：NUL 分隔的 <c>KEY=VALUE</c>。没有 <c>=</c> 的条目与空键忽略；
    /// 同名键后者覆盖前者（内核不写重复键，这条只是让解析结果确定）。
    /// </summary>
    public static IReadOnlyDictionary<string, string> ParseEnvironment(ReadOnlySpan<byte> content)
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in SplitNulTerminated(content))
        {
            var separator = entry.IndexOf('=');
            if (separator > 0)
            {
                entries[entry[..separator]] = entry[(separator + 1)..];
            }
        }

        return entries;
    }

    /// <summary><c>/proc/&lt;pid&gt;/comm</c>：进程名，内核按 15 字符截断，末尾一个换行。</summary>
    public static string ParseComm(ReadOnlySpan<byte> content) =>
        Encoding.UTF8.GetString(content).TrimEnd('\r', '\n');

    /// <summary>
    /// <c>/proc/&lt;pid&gt;/maps</c> 的映射文件名列（去重、保序、去掉 <c>[heap]</c> 这类伪路径）。
    /// 路径里的空格等字符被内核转义成 <c>\040</c> 形式，这里还原。
    /// </summary>
    public static IReadOnlyList<string> ParseMappedFiles(ReadOnlySpan<byte> content)
    {
        var files = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var start = 0;
        for (var index = 0; index <= content.Length; index++)
        {
            if (index < content.Length && content[index] != (byte)'\n')
            {
                continue;
            }

            var path = ExtractMappedPath(content[start..index]);
            if (path.Length > 0 && seen.Add(path))
            {
                files.Add(path);
            }

            start = index + 1;
        }

        return files;
    }

    private static IReadOnlyList<string> SplitNulTerminated(ReadOnlySpan<byte> content)
    {
        if (content.Length == 0)
        {
            return [];
        }

        var entries = new List<string>();
        var start = 0;
        for (var index = 0; index < content.Length; index++)
        {
            if (content[index] != 0)
            {
                continue;
            }

            entries.Add(Encoding.UTF8.GetString(content[start..index]));
            start = index + 1;
        }

        // 末尾的 NUL 只是分隔符，不产生空条目；但内容不以 NUL 结尾时，最后一段仍是有效值。
        if (start < content.Length)
        {
            entries.Add(Encoding.UTF8.GetString(content[start..]));
        }

        return entries;
    }

    /// <summary>
    /// maps 行是 <c>addr perms offset dev inode pathname</c>，pathname 可缺省（匿名映射）。跳过前五个
    /// 空格分隔的字段后，其余就是 pathname（本身可含空格，内核以 <c>\040</c> 转义）。
    /// </summary>
    private static string ExtractMappedPath(ReadOnlySpan<byte> line)
    {
        var index = 0;
        for (var field = 0; field < 5; field++)
        {
            while (index < line.Length && line[index] == (byte)' ')
            {
                index++;
            }

            while (index < line.Length && line[index] != (byte)' ')
            {
                index++;
            }
        }

        while (index < line.Length && line[index] == (byte)' ')
        {
            index++;
        }

        if (index >= line.Length)
        {
            return "";
        }

        var path = Encoding.UTF8.GetString(line[index..]);
        return path.StartsWith('[') ? "" : Unescape(path);
    }

    private static string Unescape(string path)
    {
        if (!path.Contains('\\', StringComparison.Ordinal))
        {
            return path;
        }

        var builder = new StringBuilder(path.Length);
        for (var index = 0; index < path.Length; index++)
        {
            if (path[index] == '\\'
                && index + 3 < path.Length
                && IsOctal(path[index + 1])
                && IsOctal(path[index + 2])
                && IsOctal(path[index + 3]))
            {
                builder.Append((char)(((path[index + 1] - '0') * 64)
                    + ((path[index + 2] - '0') * 8)
                    + (path[index + 3] - '0')));
                index += 3;
            }
            else
            {
                builder.Append(path[index]);
            }
        }

        return builder.ToString();
    }

    private static bool IsOctal(char value) => value is >= '0' and <= '7';
}
