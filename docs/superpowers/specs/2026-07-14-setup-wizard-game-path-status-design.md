# 设置向导游戏路径状态设计

## 目标

在设置向导第二步自动提供与原版启动器一致的游戏目录，并在路径自动填入、手动修改或通过目录选择器变更后显示安装状态和路径可用性。

## 已确认的行为

- 当向导首次进入第二步且 `GamePath` 为空时，填入 `GameInstallationPath.GetDefaultGamePath()` 的结果。
- 非空路径不被自动覆盖；返回第二步也不重置用户的选择。
- 目录不存在或没有 `game-launcher-config.json` 和 `manifest.json` 时，显示“可用于安装”，并允许继续。
- 两个状态文件完整、格式正确且 `vc` 校验通过时，显示“已检测到有效安装”，并允许继续。
- 状态文件不完整、格式错误、版本不一致或 `vc` 校验失败时，显示“安装状态损坏”，阻止继续。
- 无法规范化、读取或访问路径时，显示“无法访问目录”，阻止继续。
- 检测只读取本地目录和状态文件；不创建目录、不写入游戏文件、不写入原版启动器配置。
- 快速输入或连续选择目录时，仅显示最新路径的检测结果。

## 原版启动器兼容性结论

原版启动器源码位于 `E:\Repos\***REMOVED***\***REMOVED***\resources\***REMOVED***`。

- `out/main/index.js` 的 `request-default-download-path` 将 `dirname(app.getPath("exe"))` 的上级目录交给 `checkPath`。
- 同文件的 `checkPath` 补全 `YostarGames\\BlueArchive_JP`。
- `out/main/constant-a9fc53dd.js` 定义原版状态文件名为 `manifest.json` 和 `game-launcher-config.json`。
- `out/main/index.js` 的 `handle-check-game` 要求目录名为 `BlueArchive_JP`，并从 `game-launcher-config.json` 读取 `version` 与 `name`。
- `out/main/index-f057dd97.js` 写入与本项目 `LocalManifest`、`GameLauncherConfig` 相同的状态字段及 `vc` 哈希。
- 原版在其渲染进程本地存储中保存 `downloadPath`；本项目继续使用自己的启动器设置存储，不读取或修改原版该项。

因此，本项目可安全复用 `GameInstallationPath.GetDefaultGamePath()` 和 `LocalInstallationStateStore.ReadAsync(...)` 进行只读检测。原版在同目录写入状态文件时，向导会在下一次检测中反映其当前状态；本项功能不会与原版并发写入。

## 架构与数据流

`SetupWizardViewModel` 保持向导草稿路径和显示状态；它通过 `GameInstallationPath` 取得默认路径并规范化路径，通过 `LocalInstallationStateStore` 读取现有安装状态。该 ViewModel 只公开适合 XAML 绑定的状态、文案和继续条件，不承担文件写入。

进入第二步、`GamePath` 变更和目录选择成功都触发同一个异步检测入口。每次触发取消前一次检测，并在检测完成时核对路径版本，避免过期结果覆盖较新的输入。`CanGoNext` 在第二步同时要求路径存在、状态已完成且结果为可继续。

`SetupWizardOverlay.axaml` 在路径输入行下放置一个状态行，使用现有设计令牌和状态颜色。四个 `Assets/Locales/*.json` 文件增加同一组状态标题与说明，全部经由 `LocalizedStrings` 使用。

## 状态映射

| 路径与读取结果 | 向导状态 | 是否可继续 |
| --- | --- | --- |
| 空路径 | 未选择路径 | 否 |
| `LocalInstallationStateKind.NotInstalled` | 可用于安装 | 是 |
| `LocalInstallationStateKind.Valid` | 已检测到有效安装 | 是 |
| `LocalInstallationStateKind.Corrupted` | 安装状态损坏 | 否 |
| `LocalInstallationStateKind.IoFailure` 或路径规范化异常 | 无法访问目录 | 否 |
| 检测尚未完成 | 正在检查安装状态 | 否 |

## 验收与测试

- ViewModel 测试覆盖：首次进入第二步填入默认路径、已有路径不被覆盖、四种 `LocalInstallationStateKind` 映射、路径异常、取消过期检测和第二步继续条件。
- `LocalInstallationStateStore` 测试继续覆盖原版状态文件格式和 `vc` 完整性；新增的向导测试只以该服务的公开读取结果为依据。
- 无头 UI 测试覆盖默认路径显示、状态行显示、选择有效目录后启用下一步、选择损坏或不可访问目录后禁用下一步。
- `UiStyleContractTests` 覆盖第二步状态行的绑定、自动化名称和设计令牌使用。
- 新文案同步加入 `Assets/Locales/en.json`、`zh-Hans.json`、`zh-Hant.json` 与 `ja.json`，并由现有本地化键一致性测试验证。
- 执行 `UiStyleContractTests`、向导单元测试、无头 UI 测试、`test.ps1` 和 `build.ps1`。
