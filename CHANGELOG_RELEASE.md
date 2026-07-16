## v1.0.0-beta.6

本版本重点搭建质量基础设施并规范 UI 组件体系。新增 NSIS 安装程序与代码覆盖率门禁，提取通用设置行与对话框控件以消除重复 XAML，按语言自动切换系统字体与主题色方案，并新增繁体中文支持。

本次更新新增了 8 项特性，修复了 14 个问题。

### 新增

- **NSIS 安装程序与发行打包**：新增 `installer/Cafe.Launcher.Avalonia.nsi` 安装脚本和 `scripts/Build-Distribution.ps1` 打包脚本，支持构建独立的 NSIS 安装程序（EXE）和便携 ZIP 包，安装路径为 `C:\Program Files\Cafe Launcher`，升级时自动清理旧安装文件。
- **彩蛋**：新增 `EasterEggAudioService`（基于 NAudio 的 Ogg Vorbis 音频播放）。在 ?? 月 ? 日当天，启动器标题会随机显示；在版本号区域连续点击 8 次可触发特殊音效。
- **按界面语言切换系统字体**：新增 `LanguageFontFamilyService`，根据当前语言自动选择合适的系统字体——英文使用 Segoe UI、简体中文使用 Microsoft YaHei UI、繁体中文使用 Microsoft JhengHei UI、日文使用 Yu Gothic UI。窗口字体随语言切换实时更新。
- **繁体中文（zh-Hant）语言支持**：新增 345 条繁体中文翻译，覆盖所有 UI 界面（设置分类、对话框、操作进度、系统状态等）。
- **代码覆盖率门禁**：新增 `coverage.ps1` 脚本和 `coverage.runsettings` 配置，在 CI 和本地验证中同时采集单元测试和 Headless UI 测试的覆盖率（XPlat Code Coverage / Cobertura 格式），合并报告并强制 70% 行覆盖与分支覆盖阈值。
- **可复用 UI 控件**：提取三个通用控件以减少重复 XAML：
  - `SettingRow` — 统一设置行布局（图标 + 标题描述 + 操作控件插槽），所有设置分组改用此控件。
  - `ConfirmDialog` — StyledProperty 驱动的确认对话框，替代 5 个重复的对话框 XAML 块（修复、卸载、停止、关闭、资源面板来源确认）。
  - `LoadingOverlay` — 通用加载遮罩（不定进度条 + 标签文本），在 Banner 轮播和远程内容加载中复用。
- **设计令牌扩展**：新增字体大小令牌（Xs 到 Display，共 10 级）、字重令牌（Normal / SemiBold）、等宽字体令牌（Consolas），以及控制高度令牌（Swatch、Chip、Field、DialogTitle）。
- **设置分类导航提示**：设置导航栏各分类项新增 `ToolTip` 显示分类描述文本。

### 优化

- **设置结构精简**：移除"通知与内容"和"高级"两个设置分类，将通知开关和远程内容卡片设置并入"外观"分类，高级诊断功能通过日志查看器直接访问。设置导航从 7 项缩减为 5 项（一般 / 游戏 / 下载与网络 / 外观 / 关于）。
- **对话框操作按钮规范化**：所有对话框按钮统一使用 `dialog-action` 样式类和 `LauncherControlHeightDialog`（42px）高度；移除按钮内容中的硬编码 `Foreground` 属性，由按钮父级样式统一控制。
- **主题色系统全面动态化**：`LauncherAccentSoftBrush`、`LauncherAccentBorderBrush`、`LauncherFocusRingBrush`、`LauncherCarouselDotActiveBrush` 全部改为跟随 `SystemAccentColor`，配合自定义主题色模式实现完整换肤。
- **状态摘要布局统一**：设置页状态摘要行改为单行横向排列（标题 + 版本 + 网络状态 + 磁盘空间），移除了独立的操作备注行。
- **本地化键名规范化**：全面重命名本地化键以确保命名一致性和可发现性（如 `loadingTitle` → `launcherLoadingTitle`、`loadingValue` → `launcherLoadingValue`、`installPathNotConfigured` → `gameInstallPathNotConfigured`），并清理 20+ 个未使用的键。
- **静默异常诊断增强**：在 `SettingsViewModel` 中为设置保存、游戏路径更新、外观预览、日志级别应用等关键 catch 块增加了 `Debug.WriteLine` 诊断输出，避免异常被静默吞没。
- **单元测试框架升级**：单元测试项目从 xUnit v2（2.9.3）迁移至 xUnit v3（3.2.2），移除 `Xunit.SkippableFact` 依赖，更新 `Microsoft.NET.Test.Sdk` 至 18.7.0。

### 依赖变更

| 包 | 旧版本 | 新版本 |
|---|---|---|
| Avalonia（全套） | 12.0.4 | 12.0.5 |
| Serilog | 4.2.0 | 4.3.1 |
| Serilog.Sinks.File | 6.0.0 | 7.0.0 |
| Microsoft.Extensions.DependencyInjection | 10.0.0 | 10.0.9 |
| AvaloniaUI.DiagnosticsSupport | 2.2.2 | 2.2.3 |
| NAudio | — | 2.3.0（新增） |
| NAudio.Vorbis.Latest | — | 1.6.0（新增） |
| Avalonia.Fonts.Inter | 12.0.4 | 已移除 |

### 修复

- 修复远程背景图片因 API 返回相对路径而无法加载的问题（`LauncherApiClient` 现在对 `/prod/BlueArchive_JP/launcher_background_img/` 前缀的相对路径自动拼接完整 URL）。
- 修复 Banner 图片因 SSRF 校验和缺少 User-Agent 请求头导致加载失败的问题。
- 修复 `RemoteHttpUrlValidator` 私有地址检测范围过宽的问题——移除对 `100.64.0.0/10`（CGNAT）、`198.18.0.0/15`（基准测试）、`198.51.100.0/24`（文档示例）、`203.0.113.0/24`（文档示例）的拦截，仅保留真正不可路由的私有地址。
- 修复 `RemoteHttpUrlValidator` 在 URL 解析失败时提供诊断详情（包含被拦截的具体 IP 地址信息）。
- 修复 `LocalizationService` 在多测试并行执行时的竞态条件。
- 修复 `GameDownloadService` 中 `.tmp` 临时文件扩展名的魔术字符串问题，提取为 `TempFileExtension` 常量。
- 修复 `LauncherApiClient`、`GameDownloadService` 等 `IDisposable` 实现未调用 `GC.SuppressFinalize` 的问题。
- 修复资源管理对话框（汉化管理界面）尺寸因本地化文本长度变化导致的布局问题，面板改用固定尺寸（720×592）。
- 修复设置导航栏选中项背景色不跟随自定义主题色的问题。
- 修复壁纸主题调色板显示不完整的问题。
- 修复未安装游戏时点击不可用操作缺少提示的问题。
- 修复确认对话框宽度过宽的问题，添加 `MaxWidth` 限制。
- 修复 `EasterEggAudioService` 中的 `ObjectDisposedException` 传播问题，添加线程安全的释放守卫。
- 修复安装程序卸载时对废弃注册清理的遗漏。
