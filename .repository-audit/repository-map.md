# Repository Map

- 地图版本：13
- 审计日期：2026-09-12（full 全量审计 + 按裁定的落地批次）
- HEAD：`490c2e5`（`main`）；上一份当前态报告（2026-09-11）已归档为 `.repository-audit/history/2026-09-11-full-audit.md`
- 发布状态：最新已发布 tag 为 `v1.1.0-beta.9`（`ec92b351` → `490c2e5`，已推送 origin；Release 于 2026-09-12T01:58:30Z 发布），工作树无未提交的发布资料
- 上次全量审计基线：`66e103a`（2026-09-11）；上次审计提交 `43c17ce`
- 本轮变更集：`43c17ce..490c2e5`（10 提交 / 46 文件 / +2203 −176）

## Project

- 类型：跨平台桌面启动器 / 更新器（Blue Archive 日服）
- 语言与框架：C#、XAML、.NET 10（SDK 10.0.302，global.json `latestFeature`）、Avalonia 12.1.2
- 支持平台：Windows x64（正式）；macOS arm64（实验性，暂不支持启动游戏）、Linux x64（实验性）
- 分发：GitHub Actions 构建自包含 ZIP、macOS app ZIP、tar.gz、deb、AppImage 与 Windows Inno Setup 安装器，发布到源码仓库及独立 Release 仓库

## Structure

- 生产源码：`src/Cafe.Launcher.Avalonia/`（234 个 `.cs`、约 33,077 行；28 个 `.axaml`）
- Feature：`Features/Shell`、`GameOperations`、`Settings`、`SetupWizard`、`Diagnostics`、`ResourcePanel`
- 共享层：`Services/`（含 `Diagnostics/`、`GameRuntime/`、`Auth/`）、`Helpers/`、`Models/`、`Constants/`、`Controls/`、`Converters/`、根 `ViewModels/`
- 组合根：`Composition/ServiceConfiguration.cs`
- 单元测试：`tests/Cafe.Launcher.Avalonia.Tests/`（145 个 `.cs`；1628 过 / 2 跳 / 0 失败 / 1630，含 `AuthorizationHeaderFactoryTests`）
- Headless UI：`tests/Cafe.Launcher.Avalonia.HeadlessTests/`（44 个 `.cs`；176/176，含 7 份黄金基线）
- 原型（不参与发布）：`prototypes/FluentMotionLab`（显式关闭 lock 文件）
- CI：`.github/workflows/build.yml`（windows-latest，**仅 Debug 配置**）
- 发布：`.github/workflows/release.yml`（ubuntu-24.04 + windows-latest installer job，**唯一的 Release 配置测试运行**；release job 生成并随两个发布目标发布 `SHA256SUMS`）、`release.ps1`、`scripts/Build-Distribution.ps1`、`scripts/New-WindowsInstaller.ps1`
- 安装资源：`installer/`（Inno Setup 7.x）
- 架构/决策：`CONTEXT.md`、`docs/design/adr/`（ADR-001…020，全部为 UI/M3/崩溃域）
- 发布素材：`docs/assets/release-banners/`（9 份）、`docs/promo/`（spec 驱动管线）

## Architecture

- MVVM + 垂直 Feature 切片
- Shell 是窗口壳层，允许通过 `ShellPresentationFamily` 向下聚合具体展示 ViewModel（AGENTS.md 明文豁免）
- 非 Shell Feature 不得互相引用具体类型；共享展示契约位于根 `ViewModels/`（`IModalContentViewModel`/`ModalKind`/`ModalEntry`）
- 模态隔离两层机制（AGENTS.md 模态隔离条款）：七个主叠层各自绑定按种类区分的 `ModalHostViewModel.Is*Interactive`；**对话框层有意不设此类闸口**，其输入拦截由 `Grid.dialog-overlay` 的全屏遮罩 + `ZIndex` 次序承担（不可加闸口，除非先让模态栈成为对话框可见性的唯一来源）
- 持久化经原子 JSON 落盘；下载、校验、诊断服务位于共享 Services 层
- 网络面集中：`HttpClientFactory`（池化 handler + 代理租约）+ `RemoteHttpRequestService`（统一手动重定向、每跳复验）+ `RemoteHttpUrlValidator`（DNS 私网校验 + 短 TTL 缓存）；`new HttpClient(` 仅出现在 `HttpClientFactory.cs:78/94/118/126`（工厂自身，池化 handler + 不释放 handler），无绕过工厂的构造
- 外部协议兼容：清单/配置字段序、`vc` 与 CRC-64、请求签名对齐官方启动器（依据目前仅存于未跟踪的 `docs/official-launcher-diff-v1.7.2.md`，见 AUD-DOC-003）

