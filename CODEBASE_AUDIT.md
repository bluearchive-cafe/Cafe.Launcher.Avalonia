# 仓库审计报告（当前状态）

- 审计日期：2026-09-09
- 审计对象：`dd06253` + 工作树修复（`main`）
- 模式：`focused` 网络子系统专项（同日完成三项发现的修复）；上一审计 `fca9bc0`（release，beta.8）；全量基线 `cffbd4d`
- 历史报告：`.repository-audit/history/2026-09-09-network-audit.md`（本状态来源）、`2026-09-09-release-audit-beta.8.md`、`2026-09-09-full-audit-r2.md`

## 当前结论

**网络子系统专项审计完成，三项发现已全部修复并通过守卫测试：0 Critical / 0 High / 0 Medium / 0 新增 open。** 网络架构整体健康（集中连接池与代理租约、每跳重定向复验、有界重试、Range 续传 + CRC64、64 MB JSON 守卫）。`dd06253`（HTTP/2 默认开启设置）经审查为健康。

修复证据（本机实跑）：网络回归集 **202/202 通过**（含 13 条新增守卫测试）；全量单元测试 **1545 通过 / 0 失败 / 2 跳过**（跳过项为 Windows 上既有的符号链接守卫）。实时验证：官方 CDN 配置接口返回 200，主/备 CDN 均为纯主机名。**台账三项 AUD-NET 已标记 resolved（`resolved_commit = d342641`）。**

修复摘要：

1. **AUD-NET-001**：新增 `Services/ResponseBodyReader`（默认 60s 空闲读预算），接入 `FileDownloadService` 下载循环、`ImageCacheService` 图片读取与 `RemoteHttpRequestService.DeserializeJsonAsync` 流式复制；停滞转为 `HttpRequestException`，自然落入既有换源重试 + Range 续传。
2. **AUD-NET-002**：`RemoteHttpRequestService.SendAsync` 连接标志改为 `IWebProxy? connectionProxy`，逐 URI 判定生效代理（`EgressesThroughProxy`：`IsBypassed` 或 `GetProxy` 返回原 URI → 直连并保留本地 DNS 私网校验）；`HttpClientLease` 暴露 `ConnectionProxy`，由 `HttpClientFactory` 填充，下载/图片/清单三处调用点改用租约代理而非设置枚举。
3. **AUD-NET-003**：`BuildDownloadUrl` 保留 CDN 域名的 Authority（含显式端口）与路径前缀，不再静默剥离。

## Open 项

| ID | 严重度 | 状态 | 摘要 |
|---|---|---|---|
| AUD-ARCH-003 | Low | deferred | `RemoteContentViewModel` 直接持有 `DispatcherTimer` |
| AUD-MTN-001 | Low | deferred | `RemoteContentViewModel` 拆分 |
| AUD-DEP-002 | Low | accepted-risk | `Shirasagi0012.MaterialColorUtilities` 单维护者风险，已有年度复审与 fork 预案 |
| AUD-TST-001 | Low | deferred / No Action | 真实限速测试使用 `Stopwatch` 下限断言 |

AUD-NET-001 / AUD-NET-002 / AUD-NET-003 已于 2026-09-09 修复（守卫测试齐备），见上文修复摘要。

## Recommended Priorities

1. 合并/发布前运行 `verify.ps1`（本机已过 Debug 全量单元 + 网络集；覆盖率棘轮与 Release 门禁按仓库规则以 verify.ps1 为准）。

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

`fca9bc0..dd06253`：1 提交（`feat(network): 新增默认启用的 HTTP/2 设置`）。本轮已审：工厂 `DefaultRequestVersion=2.0` + `RequestVersionOrLower`、仅影响其后创建的客户端、设置在启动早期应用、`HttpClientFactoryTests`/`LauncherCoreServiceTests`/`LauncherSettingsServiceTests`/`SettingsEditorTests`/`RemoteHttpUrlValidatorTests` 均有覆盖。

## Audit Method and Limitations

网络专项按源码通读全部网络面文件，框架语义（`HttpClient.Timeout` 与 `ResponseHeadersRead` 边界、`IWebProxy.IsBypassed` 直连语义、`SocketsHttpHandler.Dispose` 与在途请求）按 .NET 文档与 dotnet/runtime 核对；用应用自身授权算法实时查询官方 CDN 接口验证数据形态；网络回归集实跑。未复现真实连接停滞（AUD-NET-001 基于源码 + 框架语义，置信 90）；未审网络面之外的领域（沿用同日全量 r2 与 release 审计结论）。
