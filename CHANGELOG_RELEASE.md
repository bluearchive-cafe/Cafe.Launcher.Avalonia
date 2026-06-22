## v1.0.0-beta.3

本版本重点强化安全防护、完善日志系统并提升界面可访问性。

### 新增功能

- **SSRF 防护** — 所有对外 HTTP 请求经 `RemoteHttpUrlValidator` 校验，仅允许指向公网地址（拦截 localhost、私有 IP、保留 IP 段）；重定向由 `RemoteHttpRequestService` 手动管理（限制 5 次），每步验证重定向目标 URL，拦截 HTTPS→HTTP 降级攻击。
- **游戏路径符号链接防护** — `GetSafePath` 遍历目标路径的每个已存在组件，检测 `FileAttributes.ReparsePoint`（符号链接 / Junction），防止通过文件系统重解析点将文件写出游戏目录。
- **统一日志与崩溃恢复** — 底层日志引擎替换为 Serilog（异步接收器，10000 条缓冲，5 MB 滚动文件，保留 3 个），全局注入版本号与 CommitSha 信息，通过 `session.active` 文件标记检测上次会话崩溃。
- **运行时日志级别切换** — 支持通过 `LoggingLevelSwitch` 在运行时动态调整日志级别（Debug 默认 Verbose，Release 默认 Information）。
- **游戏路径自动补全** — 选择游戏路径后自动追加 `YostarGames/BlueArchive_JP` 子目录，并提供「检测」按钮。

### 修复与改进

- **下载恢复强化** — 严格校验 Content-Range 响应头（起点偏移量、长度一致性）；临时文件超过预期大小时自动删除重建；服务端忽略 Range 请求时回退为完整下载。
- **系统托盘降级** — `SystemTrayService.Initialize()` 初始化失败时自动释放资源并返回 `false`，不影响主窗口正常启动。
- **日志查看器修复** — 修复 Serilog 文件锁导致日志查看器无法读取日志，以及活动文本日志读取失败的问题。
- **URL 改写精确匹配** — 补丁 URL 改写改用 `Uri.Host` 精确匹配，避免主机名出现在路径或查询参数中时被误改写。
- **界面状态修复** — 修复未安装状态下底部面板更改游戏路径后显示不更新。
- **多用户支持** — `Mutex` 和 `EventWaitHandle` 从 `Global` 改为 `Local` 作用域，允许多用户同时运行启动器。
- **代码稳健性** — 消除空 `catch` 块，补全 `ConfigureAwait(false)`，限制非就绪状态卸载，统一末尾目录分隔符。

### 界面优化

- **窗口可调整大小** — 启用 `CanResize="True"`，移除最小宽度/高度限制。
- **无障碍访问** — 所有仅有图标的按钮添加 `AutomationProperties.Name`，屏幕阅读器可正确读出按钮功能。
- **叠加层焦点管理** — 新增 `OverlayFocusBehavior`：叠加层（设置/对话框）显示时自动聚焦第一个可聚焦控件；叠加层关闭时恢复之前的键盘焦点；设置和对话框叠加层启用 `TabNavigation="Cycle"` 循环导航。
- **Banner 轮播** — 左右翻页按钮添加工具提示与无障碍名称（中/日/英三语）；刷新远端内容时自动停止并重置轮播定时器。

### 技术变更（面向开发者）

- **日志引擎重构** — 从自研写入器迁移至 Serilog，异步管道单例化并共享给 DI 容器和崩溃处理。
- **深层模块提取** — 提取 `IFileDownloadService`、`RemoteManifestService`、`WindowEscapeStrategy`、`ResourcePanelService` 等深模块，减少接口表面积。
- **本地安装状态重构** — 集中安装状态读写，接入统一运行状态，提取游戏安装路径模块并显式注入。
- **设置模块简化** — 内联 `SettingsNormalizer` 消除浅层 seam。
- **基础设施** — 集中 `JsonSerializerOptions` 严格配置，增强 `VersionComparer`，引入 `GameDownloadService.Dependencies` 记录类型集中依赖管理。
- **CI** — 引入 NuGet 缓存，移除构建产物的 Artifact 上传。
- **测试补充** — 新增本地安装状态、下载恢复边界情况、SSRF URL 验证、Banner 轮播状态、符号链接防护、UI 样式契约等测试。
