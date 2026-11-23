using Cafe.Launcher.Core.Models;

namespace Cafe.Launcher.Core.Services;

/// <summary>读取 Blue Archive 客户端的 <c>TitleVideoAudioSaveData</c>；实现只读，不写回。</summary>
public interface IGameTitleVideoAudioReader
{
    /// <summary>读取指定路径；缺失、不可读或损坏以稳定状态代码返回。</summary>
    TitleVideoAudioReadResult Read(string titleVideoAudioPath);
}
