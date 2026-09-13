# Repository Map

- 地图版本：14（全量重审刷新）
- 审计日期：2026-09-13（full 全量重审；按用户指令忽略既有审计报告，从零建立地图/画像/台账）
- HEAD：`5f56f8e`（`main`）；最新已发布 tag `v1.1.0-beta.9`
- 旧地图（v13，2026-09-12，HEAD 490c2e5）与其 101 项台账已归档：`history/2026-09-12-full-audit.md`、`history/2026-09-12-findings-ledger.json`

## Project

- 类型：跨平台桌面游戏启动器 / 更新器（Blue Archive 日服第三方启动器）
- 语言与框架：C# / XAML、.NET 10（SDK 10.0.302，global.json `latestFeature`）、Avalonia 12.1.2、CommunityToolkit.Mvvm 8.4.2、Serilog 4.4.0（异步文件 sink）、System.Text.Json
- 支持平台：Windows x64（正式）；macOS arm64（实验性，暂不支持启动游戏）、Linux x64（实验性）
- 分发：GitHub Actions 构建自包含 ZIP / Inno Setup 安装器 / macOS app ZIP / tar.gz / deb / AppImage，发布到源仓库与独立 Release 仓库，随附 `SHA256SUMS`

## Structure

### Production source

`src/Cafe.Launcher.Avalonia/`（219 个 `.cs` ≈ 32,798 行 + 28 个 `.axaml`）

- `Composition/ServiceConfiguration.cs`（147 行）— 唯一 DI 组合根，全部 AddSingleton，构造函数注入
- `Features/`：Shell（窗口壳层，sanctioned 聚合例外）、GameOperations、Settings、SetupWizard、Diagnostics、ResourcePanel
- `Services/`：网络（LauncherApiClient、RemoteHttpRequestService、RemoteHttpUrlValidator、HttpClientFactory、ProxySettingsService）、下载（FileDownloadService、Crc64Service）、清单/安装状态（RemoteManifestService、ManifestValidationService、LocalInstallationStateStore）、设置（LauncherSettingsService）、自更新（LauncherUpdateService）、诊断（Diagnostics/LocalDiagnostics、UnifiedLogger）、GameRuntime/、Auth/
- 根 `ViewModels/`：窗口级 VM + 模态契约（IModalContentViewModel/ModalKind/ModalEntry）
- `Views/`：MainWindow（663 行 code-behind）+ 覆盖层 + `MainWindow.Styles.axaml`（Z 序/遮罩）
- 横切：`Helpers/GamePathValidator.cs`（路径规范化 + 根边界 + reparse point 拒绝）、`AtomicJsonFileStore.cs`、`JsonDefaults.cs`

### Tests

- `tests/Cafe.Launcher.Avalonia.Tests/` — xUnit v3（1151 Fact + 105 Theory；157 个文件）
- `tests/Cafe.Launcher.Avalonia.HeadlessTests/` — Avalonia.Headless.XUnit + Skia 黄金截图（141 AvaloniaFact + 12 AvaloniaTheory；7 份基线）
- 共享替身 `tests/TestDoubles/`、`TestAnimationSetup.cs`、`TestUserDataIsolation.cs` 以 Compile-Link 编入两个测试程序集
- 覆盖率棘轮基线：手写行 85.85%、分支 92.70%（coverage.ps1）
- 原型 `prototypes/`（FluentMotionLab 等）不参与发布、显式关闭 lock

### Build / CI

- `.github/workflows/build.yml` — windows-latest：本地化契约 → Debug 全量测试 → 覆盖率门禁 → win-x64 发布；动作按 commit SHA 钉住；`ContinuousIntegrationBuild` + `RestoreLockedMode` 显式设置（RID 还原显式豁免并在注释中说明 lock 单 RID 上下文限制）
- `.github/workflows/release.yml` — tag 触发：ubuntu-24.04 构建三 RID 分发包（AppImage 工具 SHA256 钉住、deb/AppImage 冒烟测试）→ windows-latest 以 **Release 配置**重跑全部测试并构建 Inno 安装器（7.1.0，attestation 校验）→ 双仓库发布 + `SHA256SUMS`（数量硬校验）+ 横幅存在性硬校验
- SDK 双固定：global.json 10.0.302 + setup-dotnet 显式版本

### Packaging / installer

- `scripts/Build-Distribution.ps1`、`New-WindowsInstaller.ps1`、`New-ThirdPartyNotices.ps1`、`New-ReleaseChangelog.ps1`、`Test-LocalizationContract.ps1`、`Generate-*.ps1`（Designer/Keys 再生）
- `installer/Cafe.Launcher.Avalonia.iss` — 机器级安装（PrivilegesRequired=admin）

### Documentation / ADRs

