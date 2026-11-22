namespace Cafe.Launcher.Core.Constants;

/// <summary>
/// <c>settings.json</c> 的持久化格式版本。读取入口只接受这里登记过的版本，
/// 未知版本既不归一化为当前版本，也不允许被默认值覆盖。
/// </summary>
public static class SettingsFormatVersions
{
    /// <summary>把全局背景拆到游戏条目、并引入 <c>games</c> 字典的格式。</summary>
    public const int GamesWithBackgrounds = 2;

    /// <summary>当前唯一可写的格式：版本化根 + 稳定游戏身份索引的独立设置。</summary>
    public const int Current = 3;
}
