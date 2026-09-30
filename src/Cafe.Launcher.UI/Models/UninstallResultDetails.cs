using System.Collections.Generic;

namespace Cafe.Launcher.UI.Models;

/// <summary>卸载的结构化终态；主动保留的 Prefix 不属于删除失败的残留。</summary>
internal sealed record UninstallResultDetails(IReadOnlyList<string> Leftovers, string? KeptPrefixPath);
