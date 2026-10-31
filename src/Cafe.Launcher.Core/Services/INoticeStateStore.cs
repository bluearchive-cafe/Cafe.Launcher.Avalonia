using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Cafe.Launcher.Core.Services;

/// <summary>官方公告已读状态；不可用或损坏的本地状态按空集合降级。</summary>
public interface INoticeStateStore
{
    /// <summary>读取已展示公告的标识。</summary>
    Task<HashSet<string>> ReadShownNoticesAsync(CancellationToken cancellationToken = default);

    /// <summary>串行追加已展示标识，存储失败不阻断启动器操作。</summary>
    Task SaveShownNoticeAsync(string noticeHash, CancellationToken cancellationToken = default);
}
