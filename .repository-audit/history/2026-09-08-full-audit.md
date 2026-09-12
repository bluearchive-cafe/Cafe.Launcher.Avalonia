# 仓库审计（2026-09-08 · full）

- 审计日期：2026-09-08
- HEAD：`6bd2a8f`；上一 full 基线 `1ce42d1`（2026-09-07）
- 模式：full（六域全量，desktop-launcher 风险画像）
- 门禁：`verify.ps1` 本机实跑两轮（见方法说明），次轮 pass

## 当日结论

**无 Critical/High/Medium open 项。** `v1.1.0-beta.7` 已发布（六资产、prerelease、tag=HEAD，发布后零新提交）。新增 2 项 Low 发现并均已当日清理：AUD-CI-005（build.yml 缺 `permissions:` 块）、AUD-MTN-007（PROJECT_CONVENTIONS §12 版本过期）。维持 4 项有意暂缓/接受项。

## 新增发现（本日沉淀，均当日清理）

| ID | 严重度 | 摘要 | 处置 |
|---|---|---|---|
| AUD-CI-005 | Low | `build.yml` 无 `permissions:` 块，push 到 main 时 GITHUB_TOKEN 带默认仓库级（含写）权限；job 只读重构未实际使用写权限 | 已清理：`build.yml` 顶层补 `permissions: contents: read`（与 release.yml 对齐） |
| AUD-MTN-007 | Low | `PROJECT_CONVENTIONS.md` §12 工具链表版本过期（Avalonia 12.1.1 vs 实际 12.1.2；Serilog/Material.Icons 以 `(latest)` 占位），与 `Directory.Packages.props`/THIRD-PARTY-NOTICES 不一致 | 已清理：§12 表钉为 props 实际版本 + 批注 |
| AUD-MTN-008 | Low | Inno Setup 版本文档多处漂移：脚本强制 7.0+，README/AGENTS/CLAUDE 写 6.3+；CLAUDE 称 CI 用 Chocolatey，实际为 issrc 固定版 + verify-asset | 已清理：README/AGENTS/CLAUDE 统一为 7.0+，CLAUDE CI 描述改为实际链路 |
| AUD-PERF-007 | Low | `DeserializeJsonAsync` 响应体无尺寸上限（无界 MemoryStream 缓冲） | 已清理：64MiB 上限（Content-Length 预检 + chunked 流式累计钳制）+ 2 个守卫测试 |

## 暂缓/接受项现状复核

| ID | 上期状态 | 本日复核 |
|---|---|---|
| AUD-ARCH-003 | deferred | 仍成立：`RemoteContentViewModel.cs:27,330` 直接持有 `DispatcherTimer` |
| AUD-MTN-001 | deferred | 部分 superseded：`IModalPresenter` 类型已不存在（grep 零命中），modal 呈现已重构为根 `ViewModels/` 契约；仅剩 RemoteContentViewModel（714 行）拆分。已更新 finding 的 title/note |
| AUD-DEP-002 | accepted-risk | 维持：Shirasagi 0.2.0，调用面收窄至 3 个主题类（MaterialColorMapper / MaterialSchemeGenerator / ThemeColorExtractionService），年度重审 |
| AUD-TST-001 | deferred / No Action | 维持：`GameDownloadServiceTests.cs:1092` `Stopwatch` 下限断言（≥800ms）+ 真实限速，墙钟依赖但由节流设计保证不 flaky |

## 关键领域实证

- 下载完整性：`DownloadExecutor` CRC64 强制校验 + 失配删除重试 + 原子替换 + 复用下载期哈希（AUD-PERF-001 未回归）+ 未触碰文件重哈希自愈。
- 文件系统安全：`GamePathValidator.GetSafePath` 拒绝 `..` 穿越 + 平台大小写 + 逐段拒绝 reparse point（符号链接防护）；2 个 skip 测试为符号链接创建需管理员权限的受控门控。
- 供应链：release.yml 顶层 `contents:read`、发布 job `contents:write`（最小权限）；17 处 action 全部 SHA 固定；`RestoreLockedMode` 锁定生效；THIRD-PARTY-NOTICES 与 props 一致。
- 架构：跨 Feature 具体类型引用仅在组合根 / 窗口壳层 / 入口 / 视图 code-behind；modal 契约在根 `ViewModels/`；无 service locator。
- 本地化/质量门禁：裸 key 零命中；设计 token 零命中；单元 1446 过/2 跳、Headless 164/164；合并覆盖率 行 86.08% / 分支 92.58%（> 棘轮基线）。

## 已验证正面面

全量门禁 pass；beta.7 发布资产完整；i18n + Cafe CDN 修复均有聚焦测试且正确；CI 三重加固为桌面启动器良好基线。

## 审计方法说明

- 子代理：派 3 个并行领域子代理，因账号额度上限（错误码 1310）全部中止、无输出；由主代理按同一清单完成全量深审，未依赖子代理结论。
- verify 首轮在 coverage 阶段因 coverlet 无法插桩（单元 DLL 被并发进程短暂锁定）报 "being used by another process"，单元覆盖 0% 合并后 行 47.53% < 50% → 正确 fail-closed——**环境性瞬时锁定，非仓库缺陷**（CI 同代码绿）。次轮全部通过。
- `verify.ps1` 的 `-r win-x64` restore 重写 lock 文件 RID 段，已 `git restore` 还原。
- 未实跑 `Build-Distribution.ps1`（多 RID 归档）与 `New-WindowsInstaller.ps1`（Inno Setup），其有效性由已成功发布的 beta.7 六资产 + CI 绿灯背书；未执行真实外网更新下载全链路（以源码 + 测试核验）。

---

*历史：`.repository-audit/history/`（本文件为 2026-09-08 full；更早见 2026-09-07 full/release、2026-09-05 delta/release 等）。当前状态见仓库根 `CODEBASE_AUDIT.md`。*
