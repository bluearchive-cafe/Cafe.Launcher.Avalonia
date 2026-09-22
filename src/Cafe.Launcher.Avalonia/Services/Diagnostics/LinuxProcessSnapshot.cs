using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Cafe.Launcher.Avalonia.Services.GameRuntime;

namespace Cafe.Launcher.Avalonia.Services.Diagnostics;

/// <summary>
/// 诊断导出里的 Linux 进程快照（P0-A 取证与 P0-B 采样共用）：把导出时刻与游戏运行器相关的进程
/// 写成一个有大小上限的文本条目。字段选择对齐
/// <c>docs/design/linux-support-plan-2026-09-22.md</c> §3.1 要回答的问题——身份落在 <c>comm</c>
/// 还是 <c>cmdline</c>、运行器家族与父子关系、运行器相关的环境是否存活到游戏与反作弊进程。
/// </summary>
/// <remarks>
/// <para>只导出与游戏运行器相关的进程，而不是整张进程表：命中判据是「环境里带运行器键
/// （WINEPREFIX / GAMEID / PROTONPATH / UMU_ID / 启动器标记 / STEAM_COMPAT_* / PRESSURE_VESSEL_*）」
/// 或「argv 里带 <c>.exe</c> / <c>.dll</c> 参数」。这样既回答 §3.1，又把可能含敏感参数的无关进程
/// 挡在包外。环境变量同样只导出这组键，不是整份 <c>environ</c>。</para>
/// <para>maps 暂不导出，作为 §6 那批待采样问题之后的增量；本类不做判定，只做采集与格式化。</para>
/// </remarks>
internal static class LinuxProcessSnapshot
{
    /// <summary>归档里的条目名。</summary>
    internal const string EntryName = "linux-process-snapshot.txt";

    /// <summary>输出上限，超过即截断并写明，避免异常大的进程表让导出包失控。</summary>
    internal const int MaxCharacters = 256 * 1024;

    private static readonly string[] EnvironmentKeys =
    [
        "WINEPREFIX",
        "GAMEID",
        "PROTONPATH",
        "UMU_ID",
        UnixGameProcessMatcher.OwnershipMarkerKey
    ];

    private static readonly string[] EnvironmentKeyPrefixes = ["STEAM_COMPAT_", "PRESSURE_VESSEL_"];

    private static readonly char[] PathSeparators = ['\\', '/'];

    /// <summary>采集并格式化当前进程快照。只在 Linux 上调用；读取 <c>/proc</c> 失败会向上抛，由导出器记为跳过。</summary>
    public static string Collect(CancellationToken cancellationToken) =>
        Format(ReadProcesses(cancellationToken));

    /// <summary>
    /// 枚举 <c>/proc</c> 下的进程。argv 为空的（内核线程）跳过以减少噪声；单个进程的字段读不到就
    /// 留空，不影响其余进程。
    /// </summary>
    internal static IReadOnlyList<UnixProcessRecord> ReadProcesses(CancellationToken cancellationToken)
    {
        var records = new List<UnixProcessRecord>();
        foreach (var directory in Directory.EnumerateDirectories("/proc"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!int.TryParse(Path.GetFileName(directory), out var processId))
            {
                continue;
            }

            var arguments = UnixProcessRecordParser.ParseArguments(ReadBytes(Path.Combine(directory, "cmdline")));
            if (arguments.Count == 0)
            {
                continue;
            }

            records.Add(new UnixProcessRecord(
                ProcessId: processId,
                ParentProcessId: ReadParentProcessId(Path.Combine(directory, "stat")),
                Comm: UnixProcessRecordParser.ParseComm(ReadBytes(Path.Combine(directory, "comm"))),
                Arguments: arguments,
                MappedFiles: [],
                Environment: UnixProcessRecordParser.ParseEnvironment(ReadBytes(Path.Combine(directory, "environ"))),
                ExecutablePath: ReadLinkTarget(Path.Combine(directory, "exe"))));
        }

        return records;
    }

