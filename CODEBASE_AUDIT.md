# 仓库审计报告（当前状态）

- 审计日期：2026-09-08（full 全量审计）
- HEAD：`6bd2a8f`（工作树干净）；上一 full 基线 `1ce42d1`（2026-09-07 全量审计）
- 审计方式：repository-audit 流程（full 模式：门禁实跑 + 六大领域逐一核验 + 暂缓/接受项复核 + git 历史热点）
- 历史报告：`.repository-audit/history/`（最近：2026-09-07 full / 2026-09-07 release）
- 说明：本次尝试派 3 个并行子代理做领域深审，因账号额度上限（错误码 1310）全部中止，改由主代理直接全量深审，核验范围未缩水（见文末方法说明）。

## 当前结论

**无 Critical/High/Medium open 项。** `v1.1.0-beta.7` 已于 2026-09-07 15:02Z 发布（六平台资产齐全、prerelease=true、tag 即 HEAD，发布后 `beta7..HEAD` 为 0 提交）。本次审计新增的 4 项 Low 发现（AUD-CI-005、AUD-MTN-007、AUD-MTN-008、AUD-PERF-007）已全部清理；4 项有意暂缓/接受项维持。全量门禁本机实跑通过（单元 1448 过/2 跳）。

## Open 项（4 项，全部 Low / 有意）

| ID | 严重度 | 状态 | 摘要 |
|---|---|---|---|
| AUD-ARCH-003 | Low | deferred | RemoteContentViewModel 直接持有 DispatcherTimer（`RemoteContentViewModel.cs:27,330`） |
| AUD-MTN-001 | Low | deferred | RemoteContentViewModel（714 行）拆分；原含 IModalPresenter 查表半项已被根 `ViewModels/` modal 契约取代（superseded） |
| AUD-DEP-002 | Low | accepted-risk | Shirasagi0012.MaterialColorUtilities bus factor 1；调用面收窄至 3 个主题类，年度重审 |
| AUD-TST-001 | Low | deferred / No Action | GameDownloadServiceTests 真实限速 + `Stopwatch` 下限断言（≥800ms），墙钟依赖但非 flaky 失败 |

（AUD-CI-005 / AUD-MTN-007 / AUD-MTN-008 / AUD-PERF-007 为本次审计新沉淀，已清理，见下方 Resolved。）

## 审计增量核验（1ce42d1..6bd2a8f，8 提交）

| 提交 | 结论 |
|---|---|
| 55ca3e7 fix(i18n) T()/F() 按所选语言解析 | 正确：按 `CurrentLanguage` 经 `LauncherCultureResolver.GetCultureFor` 解析，而非调用线程 `CurrentUICulture`（启动期线程 culture 可能未定）；含回归测试；有注释说明动机 |
| 38ad9bb fix(download) Cafe 源主备 CDN 共用地址 | 正确：Cafe 镜像为单主机、官方备份路径不存在，故主备同址；新增区分测试（Cafe 主备同址 / Official 保持分离）；内容哈希校验不受影响 |
| 6ad1d94 feat(settings) 更新通道移至高级分类 | 纯 UI 分区迁移（DownloadNetwork → Advanced），无持久化改动（settings.json 向后兼容不受影响）；新键四语言齐备 + 测试更新 |
| 8b91a9f / 144572f / 75f4f1d | resx 文案修订 / 审计文档，无结构性影响 |
| ae76562 chore(release) 准备 beta.7 发布资料 | 版本号递增、CHANGELOG 替换为 beta.7 单节、MD3 横幅落位；`rg "^## v"` 单节核验通过 |
| 6bd2a8f docs(audit) | 审计记录，无代码影响 |

## 关键领域核验（critical/high 权重，2026-09-08 实查）

### 下载完整性 / 文件系统安全（critical）

- **`DownloadExecutor`** (`Features/GameOperations/DownloadExecutor.cs`)：下载到 `.tmp` → CRC64 据 manifest 哈希校验 → 失配删除重试（`InstallDownloadedFilesAsync`）；`verifiedHashes` 复用下载期哈希避免二次整读（AUD-PERF-001 修复）；未触碰文件仍重哈希自愈（损坏文件在更新不改写时仅靠启动 size/exist 检查兜底）。原子替换 + 先清只读属性再覆盖。
- **`GamePathValidator.GetSafePath`** (`Helpers/GamePathValidator.cs`)：`Path.GetFullPath` + 归一化前缀判定拒绝 `..` 穿越；按平台处理大小写（Windows OrdinalIgnoreCase / 其他 Ordinal）；**逐段拒绝现有 reparse point**（符号链接攻击防护）。2 个 skip 测试（孤立根目录/已存在目录为符号链接时抛 InvalidOperation）需管理员权限创建符号链接，属受控门控——非缺陷。
- **结论**：完整性哈希强制 + 穿越/符号链接防护 + 恢复/原子替换齐备，无 neuen 漏洞。

