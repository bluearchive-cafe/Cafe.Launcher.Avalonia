# 仓库审计报告 — 2026-09-09（focused 专项：网络子系统）

## Audit Metadata

- 日期：2026-09-09
- Commit：`dd06253`（工作树干净）
- 模式：focused（网络子系统专项：HTTP 客户端基础设施、代理、重试/超时、下载与断点续传、清单、图片缓存、自更新、URL 安全校验）
- 上一基线：`fca9bc0`（同日 release 审计）；全量基线 `cffbd4d`（同日 full r2）
- 风险画像：`desktop-launcher`（download_integrity / update_recovery = critical）
- 说明：本报告是对同一日中断的网络专项审计的恢复与收尾；证据全部在本轮重新固化（行号对准 `dd06253`）。

## Executive Summary

**0 Critical / 0 High / 1 Medium / 2 Low（均为本轮新发现，open）。** 网络架构整体健康：集中式连接池与代理租约、每跳重定向复验、有界重试、断点续传 + CRC64 全量校验、64MB JSON 缓冲守卫全部到位。三项发现集中在"时间预算"与"模式退化"两个主题，无资源泄漏或注入类问题。

门禁（本机实跑）：网络回归集 **189/189 通过**（Debug，14 s）。实时验证：官方 API `GET /api/launcher/advanced/game/download/cdn` 返回 `200`，主/备 CDN 均为纯主机名（无路径、无端口）。

Open 项一览：

| ID | 严重度 | 状态 | 摘要 |
|---|---|---|---|
| AUD-NET-001 | Medium | open | 流式响应正文读取无时间预算：`HttpClient.Timeout` 只约束到响应头，CDN/NAT 静默停滞使下载无限挂起且不触发重试 |
| AUD-NET-002 | Low | open | `connectionUsesProxy` 按设置枚举计算：Auto/System 退化为直连时跳过 DNS 私网校验，守卫在默认配置下失效 |
| AUD-NET-003 | Low | open | `BuildDownloadUrl` 丢弃 CDN 域名的路径/端口；当前 API 返回纯主机名，属潜伏陷阱 |

Most important actions：

1. **AUD-NET-001**：给正文读取循环加空闲读预算（每次 `ReadAsync` 链接 CTS `CancelAfter`，或整体正文截止时间），超时抛 `HttpRequestException` 即可自然落入既有重试/续传路径。
2. **AUD-NET-002**：把 `connectionUsesProxy` 从"设置枚举"改为"生效代理"判定（`GetProxy(uri)` 返回非原 URI 且 `!IsBypassed(uri)`），真代理下保留现注释的设计意图。
3. **AUD-NET-003**：`BuildDownloadUrl` 保留域名路径前缀并补守卫测试，把隐式契约显式化。

## Scope & Method

1. 自源码通读网络面全部文件：`HttpClientFactory` / `HttpClientLeaseSource` / `HttpClientLease` / `ProxySettingsService` / `WindowsRegistrySystemProxySettingsProvider` / `RemoteHttpUrlValidator` / `RemoteHttpRequestService` / `RetryPolicy` / `LauncherApiClient` / `RemoteManifestService` / `FileDownloadService` / `PatchUrlGroupService` / `LauncherUpdateService` / `ImageCacheService` / `BestHttpCookieLibraryService` / `GameDownloadService` / `DownloadExecutor` / `DownloadSession` / `DownloadTransferThrottle` / `AuthorizationHeaderFactory`。
2. 框架语义（`HttpClient.Timeout` 与 `ResponseHeadersRead` 的边界、`IWebProxy.IsBypassed` 直连语义、`SocketsHttpHandler.Dispose` 与在途请求）按 .NET 官方文档与 dotnet/runtime 核对。
3. 实时验证：用应用自身的授权算法（`AuthorizationHeaderFactory` 同构签名）查询官方 CDN 配置接口，确认现网数据形态。
4. 网络回归测试集实跑；逐条核对其余候选（重试放大、HTTP/2 新设置、限速、自更新、Cookie 库解析）为健康或可接受。

