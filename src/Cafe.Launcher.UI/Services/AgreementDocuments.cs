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

    private static readonly Lazy<string> combined = new(LoadCombined);

    public static string Load() => combined.Value;

    private static string LoadCombined()
    {
        try
        {
            var terms = Read(TermsUri);
            var privacy = Read(PrivacyUri);
            return new StringBuilder(terms.Length + privacy.Length + 8)
                .Append(terms.TrimEnd())
                .Append("\n\n---\n\n")
                .Append(privacy.TrimStart())
                .ToString();
        }
        catch (Exception exception) when (exception is IOException or FileNotFoundException or ArgumentException)
        {
            // 资源缺失时仍要维持门禁可交互；正文由构建期资源保证，运行期失败只留下可读提示。
            return $"# Agreement documents unavailable\n\n{exception.Message}";
        }
    }

    private static string Read(string uri)
    {
        using var stream = AssetLoader.Open(new Uri(uri));
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
