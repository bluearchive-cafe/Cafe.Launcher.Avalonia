namespace Cafe.Launcher.Core.Helpers;

/// <summary>
/// 出站请求的 User-Agent 唯一构造点。两个形态刻意保留差异：GitHub API 只发
/// <c>CafeLauncher/{version}</c>，图片缓存历史上带 <c>(.NET)</c> 后缀；统一措辞会改变
/// 已上线的请求头，因此只合并拼接，不合并后缀。
/// </summary>
internal static class LauncherUserAgent
{
    public static string Create(string version) => $"CafeLauncher/{version}";

    public static string CreateWithRuntimeSuffix(string version) =>
        $"CafeLauncher/{version} (.NET)";
}
