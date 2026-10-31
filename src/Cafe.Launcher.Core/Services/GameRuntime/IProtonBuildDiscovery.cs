using System.Collections.Generic;

namespace Cafe.Launcher.Core.Services.GameRuntime;

/// <summary>提供本机已安装 Proton 构建；扫描路径与平台实现留在 Core。</summary>
public interface IProtonBuildDiscovery
{
    /// <summary>返回去重、按名称排序的构建；不存在或不可读的目录忽略。</summary>
    IReadOnlyList<ProtonBuild> Discover();
}
