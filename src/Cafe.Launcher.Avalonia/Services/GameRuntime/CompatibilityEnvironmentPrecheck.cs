using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Helpers;

namespace Cafe.Launcher.Avalonia.Services.GameRuntime;

/// <summary>预检发现的严重度。Error 是「会挡住 Wine/Proton」的档，将来可供启动拒绝路径使用。</summary>
internal enum CompatibilityFindingSeverity
{
    Info,
    Warning,
    Error
}

/// <summary>预检发现的具体类别。</summary>
public enum CompatibilityFindingCode
{
    /// <summary>前缀位置不可创建或不可写。</summary>
    PrefixNotWritable,

    /// <summary>前缀所在文件系统不支持符号链接。</summary>
    SymlinksUnsupported,

    /// <summary>前缀所在挂载点带 noexec，兼容层无法执行其中的文件。</summary>
    MountNoExec,

    /// <summary>前缀所在文件系统不区分大小写（只记录，不据此拒绝）。</summary>
    CaseInsensitiveFilesystem
}

internal sealed record CompatibilityFinding(
    CompatibilityFindingCode Code,
    CompatibilityFindingSeverity Severity,
    string Detail);

internal sealed record CompatibilityEnvironmentReport(
    string PrefixPath,
    DateTimeOffset CheckedAt,
    string? Distribution,
    bool? CaseSensitive,
    IReadOnlyList<CompatibilityFinding> Findings);

/// <summary>
/// 兼容前缀的启动前环境预检（P1-D）。阻断级发现是「前缀不可写」「文件系统不支持符号链接」
/// 「挂载点 noexec」；发行版名称与大小写敏感性只记录——计划明确：大小写敏感本身不是不兼容，
/// 也不凭大小写一刀切拒绝 NTFS 上的目录。
/// </summary>
/// <remarks>
/// <para>本片只做记录，不改变启动结果：预检在 <see cref="GameRuntime.LaunchAsync"/> 选中运行器后、
/// 启动之前运行，任何失败都吞掉。报告写到数据根，由诊断导出作为条目携带。</para>
/// <para>前缀路径可能还不存在（首启），因此文件系统探针落在最近的已存在祖先目录上；无法确定时
/// 记 null，不臆断。</para>
/// </remarks>
public sealed class CompatibilityEnvironmentPrecheck
{
    private readonly LauncherDataRoot dataRoot;

    public CompatibilityEnvironmentPrecheck(LauncherDataRoot dataRoot)
    {
        ArgumentNullException.ThrowIfNull(dataRoot);
        this.dataRoot = dataRoot;
    }

    /// <summary>报告文件的绝对路径。</summary>
    public string ReportPath => dataRoot.CompatibilityEnvironmentPath;

    /// <summary>对给定前缀做预检，尽力写出报告并返回；报告写入失败不影响调用方。</summary>
    internal CompatibilityEnvironmentReport Check(string prefixPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefixPath);

        var findings = new List<CompatibilityFinding>();
        var writable = DirectoryWriteProbe.CanCreate(prefixPath);
        if (!writable)
        {
            findings.Add(new CompatibilityFinding(
                CompatibilityFindingCode.PrefixNotWritable,
                CompatibilityFindingSeverity.Error,
                prefixPath));
        }

        var probeDirectory = NearestExistingDirectory(prefixPath);
        // 只有确认可写时才探符号链接：否则探针本身因权限失败，会把「不可写」误报成
        // 「不支持符号链接」，同一次预检里给出两条互相矛盾的建议。
        if (writable && probeDirectory is not null && SupportsSymlinks(probeDirectory) == false)
        {
            findings.Add(new CompatibilityFinding(
                CompatibilityFindingCode.SymlinksUnsupported,
                CompatibilityFindingSeverity.Error,
                probeDirectory));
        }

        var mounts = TryReadText("/proc/mounts");
        if (mounts is not null && IsNoExecMount(mounts, prefixPath))
        {
            findings.Add(new CompatibilityFinding(
                CompatibilityFindingCode.MountNoExec,
                CompatibilityFindingSeverity.Error,
                prefixPath));
        }

        var caseSensitive = probeDirectory is null ? null : IsCaseSensitive(probeDirectory);
        if (caseSensitive == false)
        {
            findings.Add(new CompatibilityFinding(
                CompatibilityFindingCode.CaseInsensitiveFilesystem,
                CompatibilityFindingSeverity.Info,
                probeDirectory!));
        }

