using System;
using System.IO;
using System.Text;
using Avalonia.Platform;

namespace Cafe.Launcher.UI.Services;

/// <summary>
/// 设置向导内嵌的协议正文。两份文档在仓库根目录维护，这里只负责从 Avalonia 资源读取并拼接。
/// </summary>
internal static class AgreementDocuments
{
    private const string TermsUri = "avares://Cafe.Launcher.UI/Assets/Legal/TERMS.md";
    private const string PrivacyUri = "avares://Cafe.Launcher.UI/Assets/Legal/PRIVACY.md";
    private const string LicenseUri = "avares://Cafe.Launcher.UI/Assets/Legal/LICENSE.md";
    private const string ThirdPartyNoticesUri = "avares://Cafe.Launcher.UI/Assets/Legal/THIRD-PARTY-NOTICES.md";

    private static readonly Lazy<Content> combined = new(LoadCombined);
    private static readonly Lazy<Content> license = new(() => LoadSingle(LicenseUri));
    private static readonly Lazy<Content> thirdPartyNotices = new(() => LoadSingle(ThirdPartyNoticesUri));

    /// <summary>
    /// 一次读取的结果。<see cref="IsAvailable"/> 为 false 时正文没有加载出来，调用方
    /// 必须让同意门禁保持锁定——用「标题 + 错误串」的短回退文本会立刻满足阅读判定
    /// （AUD-UI-005）。
    /// </summary>
    internal readonly record struct Content(string Markdown, string? LoadError)
    {
        public bool IsAvailable => LoadError is null;
    }

    public static Content Load() => combined.Value;

    /// <summary>仓库根目录的 LICENSE，关于页「开源许可」入口使用。</summary>
    public static Content License => license.Value;

    /// <summary>仓库根目录的 THIRD-PARTY-NOTICES.md，关于页「第三方声明」入口使用。</summary>
    public static Content ThirdPartyNotices => thirdPartyNotices.Value;

    private static Content LoadSingle(string uri)
    {
        try
        {
            return new Content(Read(uri), null);
        }
        catch (Exception exception) when (IsUnavailable(exception))
        {
            return new Content(string.Empty, exception.Message);
        }
    }

    /// <summary>
    /// 资源基座缺失、文件缺失与 Avalonia 未初始化都只能降级，不能让它抛出到 UI 线程。
    /// <see cref="InvalidOperationException"/> 覆盖无 <c>IAssetLoader</c> 的宿主。
    /// </summary>
    private static bool IsUnavailable(Exception exception) => exception is IOException
        or FileNotFoundException
        or ArgumentException
        or InvalidOperationException;

    private static Content LoadCombined()
    {
        try
        {
            var terms = Read(TermsUri);
            var privacy = Read(PrivacyUri);
            return new Content(
                new StringBuilder(terms.Length + privacy.Length + 8)
                    .Append(terms.TrimEnd())
                    .Append("\n\n---\n\n")
                    .Append(privacy.TrimStart())
                    .ToString(),
                null);
        }
        catch (Exception exception) when (IsUnavailable(exception))
        {
            // 标题由调用方本地化；错误串进入回退正文，便于诊断。
            return new Content(string.Empty, exception.Message);
        }
    }

    private static string Read(string uri)
    {
        using var stream = AssetLoader.Open(new Uri(uri));
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
