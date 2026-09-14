# Repository Map

- 地图版本：15（full 全量重审刷新）
- 审计日期：2026-09-14（full 六域重审；上一基线 2026-09-13 @ `5f56f8e`）
- HEAD：`6c80951`（`main`；最新已发布 tag `v1.1.0-beta.9`；审计基线 `715fee5` + 同日修复轮 `d1693d5..6c80951`）
- 上一版地图（v14，2026-09-13，HEAD `5f56f8e`）与旧报告已归档：`history/2026-09-13-full-audit.md`

## Project

- 类型：跨平台桌面游戏启动器 / 更新器（Blue Archive 日服第三方启动器）
- 语言与框架：C# / XAML、.NET 10（SDK 10.0.302，global.json `latestFeature`）、Avalonia 12.1.2、CommunityToolkit.Mvvm 8.4.2、Serilog 4.4.0（异步文件 sink）、System.Text.Json
- 支持平台：Windows x64（正式）；macOS arm64（实验性，暂不支持启动游戏）、Linux x64（实验性）
- 分发：GitHub Actions 构建自包含 ZIP / Inno Setup 安装器 / macOS app ZIP / tar.gz / deb / AppImage，发布到源仓库与独立 Release 仓库，随附 `SHA256SUMS`（无签名/来源证明，见 AUD-SEC-005）

## Structure

### Production source

`src/Cafe.Launcher.Avalonia/`（242 个 `.cs` ≈ 33,585 行 + 29 个 `.axaml`）

- `Composition/ServiceConfiguration.cs`（~150 行）— 唯一 DI 组合根，全部 AddSingleton，构造函数注入（一处例外：transport 工厂内 `ISettingsEditor` 运行时解析 lambda，见本审计 advisory）
- `Features/`：Shell（窗口壳层，sanctioned 聚合例外）、GameOperations、Settings、SetupWizard、Diagnostics、ResourcePanel
- `Services/`：网络（`RemoteHttpTransport` 唯一出站模块 + `RemoteHttpRequestService` 手动重定向 + `RemoteHttpUrlValidator` + `HttpClientFactory`/`ProxySettingsService` 代理租约）、下载（`FileDownloadService` .tmp 状态机、`Crc64Service` ArrayPool）、清单/安装状态、设置、自更新、诊断、GameRuntime/、Auth/
- 根 `ViewModels/`：窗口级 VM + 模态契约（14 个类型，全部合规；AUD-ARCH-004 `DesignGalleryViewModel` 已于 6c80951 归入 Features/Diagnostics）
- `Views/`：`MainWindow.axaml.cs`（499 行，动效引擎已抽出）+ `OperationSurfaceAnimator.cs`（190 行，操作表面动效）+ `ResourcePanelOverlay.axaml`（新拆分）+ `MainWindow.Styles.axaml`（Z 序/遮罩）
- 横切：`Helpers/GamePathValidator.cs`（路径规范化 + 根边界 + reparse point 拒绝）、`AtomicJsonFileStore.cs`、`JsonDefaults.cs`

### Tests

- `tests/Cafe.Launcher.Avalonia.Tests/` — xUnit v3（本审计本地实测：1704 通过 / 0 失败 / 2 可见跳过 @ `715fee5` Debug）
- `tests/Cafe.Launcher.Avalonia.HeadlessTests/` — Avalonia.Headless.XUnit + Skia 黄金截图（7 份基线 + 基线契约测试）
- 共享替身 `tests/TestDoubles/`（含 `StubRemoteHttpTransport`）、`TestAnimationSetup.cs`、`TestUserDataIsolation.cs` 以 Compile-Link 编入两个测试程序集
- `Assert.Skip*` 16 处 + 1 处 attribute `Skip=`；平台分支全部可见跳过
- 覆盖率棘轮基线：手写行 85.85%、分支 92.70%（coverage.ps1，CI 强制）

### Build / CI

- `.github/workflows/build.yml` — windows-latest（push/PR）：本地化契约 → Debug 全量测试 → 覆盖率门禁 → win-x64 发布；动作按 SHA 钉住；`RestoreLockedMode`
- `.github/workflows/linux-tests.yml`（新增 @ `5553793`）— ubuntu-24.04 单元套件每周巡检；`build.yml` 另有 `linux-unit-tests` job（@ `c37c44c`）随 push/PR 阻塞执行（AUD-CI-001 收口）
- `.github/workflows/release.yml` — tag 触发：ubuntu-24.04 三 RID 分发包（AppImage 工具 SHA256 钉住）→ windows-latest Release 重测 + Inno 安装器（attestation 校验）→ 双仓库发布 + `SHA256SUMS` 数量硬校验 + 横幅存在性硬校验
- SDK 双固定：global.json 10.0.302 + setup-dotnet 显式版本

### Packaging / installer