        var report = new CompatibilityEnvironmentReport(
            prefixPath,
            DateTimeOffset.Now,
            ParseDistroName(TryReadText("/etc/os-release")),
            caseSensitive,
            findings);
        TryWriteReport(report);
        return report;
    }

    /// <summary>最近的已存在目录；路径上先撞到普通文件则返回 null。</summary>
    internal static string? NearestExistingDirectory(string path)
    {
        var current = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.Exists(current))
            {
                return current;
            }

            if (File.Exists(current))
            {
                return null;
            }

            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || string.Equals(parent, current, StringComparison.Ordinal))
            {
                return null;
            }

            current = parent;
        }

        return null;
    }

    /// <summary>在给定目录里创建并删除一个符号链接；不支持时返回 false，无法确定时返回 null。</summary>
    internal static bool? SupportsSymlinks(string directory)
    {
        var target = Path.Combine(directory, "cafe-symlink-probe-" + Guid.NewGuid().ToString("N"));
        var link = target + ".link";
        try
        {
            File.WriteAllText(target, "");
            File.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException or NotSupportedException)
        {
            return false;
        }
        finally
        {
            TryDelete(link);
            TryDelete(target);
        }
    }

    /// <summary>
    /// 创建一个小写探针文件，再看大写路径能否解析到它：能即文件系统不区分大小写。名字里带随机
    /// 十六进制，取大写后与原串不同，探针因此有效。无法确定时返回 null。
    /// </summary>
    internal static bool? IsCaseSensitive(string directory)
    {
        var name = "cafe-case-probe-" + Guid.NewGuid().ToString("N") + "a";
        var path = Path.Combine(directory, name);
        try
        {
            File.WriteAllText(path, "");
            return !File.Exists(Path.Combine(directory, name.ToUpperInvariant()));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        finally
        {
            TryDelete(path);
        }
    }

    /// <summary>
    /// 路径所在的最具体挂载点是否带 <c>noexec</c>。/proc/mounts 的挂载点可含被转义的空白。
    /// </summary>
    internal static bool IsNoExecMount(string mountsContent, string path)
    {
        var full = Path.GetFullPath(path);
        string? bestMountPoint = null;
        string? bestOptions = null;

        foreach (var line in mountsContent.Split('\n'))
        {
            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 4)
            {
                continue;
            }

            var mountPoint = UnescapeMountField(fields[1]);
            if (!IsUnderOrEqual(full, mountPoint))
            {
                continue;
            }

            if (bestMountPoint is null || mountPoint.Length > bestMountPoint.Length)
            {
                bestMountPoint = mountPoint;
                bestOptions = fields[3];
            }
        }

        return bestOptions is not null
            && bestOptions.Split(',').Contains("noexec", StringComparer.Ordinal);
    }

    /// <summary>从 <c>/etc/os-release</c> 的 <c>PRETTY_NAME</c> 取发行版名，取不到返回 null。</summary>
    internal static string? ParseDistroName(string? osReleaseContent)
    {
        if (string.IsNullOrWhiteSpace(osReleaseContent))
        {
            return null;
        }

        foreach (var line in osReleaseContent.Split('\n'))
        {
            if (!line.StartsWith("PRETTY_NAME=", StringComparison.Ordinal))
            {
                continue;
            }

            var value = line["PRETTY_NAME=".Length..].Trim().Trim('"');
            return value.Length == 0 ? null : value;
        }

        return null;
    }

    private static bool IsUnderOrEqual(string path, string directory)
    {
        var root = Path.TrimEndingDirectorySeparator(directory);
        if (root.Length == 0)
        {
            return true;
        }

        return string.Equals(path, root, StringComparison.Ordinal)
            || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static string UnescapeMountField(string value)
    {
        if (!value.Contains('\\', StringComparison.Ordinal))
        {
            return value;
        }

        var result = value;
        foreach (var (escaped, character) in new[]
        {
            ("\\040", " "),
            ("\\011", "\t"),
            ("\\012", "\n"),
            ("\\134", "\\")
        })
        {
            result = result.Replace(escaped, character, StringComparison.Ordinal);
        }

        return result;
    }

    private static string? TryReadText(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 探针清理尽力而为。
        }
    }

    private void TryWriteReport(CompatibilityEnvironmentReport report)
    {
        try
        {
            var directory = Path.GetDirectoryName(ReportPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var payload = new
            {
                prefixPath = report.PrefixPath,
                checkedAt = report.CheckedAt.ToString("O", CultureInfo.InvariantCulture),
                distribution = report.Distribution,
                caseSensitive = report.CaseSensitive,
                findings = report.Findings.Select(finding => new
                {
                    code = finding.Code.ToString(),
                    severity = finding.Severity.ToString(),
                    detail = finding.Detail
                })
            };
            File.WriteAllText(ReportPath, JsonSerializer.Serialize(payload, JsonDefaults.Indented));
        }
        catch (Exception exception) when (StorageFailure.IsRecoverable(exception))
        {
            // 报告是诊断增强，写不进去不影响启动。
        }
    }
}