- `AGENTS.md`（代理工作流权威）、`PROJECT_CONVENTIONS.md`（§12 工具链表受 InstallerContractTests 守护）、`CONTEXT.md`（领域语言 + P2 设计决策 + 模态交互权裁定）、`UBIQUITOUS_LANGUAGE.md`、`docs/design/adr/ADR-001..020`、`PRIVACY.md`、`THIRD-PARTY-NOTICES.md`（上次与依赖版本同提交再生：63000e1）
- 归档：`.repository-audit/history/2026-09-11-official-launcher-diff-v1.7.2.md`（436 行官方启动器 v1.7.2 协议/行为对比分析，2026-09-13 按裁定自 `docs/` 移入归档；文件仍为未跟踪状态）

## Architecture

- MVVM + 垂直 Feature 切片；Shell 为其他功能之上的壳层（2026-09 裁定，`ShellPresentationFamily` 聚合 13 个展示协作者）
- 非 Shell 功能互不引用具体类型（本次全量 `using` 扫描证实：越界仅 Shell 3 文件 = sanctioned 例外）；跨功能经 `Services/` 窄抽象（`IGameOperationActivity` → DebugViewModel 消费，组合根绑定 GameOperationsViewModel 实现）
- 模态隔离两层机制与 AGENTS.md 2026-09-12 裁定逐字一致：七个主叠层绑定 `ModalHostViewModel.Is*Interactive`；对话框层有意无闸口（`DialogsViewModel.Is*Visible` + `Grid.dialog-overlay` 遮罩 + ZIndex 200/500 次序承担输入拦截）；`ShellLifecycle.SyncModal` 以可见性同源注册模态栈
- 网络面集中：HttpClientFactory（池化 SocketsHttpHandler + 代理租约）→ RemoteHttpRequestService（手动重定向 ≤5、每跳复验、HTTPS→HTTP 降级阻断）→ RemoteHttpUrlValidator（scheme/端口/ userinfo/localhost/私网 DNS 全拒绝 + 30s 正缓存）
- 持久化全部原子落盘（temp + File.Move）；checkpoint 失效安全（版本/基线/分组不匹配即清）

## Verification Commands

- Build：`.\build.ps1`
- Test：`.\test.ps1`（两项目；`-UpdateGolden` 再生黄金基线）
- Coverage：`.\coverage.ps1`（line/branch ≥50% + 基线棘轮 85.85%/92.70%）
- Full verification：`.\verify.ps1`
- Localization：`.\scripts\Test-LocalizationContract.ps1`
- Distribution：`.\scripts\Build-Distribution.ps1 -Rids win-x64,osx-arm64,linux-x64`
- Windows installer：`.\scripts\New-WindowsInstaller.ps1`

## Key Repository Rules

1. 非 Shell 功能不得引用彼此具体类型；跨功能走 `Services/` 窄抽象
2. 功能 VM 居 `Features/*/`；根 `ViewModels/` 仅窗口级 VM 与模态契约；对话框层禁止追加交互闸口（除非先让模态注册成为可见性唯一来源）
3. UI 字符串四语言 resx 同步 + `LocalizationKeys` 常量（禁裸 key）；契约脚本 + CI 步骤守护
4. `settings.json` 字段向后兼容（默认值 + NormalizeSettings + DeepClone 反射棘轮测试）
5. 日志唯一入口 `LocalDiagnostics`；禁记录 Authorization/salt/cookie；零远程遥测
6. warnings-as-errors + EnforceCodeStyleInBuild；XAML 禁裸色号/裸尺寸（UiStyleContractTests）
7. 依赖集中声明 + lock 提交；升级须同步 §12 表（测试守护）与 THIRD-PARTY-NOTICES（无守护，靠流程）
8. `main` 机制保护仅 deletion + non_fast_forward；PR/绿灯为团队约定（PROJECT_CONVENTIONS §9）

## Risk Profile

desktop-launcher 基线按仓库证据调整：

- critical：下载完整性、文件系统安全、发布供应链、更新恢复
- high：跨平台行为（CI 仅 Windows 跑测试 — 本次主要缺口）、测试确定性、UI 线程性能、持久化/迁移、架构边界
- medium：本地化契约（554 键 × 4 语言，契约脚本守护）
- low：一般代码气味
- 信任边界：Yostar API/CDN、Cafe CDN/资源 API、GitHub 更新源、远端清单、崩溃快照、CI/发布凭据（RELEASE_REPOSITORY_TOKEN）、安装器权限边界

## Previous Audit State

- 本次为「忽略既有报告」全量重审（用户指令）；旧报告与台账归档见文件头
- 新台账从零建立：`findings.json`（2026-09-13 @ 5f56f8e，17 项编号发现：0 Critical / 0 High / 6 Medium / 10 Low / 1 Informational）