    /// <summary>纯格式化：只保留运行器相关进程，按 pid 排序，并在超限处截断。</summary>
    internal static string Format(IEnumerable<UnixProcessRecord> records)
    {
        var relevant = records.Where(IsRelevant).OrderBy(record => record.ProcessId).ToList();

        var builder = new StringBuilder();
        builder.Append("# Cafe Launcher Linux process snapshot\n");
        builder.Append("# collected ")
            .Append(DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture))
            .Append('\n');
        builder.Append("# shows only processes with runner environment or a .exe/.dll argument; processes ")
            .Append(relevant.Count)
            .Append('\n');

        var truncated = false;
        foreach (var record in relevant)
        {
            var block = FormatRecord(record);
            if (builder.Length + block.Length > MaxCharacters)
            {
                truncated = true;
                break;
            }

            builder.Append(block);
        }

        if (truncated)
        {
            builder.Append("# truncated at ").Append(MaxCharacters).Append(" characters\n");
        }

        return builder.ToString();
    }

    private static string FormatRecord(UnixProcessRecord record)
    {
        var builder = new StringBuilder();
        builder.Append("pid=").Append(record.ProcessId)
            .Append(" ppid=").Append(record.ParentProcessId)
            .Append(" comm=").Append(record.Comm)
            .Append('\n');
        if (!string.IsNullOrEmpty(record.ExecutablePath))
        {
            builder.Append("  exe=").Append(record.ExecutablePath).Append('\n');
        }

        builder.Append("  argv=").Append(FormatArguments(record.Arguments)).Append('\n');
        foreach (var pair in RelevantEnvironment(record.Environment))
        {
            builder.Append("  env ").Append(pair.Key).Append('=').Append(pair.Value).Append('\n');
        }

        return builder.ToString();
    }

    private static bool IsRelevant(UnixProcessRecord record) =>
        record.Environment.Keys.Any(IsRelevantEnvironmentKey)
        || record.Arguments.Any(IsPortableExecutableArgument);

    private static IEnumerable<KeyValuePair<string, string>> RelevantEnvironment(
        IReadOnlyDictionary<string, string> environment) =>
        environment
            .Where(pair => IsRelevantEnvironmentKey(pair.Key))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal);

    private static bool IsRelevantEnvironmentKey(string key) =>
        EnvironmentKeys.Contains(key, StringComparer.Ordinal)
        || EnvironmentKeyPrefixes.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal));

    private static bool IsPortableExecutableArgument(string argument)
    {
        var name = BaseName(argument);
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatArguments(IReadOnlyList<string> arguments) =>
        string.Join(' ', arguments.Select(QuoteIfNeeded));

    private static string QuoteIfNeeded(string argument)
    {
        if (argument.Length > 0
            && !argument.Any(char.IsWhiteSpace)
            && !argument.Contains('"', StringComparison.Ordinal))
        {
            return argument;
        }

        return $"\"{argument.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }

    private static string BaseName(string path)
    {
        var cut = path.LastIndexOfAny(PathSeparators);
        return cut >= 0 ? path[(cut + 1)..] : path;
    }

    private static byte[] ReadBytes(string path)
    {
        try
        {
            return File.ReadAllBytes(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string? ReadLinkTarget(string path)
    {
        try
        {
            return new FileInfo(path).LinkTarget;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// <c>/proc/&lt;pid&gt;/stat</c> 的第二个字段是 ppid，但第一个字段是可能含空格与括号的 comm：
    /// 以最后一个 <c>)</c> 为界取其后字段，避免把 comm 里的空格当成字段分隔。
    /// </summary>
    private static int ReadParentProcessId(string statPath)
    {
        string content;
        try
        {
            content = File.ReadAllText(statPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return 0;
        }

        var close = content.LastIndexOf(')');
        if (close < 0 || close + 1 >= content.Length)
        {
            return 0;
        }

        var fields = content[(close + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return fields.Length >= 2 && int.TryParse(fields[1], out var parentProcessId)
            ? parentProcessId
            : 0;
    }
}