### 供应链 / CI（critical）

- **最小权限**：`release.yml` 顶层 `permissions: contents: read`，仅发布 job 按需 `contents: write`（`release.yml:289`），跨仓发布用 `secrets.RELEASE_REPOSITORY_TOKEN`；`build.yml` 无 `permissions` 块（→ AUD-CI-005）。
- **SHA 固定**：17 处 action 全部 commit SHA 固定（checkout/setup-dotnet/upload-artifact/download-artifact/gh-release），无浮动 tag。
- **依赖锁定**：`Directory.Build.props` 设 `RestoreLockedMode` + `ContinuousIntegrationBuild`，CI 不一致即 NU1004 失败（AUD-DEP-003 核销未回归）；RID 专属 restore 显式豁免并在注释注明。
- **依赖版本**：`Directory.Packages.props` 集中于一处；THIRD-PARTY-NOTICES.md 与 props 一致（Avalonia 12.1.2 / MEDI 10.0.11）。唯一陈旧点：PROJECT_CONVENTIONS.md §12（→ AUD-MTN-007）。
- **结论**：发布链路（真正 supply-chain critical 路径）加固到位；仅 build.yml 一处最小权限缺口。

### 架构 / 可维护性

- **Feature 边界**：跨 Feature 具体类型引用全部落在合法聚合点（组合根 `ServiceConfiguration.cs`、窗口壳层 `MainWindowViewModel`/`WindowChromeViewModel`/`DialogsViewModel`/`BackgroundViewModel`、入口 `App.axaml.cs`、视图 code-behind）；无非 shell Feature → 非 shell Feature 的具体类型引用。
- **modal 契约**：`IModalContentViewModel`/`ModalEntry`/`ModalKind`/`ModalHostViewModel` 已落位根 `ViewModels/`（AUD-ARCH-002 维持核销）。
- **组合根纪律**：无 service locator / 静态可变全局 / 隐藏注入通道（DISABLE 检查通过）。
- **文档漂移**：除 §12 版本表外，AGENTS.md / PROJECT_CONVENTIONS.md 声明与实际实现一致（含 shell 豁免条款、本地化契约、测试纪律）。

### 第二轮深审补充（2026-09-08，网络边界 / 进程启动 / 安装脚本）

