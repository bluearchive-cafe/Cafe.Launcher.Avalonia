## v1.1.1

![Cafe Launcher v1.1.1 更新概览](https://raw.githubusercontent.com/bluearchive-cafe/Cafe.Launcher.Avalonia/v1.1.1/docs/assets/release-banners/cafe-launcher-v1.1.1-release-banner.png)

`1.1.1` 是一次维护更新：主界面、游戏安装与更新、以及各项设置都与 `1.1.0` 一致，改动集中在文件命名、内部整理与日志读取的一处修正上，下载安装包的读者按往常方式升级即可。

> [!NOTE]
> 这是 Cafe Launcher `1.1.1` 的补丁版。下文列出自上一版 `1.1.0` 以来的变化。
>
> 平台支持与上一版相同：Windows 为正式支持平台；macOS 与 Linux 仍提供实验性构建，其中 macOS 支持安装、更新和修复游戏，暂不支持启动游戏，Linux 可通过兼容运行环境启动游戏，实际效果仍取决于发行版、显卡驱动、Proton 版本及反作弊兼容性。
>
> 从 `1.1.0` 升级到本版无需先卸载：Windows 上可以直接在应用内完成更新，更新通道选「稳定版」时即可看到本版；也可以直接从发布页下载安装程序或免安装压缩包覆盖升级。游戏目录、各项设置、启动器数据位置和桌面快捷方式都会原样保留，升级后不需要重新设置游戏路径。

> [!WARNING]
> 本次更新不包含可见的新功能，主要变更在于文件名与内部整理，另修正了日志查看与导出的一处问题，因此正常使用的读者也可以留在 `1.1.0`。如遇崩溃、启动失败或游戏文件异常，可从启动器导出诊断日志并反馈。macOS 与 Linux 支持仍是实验性功能：请在可回退的环境中试用，兼容运行环境在组件大版本更新后建议重新验证一次游戏启动。

### 自上一版 v1.1.0 以来

- **程序名与安装包名以「Cafe Launcher」为准**：Windows 安装程序改名为 `Cafe.Launcher_<版本>_setup.exe`，免安装压缩包改名为 `Cafe.Launcher_<版本>_win-x64.zip`（macOS 与 Linux 的压缩包同理），便携版压缩包内的可执行文件改名为 `Cafe.Launcher.exe`。任务管理器的进程名、快捷方式图标说明与托盘提示因此统一为「Cafe Launcher」，下载时也更容易认出本启动器的文件。
- **从旧版本升级仍然是平滑的**：已经安装 `1.1.0` 的读者可以直接在应用内更新，不需要先卸载，安装目录与安装记录会被沿用；改名后的辅助更新程序会随新版本一起就位，便携版更新后的备份与恢复行为也与之前一致。
- **界面仍按同一套外观工作**：随本版更新了界面框架依赖并整理了内部结构，画面与操作方式没有变化，个别环境下的显示细节更为稳定；此前的修复也都保留在本版中。
- **日志与诊断读取更准确**：日志文件写满 5 MB 后会自动另起一份，此前日志查看器会因此长期显示空列表、「导出诊断信息」也会失败；本版改为按实际正在写入的那份日志读取，导出的诊断包始终带上当前日志。

### 技术更改

以下记录实现层面的主要变化，供需要核对发布内容的读者展开查看。

<details><summary>展开查看技术细节</summary>

- 程序集结构按 ADR-042 拆分为宿主 `Cafe.Launcher`、`Cafe.Launcher.Core`（无 UI 依赖）与 `Cafe.Launcher.UI` 三层，并新增 `Cafe.Launcher.Updater.Core` 承载可复用的自更新实现；宿主只保留进程生命周期、组合根、跨进程转发与崩溃报告入口，表现层整体迁入 UI 程序集，本地化资源随之迁移（resx 清单名变为 `Cafe.Launcher.UI.Resources.LauncherStrings`）。
- 命名按 ADR-043 统一为「目录名 = .csproj 名 = AssemblyName = RootNamespace = 源码命名空间前缀」，发行资产共用同一产品 token：可执行文件、release 资产前缀、Inno 的 `EXECUTABLE_NAME` 与 `OutputBaseFilename`、macOS 的 `CFBundleExecutable` 与 bundle id、Linux wrapper 的 exec 目标均为 `Cafe.Launcher`。为兼容既有安装，Inno 的 `AppId`、安装标记 `.cafe-launcher-install`、数据目录 `%LOCALAPPDATA%\Cafe Launcher`、单实例互斥名与自更新按后缀筛选包的逻辑都保持不变。
- 自更新入口与包筛选随之对齐：`UpdateHelperCommand.HelperExecutableName` 为 `Cafe.Launcher.Updater.exe`，包选择仍按 `_setup.exe` / `_win-x64.zip` 后缀匹配，因此从 `1.1.0` 的应用内升级路径不受改名影响。
- 发布包可自行核对：每个 release 都附 `SHA256SUMS` 摘要清单与构建来源证明，启动器在应用内更新前会先核对摘要及对应版本，不会运行校验不通过的更新包。
- 数据根与运行版本改由组合根注入，Core 不再解析进程级数据根、也不再反射入口程序集取版本；Core 的公开面按接口收窄，实现收回 `internal`，宿主与表现层的命名空间不再跨程序集共享。
- 依赖更新：Avalonia 系列升至 `12.1.3`（`Avalonia`、`Avalonia.Desktop`、`Avalonia.Themes.Fluent`、`Avalonia.Controls.ColorPicker`、`Avalonia.Headless` 等），并提交无 RID 形态的 `packages.lock.json`；`New-ThirdPartyNotices.ps1` 改为按生产工程生成通知，`AddGitCommitMetadata` 的构建时间按平台分别转义百分号，修复 Linux 上 `%cI` 被原样输出的问题。
- Linux 打包元数据补齐并加上产物门禁：RPM 与 Arch 按 soname 声明自包含 .NET 实际使用的系统库（X11、SSL/Kerberos、libunwind、zlib，ICU 按多个 soname 择一），三种格式共用一份 AppStream 元数据，deb 补 `copyright` 与 `changelog.Debian.gz`；发行流程新增 deb/RPM 实装、AppImage 启动与 Arch `namcap` 的验证步骤。LTTng 只是运行时按需 `dlopen` 的 `libcoreclrtraceptprovider.so` 需要的可选跟踪库，RPM 过滤掉 ELF 生成器为它自动生成的那条 `Requires` 并降为弱依赖，避免在 lttng-ust 2.13+（Fedora 43 只提供 `.so.1`）上装不上；Fedora 门禁改为按文件而不是路径名比较 wrapper（Fedora 41 起 `/usr/sbin` 并入 `/usr/bin`）。

</details>
