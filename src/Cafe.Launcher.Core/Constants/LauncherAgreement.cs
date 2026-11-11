using System;

namespace Cafe.Launcher.Core.Constants;

/// <summary>
/// 用户协议与隐私政策的同意版本。版本取自两份文档顶部的「最后更新」日期；
/// 文档内容发生实质变化时递增，已落盘的旧版本会在下次启动时重新征询。
/// </summary>
public static class LauncherAgreement
{
    public const string CurrentVersion = "2026-11-10";

    /// <summary>报告已落盘的同意版本是否为当前版本。</summary>
    public static bool IsCurrent(string? acceptedVersion) =>
        string.Equals(acceptedVersion, CurrentVersion, StringComparison.Ordinal);
}
