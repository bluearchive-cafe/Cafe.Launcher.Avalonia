using Cafe.Launcher.Core.Models;

namespace Cafe.Launcher.Core.Services;

/// <summary>
/// 读取 Blue Archive 客户端的设备级选项文件。实现只读：不创建、不迁移，也不写回该文件。
/// </summary>
public interface IGameDeviceOptionReader
{
    /// <summary>
    /// 读取指定 <c>DeviceOption</c> 路径；缺失、不可读、损坏或结构漂移都以稳定状态代码返回，
    /// 不抛出预期内的数据问题。
    /// </summary>
    GameDeviceOptionReadResult Read(string deviceOptionPath);
}
