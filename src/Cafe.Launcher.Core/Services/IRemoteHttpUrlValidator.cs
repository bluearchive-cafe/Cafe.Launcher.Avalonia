using System;
using System.Threading;
using System.Threading.Tasks;

namespace Cafe.Launcher.Core.Services;

/// <summary>
/// 出站 URL 校验的窄接口（SSRF 防护、Fake-IP 判定）。表现层只问「这个地址能不能发」，
/// 不接触 DNS 解析缓存与私有地址规则。
/// </summary>
public interface IRemoteHttpUrlValidator
{
    /// <summary>校验字符串 URL；不合规时抛错。</summary>
    Task<Uri> ValidateAsync(string url, CancellationToken cancellationToken = default);

    /// <summary>校验 <see cref="Uri"/>（直连路径）。</summary>
    Task<Uri> ValidateAsync(Uri uri, CancellationToken cancellationToken = default);

    /// <summary>校验 <see cref="Uri"/>，并说明本次连接是否走代理出口。</summary>
    Task<Uri> ValidateAsync(Uri uri, bool connectionUsesProxy, CancellationToken cancellationToken = default);

    /// <summary>该主机是否由 Fake-IP 模式的代理应答（用于把连接失败改写成针对性提示）。</summary>
    bool IsFakeIpResolution(string host);
}
