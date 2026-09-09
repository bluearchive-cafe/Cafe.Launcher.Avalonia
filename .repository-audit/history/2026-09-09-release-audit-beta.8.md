# 2026-09-09 Release 审计报告（v1.1.0-beta.8）

## Audit Metadata

- Date：2026-09-09
- Commit：`fca9bc0c52d5c1dae4ff7da9b99302dc0b52f52d`
- Mode：release（基于 2026-09-09 `cffbd4d` full 基线的增量复核）
- Previous baseline：`cffbd4d`
- Scope：`cffbd4d..fca9bc0` 与 `v1.1.0-beta.7..fca9bc0` 的发布准备、门禁、依赖与打包链路
- Project profile：desktop-launcher

## Executive Summary

**结论：适合发布 `v1.1.0-beta.8`。0 Critical / 0 High / 0 Medium open finding。**

目标版本、CHANGELOG 单版本标题、发布横幅完全一致；本机 `verify.ps1`、HEAD GitHub Actions、NuGet 漏洞扫描、Windows ZIP 与安装器真实构建均通过。当前没有需要在打 tag 前修复的代码、测试、依赖或发布资料问题。

Open findings：Critical 0 / High 0 / Medium 0。既有 4 项 Low deferred/accepted-risk 不阻塞发布。

## Changes Since Previous Audit

`cffbd4d..fca9bc0` 共 4 个提交：

1. `aa298da` 合并同日全量审计的 8 项修复、文档与台账；该批次在合并前后均有全量门禁与 CI 证据。
2. `3bd09df` 将 `VersionPrefix` 递增至 `1.1.0-beta.8`，按用户视角重写单版本发布说明。
3. `fab489f` 新增 CHANGELOG 标题、通知块和内部术语契约守卫。
4. `fca9bc0` 提交 beta.8 发布横幅。

相对上一 tag，发布实现路径只有 `build.yml` 新增顶层 `permissions: contents: read`；`release.yml`、打包/安装脚本、SDK 和依赖版本未改变。

## Findings

没有新增发布 finding。

### Lifecycle reconciliation

- `AUD-ARCH-001` 的 `resolved_commit` 早已记录为 `5be9610`，且 AGENTS.md 已明确 Shell 壳层的受认可聚合例外，但 JSON 状态仍为 `architecture-decision`。本轮将状态改为 `resolved`、`last_verified_commit` 更新为 `fca9bc0`，使台账与当前报告一致。

## Release Evidence

- `verify.ps1`：exit 0；Debug/Release 0 warning、0 error。
- Tests：unit 1524 passed / 2 skipped；headless 167 passed。
- Coverage：handwritten line 85.14%，branch 92.16%，均通过棘轮。
- GitHub Actions：HEAD run `34351085205` success；localization、test、coverage、win-x64 restore/publish 全部成功。
- Version：`Read-LauncherVersion.ps1 -Tag v1.1.0-beta.8` 成功；发布后的 exe `--version` 输出 `1.1.0-beta.8`。
- Assets：CHANGELOG 仅一个 `## v1.1.0-beta.8` 标题，NOTE/WARNING 存在；`cafe-launcher-v1.1.0-beta.8-release-banner.png` 已提交；远端尚无同名 tag。
- Windows packages：ZIP 81,856,778 bytes；SHA-256 `8301C92C73D1D1DBE8EBFAE810435B5B45AF42D9C5E30EA9AEAEEB63C5BE79E9`。Setup 57,862,039 bytes；SHA-256 `DD5475320B40ACB464B0FF565510A6930932EB93D98310392965D49931807F5B`。
- Dependencies：NuGet vulnerability scan 未发现已知漏洞；17/17 GitHub Actions `uses:` 固定 40 位 SHA。
- Skill package：`validate-skill.py` 通过。

## Verified Strengths

- 版本号、tag、发布说明标题、横幅名由脚本/测试多层约束，错误版本会 fail-fast。
- NuGet lock、CI locked mode、固定 SHA actions、AppImage 工具 SHA-256 和 Inno release attestation 继续构成供应链防线。
- beta.7 的完整 release workflow 已成功，且本次未改动该 workflow 与跨平台打包脚本。
- 本轮 Windows 便携包和安装器不是静态推断，而是使用目标 tag 实际产出。

## Advisory

- `Microsoft.Extensions.DependencyInjection 10.0.12` 已可用；当前锁定的 `10.0.11` 无已知漏洞，因此建议发布后按正常依赖升级流程处理。
- 本机没有生成 Linux/macOS 产物；beta.8 的真实跨平台 release job 只能在 tag 推送后运行。应将 workflow 全绿与六类资产完整性作为发布收尾检查。
- 未核验外部 `RELEASE_REPOSITORY_TOKEN` 的实际权限范围。

## Recommended Priorities

1. 提交本次审计产物，使工作树恢复干净。
2. 运行 `release.ps1 1.1.0-beta.8`。
3. 观察 release workflow，确认 build / installer / publish jobs 全绿。
4. 核验源码仓库与 Release 仓库的六类资产、release notes 与 prerelease 标记。

## Audit Method and Limitations

本轮沿用同日全量审计对生产代码关键路径的结论，只深查 `cffbd4d` 后变化及发版面；实际运行仓库完整门禁、版本解析、漏洞扫描、Actions 固定检查、Windows publish/ZIP/installer，并读取 HEAD 的远端 CI 结果。未推 tag、未实际发布、未在 Linux/macOS 上运行产物，且未访问仓库外密钥配置。