- `scripts/Build-Distribution.ps1`、`New-WindowsInstaller.ps1`、`New-ThirdPartyNotices.ps1`、`Test-LocalizationContract.ps1`、`Generate-*.ps1`
- `installer/Cafe.Launcher.Avalonia.iss` — 机器级安装（`PrivilegesRequired=admin`），卸载器有安装归属标记 + NSIS 遗留注册表桥 basename/目录双重校验

### Documentation / ADRs

- `AGENTS.md`、`PROJECT_CONVENTIONS.md`（§12 工具链表受 InstallerContractTests 守护）、`CONTEXT.md`（模态交互权裁定）、`UBIQUITOUS_LANGUAGE.md`、ADR-001..023、`PRIVACY.md`、`THIRD-PARTY-NOTICES.md`（63000e1 与依赖同提交再生）
- `docs/architecture-review-2026-09-13.html`（`afd6dbd` 入库，架构评审候选 01-11 的工作产物）
- 官方启动器 v1.7.2 协议对比分析：按用户 2026-09-12 裁定不入库；行为不变量由 `OfficialHashServiceTests`（字段序）、`AuthorizationHeaderFactoryTests`（签名/版本耦合）、`LauncherConstantsTests` 等测试钉住；工作树文件已于 2026-09-14 前移除（AUD-MAINT-002 以 accepted-risk 结案）

## Architecture

- MVVM + 垂直 Feature 切片；Shell 为其他功能之上的壳层（sanctioned 例外：`ShellLifecycle`/`ShellPresentationFamily`/`ShellStartup` 三文件）
- 非 Shell 功能互不引用具体类型（2026-09-14 全量 `using` 扫描复核：越界仅 sanctioned 三文件）
- 模态注册声明式（ADR-023 → `ModalRegistrar`，19 个 ModalKind ↔ 19 条注册）；模态隔离裁定保持：七个主叠层 `Is*Interactive` 门 + 对话框层纯可见性标志（无闸口）
- 网络面集中：`HttpClientFactory`（池化 + 代理租约，指纹变化换 handler）→ `RemoteHttpTransport`（缓冲/重试/ConfigureRequest 逐跳钩子）→ `RemoteHttpRequestService`（手动重定向 ≤5、每跳复验、降级阻断）
- 持久化全部原子落盘（temp + File.Move）；checkpoint 失效安全

## Verification Commands

- Build：`.\build.ps1`；Test：`.\test.ps1`；Coverage：`.\coverage.ps1`；Full：`.\verify.ps1`
- Localization：`.\scripts\Test-LocalizationContract.ps1`
- Distribution：`.\scripts\Build-Distribution.ps1 -Rids win-x64,osx-arm64,linux-x64`

## Key Repository Rules

1. 非 Shell 功能不得引用彼此具体类型；跨功能走 `Services/` 窄抽象
2. 功能 VM 居 `Features/*/`；根 `ViewModels/` 仅窗口级 VM 与模态契约；对话框层禁止追加交互闸口
3. UI 字符串四语言 resx 同步 + `LocalizationKeys` 常量（禁裸 key）；契约脚本 + CI 步骤守护
4. `settings.json` 字段向后兼容（默认值 + NormalizeSettings + DeepClone 反射棘轮测试）
5. 日志唯一入口 `LocalDiagnostics`；禁记录 Authorization/salt/cookie；零远程遥测
6. warnings-as-errors + EnforceCodeStyleInBuild；XAML 禁裸色号/裸尺寸（UiStyleContractTests）
7. 依赖集中声明 + lock 提交；升级须同步 §12 表（测试守护）与 THIRD-PARTY-NOTICES（无守护，靠流程）
8. `main` 机制保护仅 deletion + non_fast_forward；PR/绿灯为团队约定（PROJECT_CONVENTIONS §9）

## Risk Profile

desktop-launcher 基线按仓库证据调整：

- critical：下载完整性、文件系统安全、发布供应链、更新恢复
- high：跨平台行为（Linux 单元 job 已存在但不阻塞 PR）、测试确定性、UI 线程性能、持久化/迁移、架构边界
- medium：本地化契约（555 键 × 4 语言，契约脚本守护）
- low：一般代码气味
- 信任边界：Yostar API/CDN、Cafe CDN/资源 API、GitHub 更新源、远端清单、崩溃快照、CI/发布凭据（RELEASE_REPOSITORY_TOKEN）、安装器权限边界、用户注册表配置的系统代理

## Previous Audit State

- 本轮为 full 全量重审（用户指令）+ 同日修复轮；上一报告（2026-09-13 full+delta @ `5f56f8e`/`8449d37`）归档于 `history/2026-09-13-full-audit.md`
- 现行台账 `findings.json`（2026-09-14 @ `6c80951`）：开放 6 项（全 Low：PERF-001/004/005 残留、SEC-001/002、ARCH-005，均为决策/待设计项）
- 累计结案 17 项：14 resolved + 3 accepted-risk（MAINT-002、SEC-004、ARCH-007）；修复轮提交清单见 audit-state.json note
