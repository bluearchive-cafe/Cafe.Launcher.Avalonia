namespace Cafe.Launcher.Core.Services;

/// <summary>
/// 游戏安装路径的规范化面：把用户给的路径收敛到启动器认得的目录，并给出默认值。
/// 盘符/大小写/尾随分隔符等规则留在实现里。
/// </summary>
public interface IGameInstallationPath
{
    /// <summary>当前平台上的默认游戏目录。</summary>
    string GetDefaultGamePath();

    /// <summary>把任意用户输入规范化为标准游戏目录路径。</summary>
    string NormalizeGamePath(string path);
}