## Findings

### AUD-NET-001 — 流式响应正文读取无时间预算，连接停滞时下载无限挂起

- Category：network/reliability
- Severity：**Medium**
- Confidence：90（直接源码证据 + 框架语义文档核对；未复现真实停滞）
- Status：open
- Disposition：Fix

**Evidence**

- `RemoteHttpRequestService.cs:35`：重定向跟随的 `SendAsync` 固定使用 `HttpCompletionOption.ResponseHeadersRead`。
- `FileDownloadService.cs:123-135`：下载正文 `ReadAsStreamAsync` + `ReadAsync` 循环只绑定用户取消令牌；`GameDownloadService.cs:222-231` 为下载租约设的 `TimeSpan.FromMinutes(10)` 超时因此**只约束到响应头到达**。
- `ImageCacheService.cs:24,225,244-249`：同一模式（30 s 头部预算，正文读取无限）。
- `LauncherApiClient.cs:202-231`（`GetRemoteManifestAsync`）：清单 JSON 在 `ResponseHeadersRead` 之下由 `DeserializeJsonAsync` 流式读取，仅绑定 `ct`（64 MB 上限防的是体积，不是停滞）。
- 框架语义：`HttpClient.Timeout` 约束的是 `SendAsync` 操作本身；`ResponseHeadersRead` 下返回后正文读取不受其约束（.NET 官方文档/源码语义，已核对）。
- 停滞的正文读取**不抛任何异常**，因此既不会触发 `FileDownloadService` 的 10 次重试循环，也不会触发 `RetryPolicy`（`RetryPolicy.cs:40` 默认只重试 `HttpRequestException`/`TaskCanceledException`）；`SocketsHttpHandler` 默认未启用 TCP keepalive。

**Impact**

CDN 或中间设备（NAT 超时、反代挂起）静默停滞连接时，游戏下载会话在无异常、无进度的情况下无限挂起：进度条冻结，不自动换源重试。用户仍可手动取消，且恢复下载有 Range 续传兜底，故定级 Medium 而非 High。图片/清单路径同根因，影响较轻（图片 30 s 后头部已到，正文停滞挂起后台任务直至重启）。

**Recommendation**

在正文读取循环加**空闲读预算**：为每次 `ReadAsync` 链接 `CancellationTokenSource.CancelAfter(idleBudget)`（读进展即重置），或对整个正文设截止时间；超时统一转成 `HttpRequestException`，即可自然落入既有的换源重试 + Range 续传路径，无需新机制。三处消费方（下载、图片、清单）可共享同一辅助类型。

**Recommendation validation**：Verified（机制与现有重试/续传代码路径直接吻合）

**Suggested guard**

带"响应头后停滞"桩 handler 的单元测试：断言空闲预算内抛出并进入重试，杜绝回归。

### AUD-NET-002 — `connectionUsesProxy` 按设置枚举计算，Auto/System 退化为直连时跳过 DNS 私网校验

- Category：network/security（SSRF 纵深防御弱化）
- Severity：**Low**
- Confidence：85
- Status：open
- Disposition：Fix

**Evidence**

- `RemoteHttpUrlValidator.cs:78-87`：`connectionUsesProxy=true` 时跳过本地 DNS 解析与私网 IP 校验；注释写明设计意图——"经用户配置的代理出网时，本地解析无意义且有害（代理正是为本地 DNS 被污染的网络准备的）"。该语义本身正确，并被 3 条测试固化（`RemoteHttpUrlValidatorTests.cs:127,147,175`）。
- 问题在调用方：三处统一用**设置枚举**计算该标志——`LauncherApiClient.cs:221`、`ImageCacheService.cs:237`、`DownloadExecutor.cs:217` 均为 `proxyMode != ProxyModes.Direct`。
- `ProxySettingsService.cs:35-44`：`Auto` → `WebRequest.GetSystemWebProxy()`；`System` 且注册表快照为空 → 同样 `GetSystemWebProxy()`。当系统未配置代理、或目标被旁路规则（`ProxyOverride`/`<local>`）命中时，`IWebProxy.IsBypassed` 为真/`GetProxy` 返回原 URI，`SocketsHttpHandler` **直连**（.NET 文档明确）。
- `LauncherSettings.cs:12`：默认 `proxyMode = Auto`。