## Verification Commands

- Build：`.\build.ps1`
- Test：`.\test.ps1`（可选 `-Configuration Release`）
- Coverage：`.\coverage.ps1`（Debug-only；棘轮基线：手写行 85.85%、分支 92.70%，余量打印于每次运行）
- Full verification：`.\verify.ps1`
- Localization：`.\scripts\Test-LocalizationContract.ps1`
- Distribution：`.\scripts\Build-Distribution.ps1 -Rids win-x64,osx-arm64,linux-x64`
- Windows installer：`.\scripts\New-WindowsInstaller.ps1`

## Key Repository Rules

- `AGENTS.md`、`PROJECT_CONVENTIONS.md`、`CONTEXT.md` 是主要工程契约
- warnings-as-errors + EnforceCodeStyleInBuild、编译绑定、设计 token（禁裸色值/尺寸）、本地化四语言同步且禁裸 key 字面量
- 本地化键数：四份 `LauncherStrings*.resx` 各 554 键，零缺键/零多余/零空值
- NuGet 中央版本管理 + committed lock files；CI `RestoreLockedMode=true`，仅 RID 发布还原显式豁免（lock 只固定无 RID 依赖图）
- CHANGELOG_RELEASE.md 只保留当前单版本并面向安装用户；版本横幅必须随 tag 提交（release.yml 硬门禁，**仅查存在性**）
- 发布提交遵循 Conventional Commits；`release.ps1` 负责版本提交、tag 与 push
- `main` 的实际保护规则只有 deletion + non_fast_forward（无 PR / 状态检查 / 评审要求，见 PROJECT_CONVENTIONS §9）
- 无远程遥测：诊断日志只留本地

## Risk Profile

- Critical：下载完整性、文件系统安全、发布供应链、更新恢复
- High：跨平台行为、测试确定性、UI 线程性能、持久化/迁移、架构边界
- Medium：本地化契约
- Low：一般代码气味
- 信任边界：Yostar API/CDN、Cafe CDN/资源 API、GitHub 更新源、远端清单、崩溃快照、CI/发布凭据、安装器权限边界

## Previous Audit State

- Last full audit commit：`490c2e5`（2026-09-12，本轮）
- 上一次全量审计：`66e103a`（2026-09-11）
- Open Critical / High / Medium：**0 / 0 / 1**（Medium = AUD-CI-008）
- 按裁定已结案：AUD-DEP-009（发布 SHA256SUMS，工作树待提交）、AUD-ARCH-008（签名算法与版本变更守卫，工作树待提交）、AUD-ARCH-005（取 (A)：删除无消费者的对话框层闸口属性，隔离策略写入 AGENTS.md/CONTEXT.md）、AUD-DOC-003（按决定接受：分析文档不入库、不加 gitignore）
- 本轮新增 7 项、重新打开 1 项：AUD-CI-008（Medium）、AUD-REL-008、AUD-MTN-020、AUD-MTN-021、AUD-ARCH-008、AUD-DOC-003（Low）、AUD-DEP-011（Informational）；AUD-TST-005 由 resolved 重新打开（Low）
- 非阻塞：3 deferred（AUD-ARCH-003、AUD-MTN-001、AUD-TST-001）+ 1 accepted-risk（AUD-DEP-002）+ 1 product-decision（AUD-DEP-009）+ 6 项 Informational 开放项（AUD-ARCH-006/007、AUD-MTN-018、AUD-PERF-012、AUD-SEC-008、AUD-DEP-010/011）
- 台账总数：101 项（81 resolved / 14 open / 3 deferred / 2 accepted-risk / 1 product-decision）；15 项此前 `resolved_commit` 为空者已按 `main` 上的实际提交回填；按裁定落地的三项亦已回填（AUD-DEP-009→`81df872`、AUD-ARCH-008→`7b69e80`、AUD-ARCH-005→`acfb3a3`，落于分支 `audit/2026-09-12-full-audit`）
- 本轮新增拆分项：AUD-ARCH-009（「拒绝执行 vs 强杀」缺决策记录）、AUD-DEP-012（代码签名，产品决策）
