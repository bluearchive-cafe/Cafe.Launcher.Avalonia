# 仓库审计报告（当前状态）

- 审计日期：2026-09-10（复核轮 2026-09-09 起）
- 审计对象：`d342641` 网络专项后续复核 + 工作树修复（`9a24b97` 三项 + `e9f823a` 性能一项 + `6a686ce` 优化四项 + `d6fee4e` 残留收尾，`main`）
- 模式：`focused` 网络子系统专项（专项修复 → 复核四项修复 → 复核优化建议逐批落地）；上一审计 `fca9bc0`（release，beta.8）；全量基线 `cffbd4d`
- 历史报告：`.repository-audit/history/2026-09-09-network-audit.md`（首轮专项来源）、`2026-09-09-release-audit-beta.8.md`、`2026-09-09-full-audit-r2.md`

## 当前结论

**网络子系统专项审计完成，AUD-NET-001/002/003 已修复；复核再发现四项（AUD-NET-004…007）并全部修复，复核优化建议第一项亦落地（AUD-PERF-008），均通过守卫测试：0 Critical / 0 High / 0 Medium / 0 新增 open。** 网络架构整体健康（集中连接池与代理租约、每跳重定向复验、有界重试、Range 续传 + CRC64、64 MB JSON 守卫）。

最新修复证据（本机实跑，`e9f823a`）：`verify.ps1` exit 0——全量单元 **1557 通过 / 2 跳过**（Windows 本地口径；CI Linux 权威口径 1553 + 本次 4 条新测试，一致），Headless **167/167**，手写代码行覆盖 **85.40%**、分支 **92.46%**（复算口径，均高于仓库基线），Release 构建 0 警告 0 错误。**台账 AUD-NET-004…007（`9a24b97`）与 AUD-PERF-008（`e9f823a`）均已标记 resolved。**

首轮专项修复摘要（`d342641`）：

1. **AUD-NET-001**：新增 `Services/ResponseBodyReader`（默认 60s 空闲读预算），接入 `FileDownloadService` 下载循环、`ImageCacheService` 图片读取与 `RemoteHttpRequestService.DeserializeJsonAsync` 流式复制；停滞转为 `HttpRequestException`，自然落入既有换源重试 + Range 续传。
2. **AUD-NET-002**：`RemoteHttpRequestService.SendAsync` 连接标志改为 `IWebProxy? connectionProxy`，逐 URI 判定生效代理（`EgressesThroughProxy`：`IsBypassed` 或 `GetProxy` 返回原 URI → 直连并保留本地 DNS 私网校验）；`HttpClientLease` 暴露 `ConnectionProxy`，由 `HttpClientFactory` 填充，下载/图片/清单三处调用点改用租约代理而非设置枚举。
3. **AUD-NET-003**：`BuildDownloadUrl` 保留 CDN 域名的 Authority（含显式端口）与路径前缀，不再静默剥离。

复核修复摘要（`9a24b97`）：

4. **AUD-NET-004**：`ResolveProxyUrl` 与 `CreateProxyAsync` 构造点将 legacy `socks=` 注册表条目及裸 `socks://` 统一规范化为 `socks5://`——`SocketsHttpHandler` 仅接受 http/https/socks4/4a/5，裸方案首个经代理请求即抛 `NotSupportedException`（.NET 10.0.11 运行时实证）；原守卫测试同步纠正，并新增真连接守卫。
5. **AUD-NET-005**：自更新检查的代理端点超时（非调用方取消的 `TaskCanceledException`）与 HTTP 失败同样降级到 GitHub Releases 兜底；用户取消仍直接传播。
6. **AUD-NET-006**：`ResourcePanelApiClient` 改走 `RemoteHttpRequestService.SendAsync` 统一手动重定向路径（每跳 URL 复验）；裸 `GetAsync` 在 `AllowAutoRedirect=false` 的池化 handler 下任何 3xx 都会硬失败并无差别重试。注释 10s/实际 30s 的文档漂移一并修正。
7. **AUD-NET-007**：API 信封业务码非 200 改抛 `LauncherApiEnvelopeException`（`InvalidOperationException` 子类）并在重试过滤器中排除——服务器明确拒绝不再重试 3 次；协议完整性失败（body/data 为空）维持可重试语义。

性能优化落地（`e9f823a`，2026-09-10，台账 AUD-PERF-008）：

8. **DNS 校验结果短 TTL 缓存**：`RemoteHttpUrlValidator` 按主机缓存最近一次全公网成功解析（默认 30s），万级文件下载的解析调用从「与文件数成正比」收敛为「与主机数成正比」；私网/空/抛错结果永不缓存，SSRF 守卫容忍窗被 TTL 界定，瞬时失败保持可重试。守卫测试以注入时钟钉住 30s 边界（命中复用 / 恰达边界重解析 / 私网与失败不入缓存）。

复核优化批次（`6a686ce`，2026-09-10，台账 AUD-PERF-009 / AUD-REL-007 / AUD-PERF-010 / AUD-MTN-014）：