**Impact**

在默认配置（Auto）且无系统代理的机器上，**所有**远端请求实际由本机直连出网，但"主机名解析到私网地址"的 SSRF 校验被整体跳过——守卫声明的不变量（直连必有 DNS 校验）在默认路径上不成立。缓解项使实际风险保持低位：字面私网 IP 仍在代理分支之前被拒（`RemoteHttpUrlValidator.cs:68-76`）；重定向每跳复验（`RemoteHttpRequestService.cs:25-67`）；端口限 80/443；全部为 GET、响应仅被应用消费。攻击前提是远端 API/清单/DNS 已被控制，属纵深防御弱化而非可直达漏洞。

**Recommendation**

把"该连接是否真经代理"下沉为生效判定而非枚举判定：在 `ProxySettingsService`/租约构建处计算 `proxy is not null && proxy.GetProxy(uri) is not null && !proxy.IsBypassed(uri)`（逐 URI），仅当为真才跳过本地 DNS 校验；Auto/System 退化为直连时回落到现有 DNS 校验分支。保留现注释的设计意图：真代理下不因本地 DNS 污染而拒绝请求。

**Recommendation validation**：Verified

**Suggested guard**

组装层测试：Auto 模式 + 空系统代理快照 → 请求走本地 DNS 校验分支（可用注入 `resolveHostAsync` 的校验器断言被调用）。

### AUD-NET-003 — `BuildDownloadUrl` 丢弃 CDN 域名的路径与非默认端口

- Category：network/correctness（远端契约的静默收窄）
- Severity：**Low**
- Confidence：90
- Status：open
- Disposition：Add Guard（修复建议见下）

**Evidence**

- `FileDownloadService.cs:194-208`：`new Uri(domain)` 后只取 `uri.Scheme` 与 `uri.Host` 重组 URL——域名自身的**路径、端口、查询串全部被静默丢弃**，仅 `source` + `filePath` 参与拼接。
- 实时查询（2026-09-09，官方接口返回 `200`）：`primary_cdn = https://launcher-pkg-ba-jp.yo-star.com`、`back_up_cdn = https://launcher-pkg-ba-jp-bk.yo-star.com`——均为纯主机名，**现状无实际影响**。
- `PatchUrlGroupService.cs:68-84` 的 host 重写保留路径，与上述丢路径行为组合后，未来任何带路径前缀的 CDN/镜像域名都会静默错位。
- 现有测试 `GameDownloadServiceTests.cs:125-149` 只覆盖纯主机名域名与空白域名。

**Impact**

两条失效模式：(a) 域名带路径前缀 → 所有文件请求落到对方根路径，表现为全量 404/CRC 失败，静默且难排查；(b) 域名带非默认端口 → `initialUri` 被 URL 校验器拒绝（`RemoteHttpUrlValidator.cs:57-60`），快速失败但会被无差别重试 10 次后才抛出。当前数据形态下均不触发；属于"上游一改就坏"的隐式契约。

**Recommendation**

`BuildDownloadUrl` 改为保留域名路径前缀（`uri.GetLeftPart(UriPartial.Path)` 去尾斜杠后拼接），端口随 `Uri` 自然保留；并补两条守卫测试固化"域名含路径前缀/含端口"的拼接结果。若决定维持"域名必须为纯主机名"的契约，则在拼接处显式校验并抛出带上下文的异常，而非静默剥离。

**Recommendation validation**：Verified

**Suggested guard**

`BuildDownloadUrl_WhenDomainContainsPathPrefix_PreservesPrefix`（或对应抛错断言）+ `..._WhenDomainContainsExplicitPort_...`。

## 核实为健康的面

