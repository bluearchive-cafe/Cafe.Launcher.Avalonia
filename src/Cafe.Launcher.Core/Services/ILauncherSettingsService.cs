using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Core.Models;

namespace Cafe.Launcher.Core.Services;

/// <summary>
/// 已保存设置的读取面。表现层只能经这里**读**设置——写入只有 <c>ISavedSettingsWriter</c>
/// 一条路（ADR-024），所以这个接口刻意不含写入口，让「绕开写入协调器」在类型层面就无法表达。
/// </summary>
public interface ILauncherSettingsService
{
    /// <summary>读取已保存设置；文件缺失或损坏时返回默认值。</summary>
    Task<LauncherSettings> ReadAsync(CancellationToken cancellationToken = default);
}