9. **handler 连接默认值**：抽出 `HttpClientFactory.ConfigureConnectionDefaults` 统一直连与代理两处 handler——`ConnectTimeout=15s`（原运行时默认 100s，应用内最短请求超时为自更新 15s）、HTTP/2 `KeepAlivePingDelay=30s`（死多路复用连接在 ping 间隔+超时内暴露，而非等 60s 停滞预算）。
10. **启动远端读取整体预算**：`LauncherCoreService.LoadAsync` 六个并发 API 读取挂 30s linked-CTS 预算（原最坏 ~92s 才降级），到点落入既有降级路径返回 `RemoteUnavailable`；预算 ≥ 单次请求超时，慢网首次尝试不被砍；调用方取消语义不变。
11. **下载进度内存计数**：`RecordFileProgress` 正常路径从每 256KB 块一次磁盘 stat 改为 `Interlocked.Add`；重置路径保留 stat 重采样，`FileDownloadService` 超长临时文件删除分支补发 reset。
12. **image-cache 过期清扫**：构造时后台清扫 `.cache`/`.remote`/遗留 `.tmp` 中 mtime 超 30 天的条目（原先只影响 `.remote` 命中判定、文件永不过期），被逐出内容需要时重新下载。

残留收尾（`d6fee4e`，2026-09-10，台账 AUD-NET-008）：

13. **自更新两端点统一手动重定向路径**：`FetchProxyReleasesAsync` 从裸 `GetAsync`、`FetchGitHubReleasesAsync` 从不带校验的轻量重载，均改走 `RemoteHttpRequestService.SendAsync`（每跳 URL 复验 + `connectionProxy` 逐 URI 直连判定）——全仓库自此无裸 HttpClient 调用。实现注意：`LauncherApiBaseUrl` 尾斜杠 + 路径头斜杠的字符串拼接会产生双斜杠，改用 `Uri(Uri, string)` 相对解析（既有 `RequestPath` 断言捕获）。

未采纳（维持分析记录）：下载重试退避——与原版 Electron 启动器的逐次立即换源语义是显式设计契约（`FileDownloadService` 注释与既有测试固化）；手动代理模式——feature 级（需 UI、四份 resx 本地化与产品决策），不在修复范畴。

## Open 项

| ID | 严重度 | 状态 | 摘要 |
|---|---|---|---|
| AUD-ARCH-003 | Low | deferred | `RemoteContentViewModel` 直接持有 `DispatcherTimer` |
| AUD-MTN-001 | Low | deferred | `RemoteContentViewModel` 拆分 |
| AUD-DEP-002 | Low | accepted-risk | `Shirasagi0012.MaterialColorUtilities` 单维护者风险，已有年度复审与 fork 预案 |
| AUD-TST-001 | Low | deferred / No Action | 真实限速测试使用 `Stopwatch` 下限断言 |

AUD-NET-001…003（`d342641`）、AUD-NET-004…007（`9a24b97`）、AUD-PERF-008（`e9f823a`）与 `6a686ce` 的 AUD-PERF-009 / AUD-REL-007 / AUD-PERF-010 / AUD-MTN-014 均已修复（守卫测试齐备），见上文修复摘要。

## Recommended Priorities

1. 合并/发布前运行 `verify.ps1`（`e9f823a` 上 exit 0：全量单元 + 覆盖率棘轮 + Release 门禁均过）。

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

`d342641..9a24b97`：1 提交（`fix(network)` 复核四项修复，见上文摘要）。首轮专项后的独立复核（非全量重审）：自源码通读网络面（工厂/代理/校验/重试/下载/API/图片/清单/自更新/资源面板），发现并修复 AUD-NET-004…007；优化面建议（DNS 校验缓存、`ConnectTimeout`/H2 keep-alive ping、启动整体 deadline、手动代理模式等）记录于当轮分析输出。

`9a24b97..e9f823a`：1 提交（`perf(network)` DNS 校验缓存，即上述优化建议第一项立案落地为 AUD-PERF-008）。

`e9f823a..6a686ce`：1 提交（`perf(network)` 四项优化落地，见上文第 9–12 项）。仅手动代理模式维持分析输出记录（feature 级）。

## Audit Method and Limitations

网络专项按源码通读全部网络面文件，框架语义（`HttpClient.Timeout` 与 `ResponseHeadersRead` 边界、`IWebProxy.IsBypassed` 直连语义、`SocketsHttpHandler.Dispose` 与在途请求）按 .NET 文档与 dotnet/runtime 核对；用应用自身授权算法实时查询官方 CDN 接口验证数据形态；网络回归集实跑。未复现真实连接停滞（AUD-NET-001 基于源码 + 框架语义，置信 90）。复核轮：AUD-NET-004 的 NotSupportedException 已在 .NET 10.0.11 独立控制台实证（与产品目标框架一致）；socks 修复以 .invalid 代理主机真连接守卫固化。未审网络面之外的领域（沿用同日全量 r2 与 release 审计结论）。
