namespace Cafe.Launcher.Core.Services;

/// <summary>读取操作系统的动画偏好，供表现层解释系统动效模式。</summary>
public interface ISystemAnimationSettingsProvider
{
    /// <summary>返回系统是否启用动画；无法确定时返回 null。</summary>
    bool? GetSystemAnimationsEnabled();
}
