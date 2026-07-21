## v1.0.0-beta.8

本版本重点重构了首次启动体验（五步设置向导）、引入界面动效系统（过渡动画、系统动效偏好感知、覆盖层可取消退出）与 NSIS 安装器，新增磁盘空间预检查及底部状态信息显示模式切换，并全面优化了主界面状态层级、设置结构、本地化术语与构建质量。

本次更新新增了 7 项特性，优化了 7 项，修复了 12 个问题。

### 新增

- **首次启动设置向导**：五步向导（语言选择 → 安装路径 → 下载源 → 代理配置 → 确认摘要），使用标准 RadioButton 选择下载源和代理，支持路径存在性检测与即时反馈、语言即时预览、导航标题换行、确认页编辑按钮返回修改以及退出确认拦截。

- **界面动效系统**：主窗口新增 `motion-overlay` / `motion-surface` / `motion-content` / `motion-bottom` 四层进入动画，由 `IsMotionEnabled` 与面板可见状态驱动。新增 `MotionVisibility` 控件感知系统动效偏好（窗口激活后延迟刷新，启动失败时保留已解析设置），用户可通过外观设置启用/禁用。覆盖层退出支持取消操作，防止过期任务隐藏当前覆盖层；Toast 退出统一手动/自动路径并精确识别取消来源。Toast 入场缓动由 Linear 改为 QuadraticEaseOut，新增 4px→0 滑入位移，时长从 350ms 缩短至 220ms。提取 `AnimationTimings` 工具类统一管理动画时长。

- **底部状态信息显示模式切换**：新增不显示 / 简略（旧版样式）/ 详细（当前默认）三种模式，设置项位于外观分类。

- **主界面状态与操作层级强化**：统一安装、进度与控制面板的状态布局，按操作类型显示对应进度图标（Sync / Update / CheckCircleOutline），优化路径安装、刷新、轮播和新闻交互。

- **设置与高级功能重组**：设置分类收敛为五项（通用 / 外观 / 下载与网络 / 高级 / 关于）。高级分类新增日志文件操作（打开目录 / 清空日志）和诊断导出选项，日志查看器新增分页加载与搜索防抖。

- **全局字体按语言切换**：移除 Inter 默认字体依赖，根据界面语言自动应用对应字体。

- **磁盘空间预检查与容量策略**：全新安装前检查目标磁盘剩余空间，不足时禁用安装按钮并给出提示；统一容量策略。

### 优化

- **下载进度报告节流**：使用 `Stopwatch.GetTimestamp()` 将进度回调节流至 ~100ms，避免高速下载时 UI 线程过载。

- **LauncherSettings 深克隆性能**：将 JSON 序列化往返的 `DeepClone` 替换为复制构造函数，消除序列化开销。

- **游戏操作工作流边界拆分**：拆分为 `GameLaunchWorkflow` / `GameDownloadService` / `GameUninstallWorkflow`，提取 `GameOperationStage` 等强类型契约。

- **文件下载参数收敛**：提取 `FileDownloadRequest` / `FileDownloadOperationControl` 模型，收敛参数传递。

- **颜色空间计算提取**：约 200 行 HSV/HSL 转换逻辑从 `SettingsAppearanceViewModel` 提取到 `ColorUtils` 工具类。

- **设计令牌提取**：加载条尺寸、排版、动画时长等硬编码值提取为设计令牌。

- **本地化键命名全面规范化**：按功能域统一前缀（`settingsGroup*`、`logLevel*`、`launcher*`），去歧义合并重复键，清理 26 个未使用的本地化键。

### 修复

- **修复 Banner 图片加载失败**：`RemoteHttpUrlValidator` 移除对 CGN、RFC 2544 等被 CDN 和代理软件实际使用的 RFC 保留地址段的拦截；`ImageCacheService` 增加 User-Agent 头解决部分 CDN 拒绝无 UA 请求；修复远程背景相对路径到绝对 URL 的转换。

- **修复静默异常吞没**：审计全部 115 个 `catch` 块，为 8 个静默吞异常的位置添加 `Debug.WriteLine` 诊断输出。

- **修复设置界面视觉与布局**：崩溃恢复对话框按钮高度统一（48→42），深色模式设置内容区背景与对话框统一（#202733→#161C26），ColorPicker 固定宽度 220，导航栏焦点指示条背景跟随自定义主题色，壁纸主题调色板完整显示。全部对话框操作按钮统一 `LauncherControlHeightDialog` 高度，设置行正文保持可读宽度，Toast 关闭按钮补齐本地化提示。

- **修复轮播在刷新期间切换到加载中 Banner**：防止远端内容轮播在数据刷新期间切换到临时占位卡片。

- **修复安装路径响应式布局**：路径输入框宽度自适应及行内按钮响应式回归，将更改路径操作内嵌到路径字段并统一边缘留白。

- **修复 RemoteContentViewModel 重复释放 CTS**：增加 disposed 防护。

- **修复设置字段序列化顺序**：`SettingsEditor` 保持 JSON 字段写入顺序与已有格式一致。

- **修复向导细节**：确认页编辑按钮正确返回对应步骤，语言切换后 UI 即时刷新。

- **修复资源面板状态异常**：`ResourcePanelItem` 新增 `IsOperable` 属性，状态异常时禁用复选框和保存按钮。

- **修复构建与 CI**：移除硬编码 `RuntimeIdentifier`，条件化 `OutputType=Exe`，测试步骤改用隐式 restore 解决 RID 解析，`Test-LocalizationContract.ps1` 兼容 Windows PowerShell 5.1，覆盖率阈值调整为 50% 并切换到 `coverlet.msbuild`。

- **修复安装器卸载登记与彩蛋音频**：清理失效的卸载注册表项，防止彩蛋音频播放异常导致崩溃。

- **修复 `LauncherCoreService.LoadAsync` 同步调用**：将 `.Result` 改为 `await` 避免潜在死锁。
