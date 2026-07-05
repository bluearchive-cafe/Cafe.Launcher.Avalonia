## v1.0.0-beta.6

Cafe Launcher 的首个集中质量提升版本。全面规范了本地化键命名体系（30+ 键重命名），提取共享 UI 控件统一设置页与对话框，修复多项运行时崩溃和 Avalonia / .NET 代码规范问题，并建立了覆盖率门禁。

---

### 新功能

- **共享 UI 控件**：新增 `Controls/` 目录，提取三个可复用控件：
  - `SettingRow` — 设置行（图标 + 标题 + 描述 + 操作区），所有七个设置分区统一使用
  - `ConfirmDialog` — 确认对话框（StyledProperty 驱动，支持危险操作样式）
  - `LoadingOverlay` — 加载遮罩（不确定进度条 + 标签）
- **设置分类导航描述提示**：鼠标悬停在设置侧边栏分类项上时，显示该分类包含的具体设置内容（`04e9c9c`）
- **NSIS 安装脚本**：支持 Windows 安装向导与发行制品打包（`0d6396a`）
- **设置控件无障碍标注**：所有 ComboBox 和按钮添加 `AutomationProperties.Name`（`6d70ed2`）
- **壁纸主题调色板完整显示**：调色板容器加宽以容纳所有色块（`b6b4430`）

### 修复

- **`System.IO.FileNotFoundException`**：修复发布版本因缺失资源文件导致的启动崩溃（`699c7b3`）
- **远程背景路径解析**：修复自定义背景图片相对路径解析异常（`8729878`）
- **Banner 图片加载失败**：修复因 SSRF 校验和 User-Agent 缺失被 CDN 拒绝的问题（`234449d`）
- **ColorPicker 宽度异常**：修复设置页面 ColorPicker 控件宽度不足导致布局异常（`5cfa851`）
- **设置界面视觉问题**：修复页脚按钮对齐、状态栏布局等三处问题（`7a487fe`）
- **汉化管理对话框尺寸**：固定弹窗尺寸，防止内容溢出（`e13886d`）
- **编译绑定恢复**：DataTemplate 中 `DataType=` → `x:DataType=`，恢复编译绑定（`a41884c`）
- **强调色动态跟随**：`LauncherAccentSoftBrush` / `AccentBorderBrush` / `FocusRingBrush` / `CarouselDotActiveBrush` 改为 `DynamicResource SystemAccentColor`（`a41884c`）
- **GC.SuppressFinalize**：7 个 `IDisposable` 类补充 `GC.SuppressFinalize(this)`（`a41884c`）
- **PageTransition 编译绑定**：Carousel 页切换从 `<Binding>` 元素语法改为属性语法（`a41884c`）
- **`.tmp` 常量提取**：`GameDownloadService` 硬编码 `.tmp` 提取为 `TempFileExtension`（`a41884c`）
- **静默异常吞没**：为关键 `catch` 块增加诊断日志输出，避免错误被无声忽略（`e7c34a2`）
- **未安装操作提示**：游戏未安装时点击修复/启动/卸载，显示正确提示（`050a8f6`）
- **确认对话框宽度**：`ConfirmDialog` 默认最大宽度限制为 540（`12a5d48`）
- **并行测试竞态条件**：修复 `LocalizationService` 初始化线程安全问题（`2bbf20c`）
- **UI 样式契约测试修正**：`ColorPicker.setting-control` 的 `Width` 断言与实际布局一致（`5cfa851`）

### 改进

- **设置与对话框视觉规范化**：七分区统一使用 `SettingRow`，六种确认对话框统一使用 `ConfirmDialog`（`bec30f9`、`a41884c`）
- **本地化键全面规范化**：按功能域统一 30+ 键的前缀命名——设置分组 `settingsGroup*`、日志过滤对齐 `logLevel*`、启动器自更新 `launcherUpdate*`、游戏状态 `game*`、停止下载对话框正名 `stopDownloadTitle/Message`（`aebf422`）
- **设置分类描述修正**：4 个分类的描述文字现已与各分类实际包含的设置项一致（`aebf422`）
- **翻译术语统一**：规范汉化管理对话框功能名称与中文翻译用词（`ec8408e`、`fba04f9`）
- **对话框按钮尺寸统一**：全部对话框底部操作按钮采用一致的高度、间距和颜色规范（`d4c6911`、`a8a2fdd`）
- **覆盖率门禁**：`verify.ps1` 强制手写代码行覆盖率 ≥ 84%、分支覆盖率 ≥ 90%，合并单元测试与 Headless UI 测试报告（`3b818f6`、`21caea5`、`902f63b`）
- **测试覆盖补充**：新增核心业务分支、下载与远程地址安全、平台交互分支、游戏操作 ViewModel 的测试用例（`0668b6b`、`6acc2e7`、`97540c2`）
- **DI 生命周期统一**：全部 ViewModel 改为 `AddSingleton`，消除 Transient 在 Singleton 中的隐蔽依赖（`a41884c`）
- **死代码移除**：`GameOperationsViewModel` 中 `await Task.CompletedTask` 无操作（`a41884c`）
- **冗余属性清理**：移除 `MainWindowLogViewerOverlay` 中与全局配置冲突的 `x:CompileBindings=True`（`a41884c`）
- **文档同步**：`CLAUDE.md` / `AGENTS.md` / `README.md` 更新 DI 生命周期、Controls 目录说明（`a41884c`）

### 移除

- **重复本地化键**：删除 `notInstalled`（合并至 `gameNotInstalled`）、`belowLowestVersion`（合并至 `gameBelowLowestVersion`）（`aebf422`）
- **未使用的本地化属性**：清理 7 个从未在 XAML 中绑定的 `settingsCategory*Description` 死属性（`5cfa851`）
- **失效的卸载登记**：移除过时的 InstallShield 残留（`14b6988`）
- **内联设置行布局**：七个设置分区内联 `Grid` 替换为 `<controls:SettingRow>`（`a41884c`）
- **内联对话框布局**：六种确认对话框内联布局替换为 `<controls:ConfirmDialog>`（`bec30f9`）
