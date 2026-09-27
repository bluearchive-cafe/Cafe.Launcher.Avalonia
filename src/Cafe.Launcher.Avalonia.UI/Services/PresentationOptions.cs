namespace Cafe.Launcher.Avalonia.Services;

/// <summary>
/// 表现层的宿主策略开关。宿主解析命令行/环境后在这里交给表现层——容器无法解析基元类型
/// （<c>bool</c>），所以显式包一层记录类型，而不是让 ViewModel 去读宿主 <c>Program</c> 的静态字段。
/// </summary>
/// <param name="ShowHiddenSettings">是否显示隐藏设置分区（宿主从命令行开关解析）。</param>
public sealed record PresentationOptions(bool ShowHiddenSettings = false);