- **连接池与生命周期**：单例 `SocketsHttpHandler`（直连）+ 代理 handler 按模式缓存、指纹（注册表快照）变化即失效重建（`HttpClientFactory.cs:121-161`）；并发创建竞态以"后到者复用先到者、多余实例即弃"处理（:138-151）。`PooledConnectionLifetime` 15 min。stale handler 的 `Dispose` 只清理空闲连接，不影响在途请求（dotnet/runtime 语义，已核对）。
- **重定向安全**：全部 handler `AllowAutoRedirect=false`，手动跟随、每跳重新过 URL 校验、拒绝 HTTPS→HTTP 降级、上限 5 跳（`RemoteHttpRequestService.cs:14,25-67`）。
- **JSON 响应守卫**：64 MB 双重上限（声明长度 + 流式计数）防错误载荷耗尽内存；解析失败注入 URL/状态/Content-Type/十六进制预览/压缩嗅探，日志可行动（:100-267）。
- **重试全部有界且取消立即传播**：下载每文件 10 次（顺序对齐原版 Electron 启动器，`FileDownloadService.cs:22-25` 注释写明）；清单/信封 3 次（500/1000 ms 退避）；资源面板 3 次。`RetryPolicy.cs:51-54` 保证用户取消不进重试；429/408/5xx/网络异常/超时才重试（`LauncherApiClient.cs:284-299`）。
- **断点续传与完整性**：Range 请求 + `Content-Range` 严格校验（单位/起点/总长，`FileDownloadService.cs:93-119`）；完成后 CRC64 全量校验，失败即弃件重下。
- **限速**：按活跃（非暂停）时间计费，暂停不计入（`DownloadTransferThrottle.cs`）。
- **HTTP/2 设置（`dd06253`，本轮范围内新合入）**：`DefaultRequestVersion=2.0` + `RequestVersionOrLower` 安全回退；仅影响其后创建的客户端（语义即如此，设置在启动早期应用）；带工厂/服务/设置三层测试。默认开启，代理 CONNECT 隧道下 ALPN 端到端照常工作。
- **自更新**：版本检查为缓冲 GET（`ResponseContentRead`，受客户端超时约束），安装包经浏览器跳转，无应用内大文件下载停滞面。
- **授权头**：MD5 签名系官方线协议强制（`AuthorizationHeaderFactory.cs:11-14` 注释写明），服务器按 `time` 字段强制时效；盐值本就随分发包公开，非新增暴露。
- **Cookie 库解析**：本地文件解析有数量上限（10 000）与尾数据校验（`BestHttpCookieLibraryService.cs:21-45`）。

## Testing

- 网络回归集实跑：`HttpClientFactoryTests` / `ProxySettingsServiceTests` / `RetryPolicyTests` / `RemoteHttpUrlValidatorTests` / `FileDownloadServiceTests` / `LauncherApiClientTests` / `RemoteManifestServiceTests` / `LauncherUpdateServiceTests` / `ResourcePanelApiClientTests` / `GameDownloadServiceTests` / `RemoteHttpRequestServiceTests` → **189/189 通过，0 失败 0 跳过**（Debug，`dd06253`，14 s）。
- 与三项发现的关系：AUD-NET-001 无任何"头部后停滞"测试覆盖；AUD-NET-002 的校验器语义被测试固化为预期行为，缺口在调用方标志计算（无组装层测试）；AUD-NET-003 无带路径/端口域名的用例。

## Changes Since Previous Audit

`fca9bc0..dd06253`：1 提交（HTTP/2 设置，见"健康的面"），本轮已审。工作树干净；未触发 `packages.lock.json` 改写。

## 台账更新

- 新增：`AUD-NET-001`（medium/open）、`AUD-NET-002`（low/open）、`AUD-NET-003`（low/open），`first_seen_commit = dd06253`。
- 复核：无既有网络类 open 项需要收敛；`AUD-ARCH-003` / `AUD-MTN-001` / `AUD-DEP-002` / `AUD-TST-001` 维持原状态。
