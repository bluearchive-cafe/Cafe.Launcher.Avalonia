namespace Cafe.Launcher.Core.Models;

/// <summary>内置游戏档案与该条目可使用的产品扩展能力；能力仍需与产品服务可用性共同判定。</summary>
public sealed record LauncherGameDefinition(
    YostarGameProfile Profile,
    bool SupportsCafeDownloadSource,
    bool SupportsResourcePanel)
{
    /// <summary>与名称和官方协议 tag 分离的稳定游戏身份。</summary>
    public string GameId => Profile.RuntimeId;
}
