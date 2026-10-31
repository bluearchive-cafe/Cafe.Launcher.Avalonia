namespace Cafe.Launcher.Core.Services.Diagnostics;

/// <summary>提供诊断导出所需的 GPU 与 Vulkan 摘要，工具和进程调用由 Core 负责。</summary>
public interface IGraphicsInfoProbe
{
    /// <summary>尽力读取图形信息；工具缺失或探测失败时返回 null。</summary>
    GraphicsInfo? Probe();
}
