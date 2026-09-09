# 仓库审计报告（当前状态）

- 审计日期：2026-09-09
- 审计对象：`d342641` 网络专项后续复核 + 工作树修复（`9a24b97`，`main`）
- 模式：`focused` 网络子系统专项（同日两轮：AUD-NET-001/002/003 修复 + 复核新发现四项并全部修复）；上一审计 `fca9bc0`（release，beta.8）；全量基线 `cffbd4d`
- 历史报告：`.repository-audit/history/2026-09-09-network-audit.md`（首轮专项来源）、`2026-09-09-release-audit-beta.8.md`、`2026-09-09-full-audit-r2.md`

## 当前结论

**网络子系统专项审计完成，AUD-NET-001/002/003 已修复；同日复核再发现四项（AUD-NET-004…007）并全部修复，均通过守卫测试：0 Critical / 0 High / 0 Medium / 0 新增 open。** 网络架构整体健康（集中连接池与代理租约、每跳重定向复验、有界重试、Range 续传 + CRC64、64 MB JSON 守卫）。

最新修复证据（本机实跑，`9a24b97`）：`verify.ps1` exit 0——全量单元 **1567 通过 / 2 跳过**，Headless **169/169**，手写代码行覆盖 **85.66%**、分支 **92.74%**（均高于基线与上轮），Release 构建 0 警告 0 错误。**台账 AUD-NET-004…007 已标记 resolved（`resolved_commit = 9a24b97`）。**

首轮专项修复摘要（`d342641`）：

1. **AUD-NET-001**：新增 `Services/ResponseBodyReader`（默认 60s 空闲读预算），接入 `FileDownloadService` 下载循环、`ImageCacheService` 图片读取与 `RemoteHttpRequestService.DeserializeJsonAsync` 流式复制；停滞转为 `HttpRequestException`，自然落入既有换源重试 + Range 续传。
2. **AUD-NET-002**：`RemoteHttpRequestService.SendAsync` 连接标志改为 `IWebProxy? connectionProxy`，逐 URI 判定生效代理（`EgressesThroughProxy`：`IsBypassed` 或 `GetProxy` 返回原 URI → 直连并保留本地 DNS 私网校验）；`HttpClientLease` 暴露 `ConnectionProxy`，由 `HttpClientFactory` 填充，下载/图片/清单三处调用点改用租约代理而非设置枚举。
3. **AUD-NET-003**：`BuildDownloadUrl` 保留 CDN 域名的 Authority（含显式端口）与路径前缀，不再静默剥离。

复核修复摘要（`9a24b97`）：

4. **AUD-NET-004**：`ResolveProxyUrl` 与 `CreateProxyAsync` 构造点将 legacy `socks=` 注册表条目及裸 `socks://` 统一规范化为 `socks5://`——`SocketsHttpHandler` 仅接受 http/https/socks4/4a/5，裸方案首个经代理请求即抛 `NotSupportedException`（.NET 10.0.11 运行时实证）；原守卫测试同步纠正，并新增真连接守卫。
5. **AUD-NET-005**：自更新检查的代理端点超时（非调用方取消的 `TaskCanceledException`）与 HTTP 失败同样降级到 GitHub Releases 兜底；用户取消仍直接传播。
6. **AUD-NET-006**：`ResourcePanelApiClient` 改走 `RemoteHttpRequestService.SendAsync` 统一手动重定向路径（每跳 URL 复验）；裸 `GetAsync` 在 `AllowAutoRedirect=false` 的池化 handler 下任何 3xx 都会硬失败并无差别重试。注释 10s/实际 30s 的文档漂移一并修正。
7. **AUD-NET-007**：API 信封业务码非 200 改抛 `LauncherApiEnvelopeException`（`InvalidOperationException` 子类）并在重试过滤器中排除——服务器明确拒绝不再重试 3 次；协议完整性失败（body/data 为空）维持可重试语义。

## Open 项

| ID | 严重度 | 状态 | 摘要 |
|---|---|---|---|
| AUD-ARCH-003 | Low | deferred | `RemoteContentViewModel` 直接持有 `DispatcherTimer` |
| AUD-MTN-001 | Low | deferred | `RemoteContentViewModel` 拆分 |
| AUD-DEP-002 | Low | accepted-risk | `Shirasagi0012.MaterialColorUtilities` 单维护者风险，已有年度复审与 fork 预案 |
| AUD-TST-001 | Low | deferred / No Action | 真实限速测试使用 `Stopwatch` 下限断言 |

AUD-NET-001…003（`d342641`）与 AUD-NET-004…007（`9a24b97`）均已修复（守卫测试齐备），见上文修复摘要。

## Recommended Priorities

1. 合并/发布前运行 `verify.ps1`（`9a24b97` 上 exit 0：全量单元 + 覆盖率棘轮 + Release 门禁均过）。

## Release Evidence（v1.1.0-beta.8，来自同日 release 审计）

| 检查 | 结果 |
|---|---|
| `verify.ps1` | exit 0；Debug/Release 0 警告、0 错误 |
| 单元测试 | 1524 通过、2 跳过、0 失败 |
| Headless 测试 | 167/167 通过 |
| 覆盖率棘轮 | 手写 C# 行 85.14%，分支 92.16%，均高于基线 |
| HEAD CI | run `34351085205` success |
| Windows 便携包 / 安装器 | 便携 zip 与 Inno Setup 7.1.0 安装器均按目标 tag 构建成功 |
| 依赖漏洞 | `dotnet list package --vulnerable --include-transitive`：无已知漏洞包 |
| Actions 供应链 | 17/17 `uses:` 固定 40 位 SHA；权限最小化维持 |

## Changes Since Previous Audit

`fca9bc0..dd06253`：1 提交（`feat(network): 新增默认启用的 HTTP/2 设置`）。首轮专项已审：工厂 `DefaultRequestVersion=2.0` + `RequestVersionOrLower`、仅影响其后创建的客户端、设置在启动早期应用、`HttpClientFactoryTests`/`LauncherCoreServiceTests`/`LauncherSettingsServiceTests`/`SettingsEditorTests`/`RemoteHttpUrlValidatorTests` 均有覆盖。

`d342641..9a24b97`：1 提交（`fix(network)` 复核四项修复，见上文摘要）。首轮专项后的独立复核（非全量重审）：自源码通读网络面（工厂/代理/校验/重试/下载/API/图片/清单/自更新/资源面板），发现并修复 AUD-NET-004…007；优化面建议（DNS 校验缓存、`ConnectTimeout`/H2 keep-alive ping、启动整体 deadline、手动代理模式等）记录于当轮分析输出，未立案为发现。

## Audit Method and Limitations

网络专项按源码通读全部网络面文件，框架语义（`HttpClient.Timeout` 与 `ResponseHeadersRead` 边界、`IWebProxy.IsBypassed` 直连语义、`SocketsHttpHandler.Dispose` 与在途请求）按 .NET 文档与 dotnet/runtime 核对；用应用自身授权算法实时查询官方 CDN 接口验证数据形态；网络回归集实跑。未复现真实连接停滞（AUD-NET-001 基于源码 + 框架语义，置信 90）。复核轮：AUD-NET-004 的 NotSupportedException 已在 .NET 10.0.11 独立控制台实证（与产品目标框架一致）；socks 修复以 .invalid 代理主机真连接守卫固化。未审网络面之外的领域（沿用同日全量 r2 与 release 审计结论）。
