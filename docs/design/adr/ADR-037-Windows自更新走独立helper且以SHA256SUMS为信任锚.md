# ADR-037：Windows 启动器自更新走独立 helper，且以 SHA256SUMS 为信任锚

- 状态：✅ 已接受
- 日期：2026-09-23
- 相关：`Services/LauncherUpdateService.cs`、`Features/Shell/ShellLifecycle.cs`、`src/Cafe.Launcher.Updater/`、`scripts/Build-Distribution.ps1`、`installer/Cafe.Launcher.Avalonia.iss`

## 背景

自更新此前只做「检查 + 弹框」：`LauncherUpdateService.CheckForUpdateAsync` 返回 release 资产列表，用户在对话框里选一个文件，`ShellLifecycle.OnUpdateAvailableConfirmed` 把下载直链交给 `OpenExternalUrl` 交给浏览器。审计据此记录「自更新仅 host 钉住的 GitHub 下载页跳转，从不下载执行自身二进制」。

用户要求把 Windows 上的自更新补齐为应用内执行：下载 → 校验 → 替换 → 重启，且由启动检查与设置页两处入口触发。这推翻了「不执行自身二进制」的既有决定，必须重新立下安全边界。

Windows 的现实约束：

- 安装版由 Inno Setup 安装，`PrivilegesRequired=admin`、`CloseApplications=no`、`RestartApplications=no`，静默安装 `[Run]` 带 `skipifsilent`，因此安装器不会自行拉起应用；且正在运行的应用会命中 `AppMutex` 而让静默安装失败。
- 便携版是一个解压目录，运行中的 exe 被自身进程锁定，任何进程都无法在其运行时覆盖它。
- 发布产物含 `SHA256SUMS`，提供与包同源的完整性摘要。

## 决策

1. **新增独立 helper 项目 `src/Cafe.Launcher.Updater/`**。主程序下载并校验完成后，把 helper 复制到 `%TEMP%` 运行，自身优雅退出；helper 等主进程退出后再替换、再拉起新版本。helper 不从被替换的目录运行，因此不受文件锁影响；helper 只依赖目标框架之外无第三方依赖，发布为自包含单文件可裁剪 exe。
2. **信任锚是同 release 的 `SHA256SUMS`**。下载包完成后计算 SHA-256 与之比对，不符即中止、不执行。选择逻辑把 `SHA256SUMS` 资产当作自更新的必要项：缺失或未命中目标文件名时退化为「打开浏览器下载页」，绝不无校验执行。
3. **目标资产钉死**：安装版（安装目录存在 `.cafe-launcher-install` 标记，Inno 写入）取 `_setup.exe`；便携版取 `_win-x64.zip`。其它平台或非 x64 一律走既有浏览器跳转。
4. **安装版通过 Inno 提权**：helper 以 `runas` 启动 `setup.exe /SILENT /SUPPRESSMSGBOXES /NORESTART`。UAC 弹窗是用户主动触发更新的一部分，不做静默提权。因为主进程已先退出，安装器的 `AppMutex` 与 `CloseApplications=no` 契约不冲突；`skipifsilent` 意味着安装器不自行拉起，helper 在安装器结束后显式启动应用。
5. **便携版整目录换位**：helper 解压到同卷 staging，旧目录改名备份，staging 移入原位，尽力删除备份；删除失败留给下次启动清理。失败则回滚到旧目录。
6. **helper 二次校验**：启动参数携带期望 SHA-256，helper 在解压/执行前重新校验包，作为纵深防御。
7. **失败保留原版本并记录**：任何一步失败写 `%LOCALAPPDATA%\Cafe Launcher\update-apply.log`，用户主动更新失败不改变仍可用的现版本。

## 被否决的替代方案

- **继续只跳转浏览器。** 用户明确要求应用内执行；保留的只是非 Windows / 无可用包时的回退。
- **用被替换目录里的旧 exe 充当 helper。** 运行中的 exe 被锁定，无法覆盖自身；先复制到临时目录再运行又要把整套运行时复制一遍。
- **用暂存的新版 exe 充当 helper（`--apply-update`）。** 首次自更新依赖被下载的构建自身可运行，把「更新器」与「被更新的代码」耦合在一起；独立 helper 在旧版本一侧已存在且稳定。
- **运行时生成 `.cmd`/`.ps1` 脚本。** 免去一个项目，但不可测、易触发杀软，且脚本能力不足以稳健处理目录换位与回滚。
- **仅靠 HTTPS 与 GitHub 域名钉住。** 提供传输完整性与来源收窄，但不提供包内容完整性；同 release 的 `SHA256SUMS` 成本极低且失败闭合。
- **直接对安装目录做便携解压替换（含安装版）。** 绕过 Inno 的卸载登记与版本记账，且写 `Program Files` 需要提权，收益不明。

## 后果

- Windows 上自更新成为一条真实的代码执行路径：下载并运行 `setup.exe` 或替换安装目录。安全边界由「目标后缀钉死 + URL 校验 + SHA256SUMS 失败闭合 + 用户触发提权 + helper 二次校验」共同给出。
- 非 Windows 行为不变，仍是 `OpenExternalUrl`；`LauncherUpdatePackageSelector` 对它们返回 `ExternalDownload`。
- 需要新的本地化文案、对话框进度态、两处入口接线，以及 helper 的打包/发布步骤。
- 守卫：`LauncherUpdatePackageSelectorTests`（平台/安装态/资产矩阵）、`LauncherUpdateChecksumManifestTests`（解析与畸形输入）、下载与应用编排的单元测试（假传输/假进程）、helper 纯逻辑的编译链接测试。
- 已知限制：无代码签名，信任仍建立在 GitHub 发布账号与 TLS 之上；来源证明（attestation）校验未接入，可后续在 `SHA256SUMS` 之上加一层。
