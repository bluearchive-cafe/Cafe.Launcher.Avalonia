using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Cafe.Launcher.Avalonia.Services.GameRuntime;

/// <summary>一个已安装的 Proton 构建：目录名与绝对路径。</summary>
internal sealed record ProtonBuild(string Name, string Path);

/// <summary>
/// 发现本机的 Proton 构建（P1-E）：扫描 Steam/兼容工具常见的 <c>compatibilitytools.d</c> 目录，
/// 只认目录内带 <c>proton</c> 可执行脚本的项。GE-Proton 装在这些目录里，用户不必手抄路径。
/// </summary>
/// <remarks>
/// 设置页的候选建议（真正的「体验增强」）尚未接入；当前消费方是诊断导出，把发现的构建记录进
/// <c>system-info.json</c>，失败原因可对照实际安装。扫描尽力而为：目录不存在或读不到都只当没有。
/// </remarks>
public sealed class ProtonBuildDiscovery
{
    private readonly IReadOnlyList<string> searchDirectories;

    public ProtonBuildDiscovery()
        : this(DefaultSearchDirectories())
    {
    }

    internal ProtonBuildDiscovery(IEnumerable<string> searchDirectories)
    {
        ArgumentNullException.ThrowIfNull(searchDirectories);
        this.searchDirectories = searchDirectories.ToArray();
    }

    /// <summary>扫描并返回去重、按名字排序的构建；没有则为空。</summary>
    internal IReadOnlyList<ProtonBuild> Discover()
    {
        var builds = new Dictionary<string, ProtonBuild>(StringComparer.Ordinal);
        foreach (var directory in searchDirectories)
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            try
            {
                foreach (var candidate in Directory.EnumerateDirectories(directory))
                {
                    if (!LooksLikeProtonBuild(candidate))
                    {
                        continue;
                    }

                    var name = Path.GetFileName(candidate);
                    if (!builds.ContainsKey(name))
                    {
                        builds[name] = new ProtonBuild(name, candidate);
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Best effort: an unreadable directory is treated as having no builds.
            }
        }

        return builds.Values.OrderBy(build => build.Name, StringComparer.Ordinal).ToList();
    }

    private static bool LooksLikeProtonBuild(string directory) =>
        File.Exists(Path.Combine(directory, "proton"));

    private static IReadOnlyList<string> DefaultSearchDirectories()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
        {
            return [];
        }

        return
        [
            Path.Combine(home, ".steam", "root", "compatibilitytools.d"),
            Path.Combine(home, ".steam", "steam", "compatibilitytools.d"),
            Path.Combine(home, ".local", "share", "Steam", "compatibilitytools.d"),
            Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", "data", "Steam", "compatibilitytools.d"),
            Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam", "compatibilitytools.d")
        ];
    }
}
