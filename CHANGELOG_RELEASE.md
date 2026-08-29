## v1.1.0-beta.3

![Cafe Launcher v1.1.0-beta.3 更新概览](https://raw.githubusercontent.com/bluearchive-cafe/Cafe.Launcher.Avalonia/v1.1.0-beta.3/docs/assets/release-banners/cafe-launcher-v1.1.0-beta.3-release-banner.png)

> [!NOTE]
> 本版本重点推进 Linux 下的 Windows 游戏兼容运行环境：新增 UMU/Proton 与 Wine 启动路径、可选运行环境设置和启动失败诊断，同时将游戏运行状态改为由实际启动进程维护。
>
> 当前版本仍属于 `1.1.0` 的 Beta 预发布序列。相较 `v1.1.0-beta.2`，这些向后兼容的新增与修复将预发布号递增为 `v1.1.0-beta.3`，不改变计划中的稳定版主次版本号。
>
> Windows 为正式支持平台；macOS 与 Linux 仍为实验性构建。Linux 兼容运行环境已在 Arch Linux 上使用 `faugus-launcher` 与 `umu-launcher` 的组合完成游戏本体启动实测，但不同发行版、显卡驱动、Proton 版本和反作弊环境仍可能影响兼容性。

### Linux 兼容运行环境（实验性）

- **新增 UMU/Proton 启动支持**：Linux 可通过 `umu-run` 启动 Windows 游戏客户端，并注入游戏标识、Wine Prefix 与可选 Proton 路径；默认 Prefix 使用用户数据目录，不再与游戏安装目录耦合。

- **新增 Wine 备用路径**：当 UMU 不可用或不适用时，可直接通过 Wine 启动；自动模式按 UMU → Wine 的顺序选择可用的兼容运行环境。

- **运行环境可由用户选择**：Linux 设置页新增“兼容运行环境”分组，可选择自动、原生、UMU/Proton 或 Wine；启动器配置可持久化所选运行器，以及可选的运行器可执行文件、Prefix 与 Proton 路径。

- **实机启动验证**：已在 Arch Linux 中安装 `faugus-launcher` 与 `umu-launcher` 后成功进入游戏本体，为后续发行版适配与问题定位提供基线。

### 启动与运行状态可靠性

- **以实际进程作为运行状态依据**：游戏启动后保留宿主进程句柄并观察退出状态；卸载、下载与修复等操作优先依据该跟踪结果判断游戏是否运行，必要时才回退进程名扫描。

- **运行器架构可扩展**：将启动方式拆分为独立运行器层。Windows 原生启动保持既有行为，Linux 运行器则可按平台和环境可用性安全选择。

- **更完整的失败诊断**：运行器不可用时会记录每个候选的原因；启动异常会附带所选运行器、可执行文件、工作目录与原始异常，既显示可操作的提示，也写入本地诊断日志。

### 跨平台安全与工程质量

- **按平台校验路径大小写**：Windows 继续使用不区分大小写的路径比较；Linux/macOS 使用区分大小写的比较，避免安全检查与实际文件系统语义不一致。

- **Linux 新增 deb 安装包**：CI 发版在 Ubuntu 24.04 上使用 `dpkg-deb` 构建 `.deb`，安装到 `/opt/cafe-launcher`，提供 `cafe-launcher` 命令与桌面入口；CI 会校验包元数据与文件布局，并实际安装后通过启动入口读取版本完成冒烟验证。包管理器安装时默认游戏目录仍位于用户主目录，不会写入受保护的 `/opt`。

- **发布说明自动附平台下载链接**：CI 发布时会在发布说明末尾自动生成「下载」小节，按平台列出全部安装包与便携版的下载链接；源仓库与发行仓库的发布说明各自指向对应 release 的资产。

- **补充回归测试**：新增 UMU、Wine、运行器选择、进程跟踪和启动诊断测试，并扩展设置持久化与跨平台路径契约覆盖。
