using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Services.Update;

namespace Cafe.Launcher.Core.Services;

/// <summary>
/// Windows 自更新的选择与取回面：判断本次发布能否在应用内更新，以及把安装包取到本地。
/// 应用安装包是另一件事（宿主拉起独立 helper），因此不在这个接口里。
/// </summary>
public interface ILauncherSelfUpdateService
{
    /// <summary>本次发布是否能在应用内自更新（宿主形态与发布资产匹配）。</summary>
    LauncherUpdateInAppAvailability ResolveInAppAvailability(IReadOnlyList<ReleaseFile> files);

    /// <summary>取回并校验安装包，返回可交给 helper 的准备工作。</summary>
    Task<LauncherSelfUpdatePreparation> PrepareAsync(
        IReadOnlyList<ReleaseFile> files,
        string version,
        IProgress<LauncherUpdateProgress>? progress,
        CancellationToken cancellationToken);
}