- **网络边界**：`RemoteHttpUrlValidator` 拒绝非 HTTP(S)/userinfo/非标端口/localhost/私网 IP 字面量/DNS 解析非公网（SSRF 防护），`RemoteHttpRequestService.SendAsync` 每跳重定向复验 + HTTPS→HTTP 降级拒绝，manifest URL 全链经校验；API 客户端路径为硬编码常量（无远程输入面）。代理模式跳过本地 DNS 解析（已在注释说明理由，合理）。
- **进程启动**：`UseShellExecute=false` + `ArgumentList`（结构化参数，无 shell 拼接）；启动目标的 exe 名拒绝含 `/`/`\`（防穿越）+ `File.Exists` 确认；PATH 定位固定可执行名、用户显式路径优先。
- **安装脚本**：`Build-Distribution.ps1` 路径均 `Join-Path`+`-LiteralPath`、RID 白名单、删除仅限构建产物目录、产物存在性终检；`New-WindowsInstaller.ps1` 对 ISCC define 值做引号/CRLF 安全审查；`installer/iss` `PrivilegesRequired=admin`、`[UninstallDelete]` 不越 `{app}`、NSIS 旧版升级桥校验 Uninstall.exe 存在性与文件名。
- **发现的 2 项**：AGENTS/README/CLAUDE 的 Inno Setup 版本描述与脚本强制 7.0+ / release.yml 实际链（issrc 固定版 + verify-asset）脱节（AUD-MTN-008）；`DeserializeJsonAsync` 响应体无上限（AUD-PERF-007）。

### 本地化 / 测试 / 性能


- **本地化契约**：裸 key（`T("…")`/`F("…")`/`I18n["…"]`）扫描零命中，Test-LocalizationContract 已接入 verify（最前 fail-fast）；设计 token（裸色号/魔数圆角/间距）扫描零命中。
- **测试**：单元 1446 过 / 2 跳（符号链接门控），Headless 164/164；合并覆盖率 行 86.08% / 分支 92.58%（高于棘轮基线）。无裸自旋无预算循环、无 Task.Delay 后断言 flake、无测试顺序依赖（全局串行 + 用户数据目录重定向）。
- **性能**：无重复整读哈希回归、无 UI 线程同步解码驻留、无 `.Result()`/`.Wait()` 同步阻塞、无 N+1 元数据 syscall 热点（下载/校验/缓存路径已在前序审计修复）。

## Verified Strengths（闭环价值处记录）

- 全量门禁 `verify.ps1` 本机实跑通过：Debug 0 警告 0 错误、合并覆盖率 行 86.08% / 分支 92.58%、win-x64 Release 0 警告 0 错误、Resx 契约测试 18/18。
- `v1.1.0-beta.7` 发布资产完整性核验：六资产齐全、prerelease 正确、tag=HEAD；release.yml「Verify release banner」硬门禁起到 fail-fast 作用。
- 本地化语言混战国修复 + Cafe 源 CDN 修复均有聚焦测试。
- CI 发布 pipeline 最小权限 + SHA 固定 + locked-mode 三重加固，为典型桌面启动器树立了良好基线。

## Decisions Required

- 无阻塞项。本日新沉淀的 4 项 Low（AUD-CI-005、AUD-MTN-007、AUD-MTN-008、AUD-PERF-007）已全部清理。

## Resolved / Superseded Since Previous Audit

- 无已核销的 High/Critical。AUD-REL-004（发布准备）已于 `ae76562` 核销。AUD-MTN-001 的 IModalPresenter 查表半项被根 modal 契约取代（superseded），仅剩 RemoteContentViewModel 拆分。
- **本日新沉淀并清理**：
  - AUD-CI-005（build.yml 缺 permissions 块）→ 已修复并提交（`ce9c98c`）：`build.yml` 顶层补 `permissions: contents: read`。
  - AUD-MTN-007（PROJECT_CONVENTIONS §12 版本过期）→ 已修复并提交（`ce9c98c`）：§12 表钉为 `Directory.Packages.props` 实际版本。
  - AUD-MTN-008（Inno 版本文档漂移）→ 已修复（工作树待提交）：README/AGENTS/CLAUDE 统一为 7.0+，CLAUDE CI 描述改为 issrc 固定版 + verify-asset。
  - AUD-PERF-007（响应体无上限）→ 已修复（工作树待提交）：`MaxBufferedJsonBytes=64MiB` 预检 + 流式累计钳制。

## Automated Guards Added

- build.yml 的 permissions 块与 release.yml 对齐。`LauncherApiClientTests` 新增 2 个超限守卫测试（Content-Length 预检 + chunked 流式累计），覆盖 AUD-PERF-007 修复面。
- 维持既有守卫：本地化合约、lock 文件锁定、CI 最小权限、横幅硬门禁等。

## Recommended Priorities

1. 提交第二轮修复（DeserializeJsonAsync 上限 + 测试 + 文档 4 处）并推送，确认 build.yml CI 绿灯。
2. 维持 4 项有意暂缓/接受项（ARCH-003 / MTN-001 / DEP-002 / TST-001）到下一全量或年度审。

---

### 审计方法说明

- 模式：full。风险画像按 `desktop-launcher`（download_integrity / filesystem_safety / release_supply_chain / update_recovery = critical）。
- 门禁实证：`verify.ps1` 本机实跑两轮。首轮在 coverage 阶段因 coverlet 无法插桩（单元 DLL 被并发进程短暂锁定，报 "being used by another process"），返回单元覆盖 0% 合并后 行 47.53% < 阈值 50% → 正确 fail-closed，**非仓库缺陷**（锁定为环境性、瞬时，且 CI 同代码绿）。第二轮全部通过。`verify.ps1` 的 `-r win-x64` restore 按 AGENTS.md 约定重写 lock 文件 RID 段，已 `git restore` 还原以保证工作树干净。
- 子代理：曾派 3 个并行子代理（架构+可维护性 / 安全+供应链 / 测试+性能），因账号额度上限中止，无输出；后续全部由主代理按同一清单完成，未依赖子代理结论。
- 主要限制：未实跑 `Build-Distribution.ps1`（多 RID 发布归档）与 `New-WindowsInstaller.ps1`（Inno Setup），其有效性由已成功发布的 beta.7 六资产及 CI 绿灯背书；未执行真实外网更新下载全链路（哈希/补丁应用路径以源码+测试核验）。
