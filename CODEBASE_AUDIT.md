# 仓库审计报告（当前状态）

- 审计日期：2026-09-09
- 审计对象：`fca9bc0`（`main` / `origin/main`，审计开始时工作树干净）
- 模式：`release` 增量审计；上一全量基线 `cffbd4d`
- 目标版本：`v1.1.0-beta.8`
- 历史报告：`.repository-audit/history/2026-09-09-release-audit-beta.8.md`

## 当前结论

**适合发布 `v1.1.0-beta.8`。当前没有 Critical、High 或 Medium open finding，也没有尚待产品/架构决策的发布阻塞项。**

版本号、单版本发布说明、横幅文件与目标 tag 一致；HEAD 的本机完整门禁和 GitHub Actions 均通过；Windows 便携包与 Inno Setup 安装器已按目标 tag 实际构建成功；NuGet 漏洞扫描未发现已知漏洞。提交本次审计产物后，可以进入 `release.ps1 1.1.0-beta.8` 的正式打 tag/推送步骤。

## Open 项

没有发布阻塞项。以下 4 项为既有 Low 风险，均不影响本次发布：

| ID | 状态 | 摘要 |
|---|---|---|
| AUD-ARCH-003 | deferred | `RemoteContentViewModel` 直接持有 `DispatcherTimer` |
| AUD-MTN-001 | deferred | `RemoteContentViewModel` 拆分 |
| AUD-DEP-002 | accepted-risk | `Shirasagi0012.MaterialColorUtilities` 单维护者风险，已有年度复审与 fork 预案 |
| AUD-TST-001 | deferred / No Action | 真实限速测试使用 `Stopwatch` 下限断言 |

## 发布证据

| 检查 | 结果 |
|---|---|
| `verify.ps1` | exit 0；Debug/Release 0 警告、0 错误 |
| 单元测试 | 1524 通过、2 跳过、0 失败 |
| Headless 测试 | 167/167 通过 |
| 覆盖率棘轮 | 手写 C# 行 85.14%，分支 92.16%，均高于基线 |
| HEAD CI | run `34351085205` success；测试、覆盖率、win-x64 Release publish 全绿 |
| 版本契约 | `Read-LauncherVersion.ps1 -Tag v1.1.0-beta.8` 通过；应用 `--version` 输出 `1.1.0-beta.8` |
| 发布资料 | CHANGELOG 唯一标题为 `v1.1.0-beta.8`；NOTE/WARNING 块存在；对应 PNG 横幅已提交 |
| Windows 便携包 | `Cafe.Launcher.Avalonia_v1.1.0-beta.8_win-x64.zip` 构建成功 |
| Windows 安装器 | Inno Setup 7.1.0 编译成功，生成 `Cafe.Launcher.Avalonia_v1.1.0-beta.8_setup.exe` |
| 依赖漏洞 | `dotnet list package --vulnerable --include-transitive`：无已知漏洞包 |
| Actions 供应链 | 17/17 `uses:` 均固定到 40 位 commit SHA；权限最小化规则维持 |
| 审计技能自检 | `python .agents/skills/repository-audit/scripts/validate-skill.py` 通过 |

## Changes Since Previous Audit

`cffbd4d..fca9bc0` 共 4 个提交：合并上一轮全量审计的 8 项整改、准备 beta.8 版本与发布说明、增加发布说明契约守卫、提交横幅。生产代码变化已在 `cffbd4d` 后的全量审计整改中逐项核验；本次新增的发布准备改动由本机门禁、HEAD CI、版本读取脚本及真实 Windows 打包共同覆盖。

发布链路相对 `v1.1.0-beta.7` 仅 `build.yml` 增加顶层 `permissions: contents: read`；`release.yml`、打包脚本、安装器脚本、SDK 与依赖清单无漂移。beta.7 的六类资产发布流水线已成功执行，本次 tag 仍将使用同一路径。

## Lifecycle Reconciliation

`AUD-ARCH-001` 已在提交 `5be9610` 通过 AGENTS.md 明确裁决 Shell 是允许向下聚合 Feature ViewModel 的窗口壳层，但台账仍保留 `architecture-decision` 状态。本轮将其改为 `resolved`；这修正了台账与当前报告“无 High open 项”的不一致，不代表新增代码风险。

## Advisory

- `Microsoft.Extensions.DependencyInjection` 有 `10.0.11 → 10.0.12` 补丁更新可用；当前版本无已知漏洞，且依赖更新会要求同步再生 lock 文件，因此不作为 beta.8 发布阻塞项。
- 本机未构建 Linux/macOS 包；这两类产物仍由 tag 触发的 Linux release job 生成。发布链路自 beta.7 成功运行后未发生相关改动，但 tag 后仍应观察 release workflow，确认六类资产全部上传。
- macOS 产物仍未签名/公证，且不支持启动游戏；这些限制已在 README 与发布说明中明确披露。

## Recommended Priorities

1. 提交本次审计产物，使工作树恢复干净（`release.ps1` 默认拒绝 tracked 文件有未提交改动）。
2. 执行 `release.ps1 1.1.0-beta.8`，由脚本创建并推送 tag。
3. 观察 tag 对应的 `release.yml`，确认 build、installer、双仓库发布三个 job 全绿，六类资产齐全且 prerelease 标记正确。
4. 发布后择机升级 `Microsoft.Extensions.DependencyInjection`，并按仓库规则提交再生的 lock 文件。

## Audit Method and Limitations

本轮使用桌面启动器风险画像的 release 模式，沿用同日 `cffbd4d` 全量审计对架构、安全、下载完整性、持久化和测试确定性的结论，重点复核其后变更、版本与发布资料、构建可复现性、供应链、打包和远端 CI。未创建或推送 tag，未触发真实 beta.8 release workflow，未在 Linux/macOS 主机上运行产物，也未验证仓库外 `RELEASE_REPOSITORY_TOKEN` 的实际权限范围。
