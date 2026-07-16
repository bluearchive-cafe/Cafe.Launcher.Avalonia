# 主界面底部操作区与状态可读性优化计划

> **执行要求：** 使用 `superpowers:executing-plans` 逐项实施，并在每个任务完成后运行其中列出的验证命令。

**目标：** 根据 `Cafe Launcher 程序界面优化分析`（参考 `ui.html` 与 `cafe_launcher_ui_comparison.html`），修正未安装状态下底部操作区"主按钮与路径栏分离""刷新语义模糊""问号状态图标不准""磁盘空间需用户自行计算"四类问题，并将底部区域整理为稳定两至三行结构。范围限定在主界面底部三面板与状态展示，不改变持久化设置或远程接口契约。

**架构：** 改动集中在 `Views/MainWindow.axaml` 的 install/progress/control 面板、`ViewModels/ShellViewModel.cs` 的状态/磁盘展示派生量、`ViewModels/SettingsOptionsViewModel.cs` 的 `ResolveDiskSpaceText`，以及新增若干本地化键。状态机 `LauncherRuntimeState` 与 `LauncherCoreService` 不变；只是一律通过 `ShellViewModel` 暴露的派生文案与图标驱动同一套状态组件。

**技术栈：** .NET 10、Avalonia、CommunityToolkit.Mvvm、xUnit v3、Avalonia.Headless.XUnit。

**已确认决策：** 安装按钮采用方案 A，直接进入 `path-field` 同一操作行；磁盘空间保留既有 `diskSpace` 原文案，仅追加“充足”或“不足，还差 X”后缀，不新增着色状态或视图枚举。

## 背景与现状核对

- 未安装状态文案 `gameNotInstalled` + 引导 `choosePathInstall` 已存在并在 `ShellViewModel.ResolveStatusText/ResolveOperationNote` 中按 `LauncherRuntimeState.NotInstalled` 输出。
- 未安装状态图标当前为 `HelpCircleOutline`（`ShellViewModel.cs:165,183,269`），与"未知/帮助"语义冲突，需替换。
- 维持 `RefreshAsync` 调用 `LauncherCoreService.LoadAsync`，即重载 API 数据与游戏状态；`refreshTooltip` 已为"Reload launcher API data and game state"。刷新符号统一性已具备，仅按钮归位与文案待澄清。
- 磁盘空间当前输出原始 `diskSpace`（"Required {0} / Available {1}"），已有 `diskSpaceInsufficient`/`diskSpaceInsufficientDetail` 键但未驱动展示；需由"数值"升级为"结论 + 原值"。
- 主操作按钮（安装/开始）当前在 `operation-actions` 列，与路径栏分处两列，未安装时存在分析所述"漂浮感"。

## 全局约束

- 不新增设置字段、公共服务或远程契约；不改变 `LauncherSettings` 序列化字段顺序。
- `LauncherRuntimeState` 枚举值与 `LauncherCoreService` 状态推导不变。
- XAML 仅使用 `App.axaml` 已有 `Launcher*` 令牌与间距资源（`LauncherSpacingXs/Sm/Md/Lg/Xl/Xxl`）；颜色复用现有 `Launcher*Brush`，禁止新增写死颜色、原始角半径与图标尺寸。
- 每个新增 UI 文案同步写入 `Assets/Locales/{en,zh-Hans,zh-Hant,ja}.json`，并经 `LocalizedStrings` 同名 PascalCase 属性暴露；本地化单元测试先写失败用例再实现。
- 不得引入反射式绑定；全部沿用编译期显式绑定。
- 不引出诊断/授权/令牌等敏感信息到 UI 文案。

## 任务 1（P0）：未安装状态图标替换

**文件：**
- 修改：`ViewModels/ShellViewModel.cs`
- 修改：`Assets/Locales/{en,zh-Hans,zh-Hant,ja}.json`
- 修改：`tests/Cafe.Launcher.Avalonia.Tests/LocalizationServiceTests.cs` 与 `tests/Cafe.Launcher.Avalonia.HeadlessTests/MainWindowHeadlessTests.cs`（如有对 `HelpCircleOutline` 的断言）

1. 在 `ResolveStatusIconKind` 中将 `NotInstalled` 分支从 `HelpCircleOutline` 改为语义更准的 `DownloadOutline`（表示"尚需下载安装"）。`SetLoading`/`SetInitial`（§无快照加载中）保留 `HelpCircleOutline`，因加载中确属"未知"。
2. 若 `MainWindowHeadlessTests` 断言过未安装状态图标字符串，同步改为 `DownloadOutline`。
3. 本任务不新增本地化键（图标为 Material 图标名）。
4. 验证：`dotnet test .\tests\Cafe.Launcher.Avalonia.HeadlessTests\... --filter "FullyQualifiedName~MainWindowHeadlessTests"` 全过；`UiStyleContractTests` 不受影响。

## 任务 2（P0）：底部操作区主按钮归位，路径与安装同操作行

**文件：**
- 修改：`Views/MainWindow.axaml`（install 面板 `IsInstallPanelVisible` 分支）
- 修改：`tests/Cafe.Launcher.Avalonia.HeadlessTests/MainWindowHeadlessTests.cs` 与 `UiStyleContractTests.cs`

1. 重排未安装现可见的 install 面板：保留 `operation-status`（状态图标 + 标题 + 引导），将 `ChangePersistedGamePathCommand`/`SelectInstalledGameCommand`/`InstallOrUpdateCommand` 与路径文本整理为同一 `operation-row`，使"确认路径 → 执行安装"在同一行内完成，沿用方案 A 紧凑型优先。
2. 左列为状态区，右列（或同行末尾）为安装主按钮与"检测"辅助按钮；"刷新"按钮移至状态区右上或并入更多菜单（见任务 3 最终定位后再连）。
3. 保持所有按钮既有 `AutomationProperties.Name`/`ToolTip`，主按钮保留 `primary-operation` 类、辅助按钮保留 `secondary-operation` 类；不新增写死尺寸，间距一律引用 `LauncherSpacing*`。
4. 先在 headless/UI 契约测试增/改断言：未安装状态下主安装按钮与路径文本位于同一 `operation-row`，且 `Refresh` 按钮不得再位于 `operation-actions` 主操作列中原位（按任务 3 决定的归位结果）。
5. 验证：`dotnet test .\tests\Cafe.Launcher.Avalonia.HeadlessTests\... --filter "FullyQualifiedName~UiStyleContractTests|FullyQualifiedName~MainWindowHeadlessTests"` 全过。

## 任务 3（P0）：明确"刷新"真实功能并归位

**文件：**
- 修改：`Assets/Locales/{en,zh-Hans,zh-Hant,ja}.json`、`Services/LocalizationService.cs`
- 修改：`Views/MainWindow.axaml`（`RefreshCommand` 按钮归位与 `ToolTip`）
- 修改：`tests/Cafe.Launcher.Avalonia.Tests/LocalizationServiceTests.cs` 与 headless/契约测试

1. 不重命名按钮本体（保持 `refresh`），但重写 `refreshTooltip` 为跨四语种清晰说明：其调用 `LauncherCoreService.LoadAsync`，作用为"重新拉取服务器版本与公告、重新读取本地游戏状态"，消除"检测文件/查找更新"等歧义。文案控制在两行以内，不暴露实现细节。
2. 将刷新按钮从 `operation-actions` 主操作列移出：若分析倾向"刷新服务器状态"则上移到状态区与 `NetworkText`/`VersionText` 相邻；点击区保持 ≥32px 的 `Launcher*` 尺寸令牌约束。
3. 给刷新按钮补 `AutomationProperties.Name`（已具备，复核）。
4. 复用既有 `refreshTooltip` 键与 `LocalizedStrings.RefreshTooltip`，仅更新四语种值，不新增同义键。
5. 验证：默认语言单元测试与术语契约测试通过；headless 断言刷新按钮位于新归位点。

## 任务 4（P0）：磁盘空间从数值升级为"结论 + 原值"

**文件：**
- 修改：`ViewModels/SettingsOptionsViewModel.cs`（`ResolveDiskSpaceText`）
- 修改：`ViewModels/ShellViewModel.cs`（必要时新增派生属性承接"结论"）
- 修改：`Assets/Locales/{en,zh-Hans,zh-Hant,ja}.json`、`Services/LocalizationService.cs`
- 修改：`tests/Cafe.Launcher.Avalonia.Tests/SettingsOptionsViewModelTests`（存在时）/`LocalizationServiceTests.cs`

1. `ResolveDiskSpaceText` 保留既有 `diskSpace` 格式，再根据所需/可用比较追加结论：
   - 充足：追加 `diskSpaceOkSuffix`（例如“（充足）”）。
   - 不足：追加 `diskSpaceShortSuffix`，`{0}` 为所需与可用的差值（例如“（不足，还差 4GB）”）。
   - 未知或所需值无法解析：保留原 `diskSpace` 文案，不追加结论，继续展示已知的所需/可用信息。
2. 不新增 XAML 着色、`DiskSpaceStatusKind` 或其他视图状态枚举。
3. 四语种新增 `diskSpaceOkSuffix`、`diskSpaceShortSuffix` 两键及其 PascalCase 属性，先写失败测试。
4. 保留既有 `diskSpaceInsufficient*` 键，避免破坏向后兼容（settings 规范要求保留旧键字段语义）。
5. 验证：`ResolverDisk` 单测覆盖三种分支；本地化测试四语种通过。

## 任务 5（P1）：底部区域稳定网格与间距

**文件：** `Views/MainWindow.axaml`、`Views/MainWindow.Styles.axaml`、`tests/.../UiStyleContractTests.cs`

1. 统一三面板 `operation-layout` 的左右内边与列间距，全部引用 `LauncherSpacing*`（建议列间距 `Lg`、左起点对齐 `LauncherSpacingXl`），使状态图标、状态文本、版本/空间/服务器均按同一基线排布。
2. 状态图标列固定宽度，避免文本缩进错位（设 `LauncherIconXxl` 栅格位）。
3. 在 `UiStyleContractTests` 增断言：install 面板子元素 `ColumnDefinitions` 起点一致、无残留 `Margin` 写死值。
4. 验证：契约测试全过；Debug 构建 0 警告。

## 任务 6（P1）：轮播控件点击区、暂停与页码关系

**文件：** `Views/Styles/RemoteContent.axaml`、`Views/MainWindow.Styles.axaml`、`ViewModels/RemoteContentViewModel.cs`、四语种文件、`LocalizationService.cs`、headless/契约测试

1. 扩大上一/下一张箭头点击热区至 ≥32×32：用 `LauncherIconMd`/`LauncherIconButtonSize`（已有则复用），增 hover/pressed 背景与键盘焦点样式，复用 `LauncherChromeHover*`/`LauncherAccentSoftBrush`，禁写死色。
2. 暂停按钮与 `carouselPageText` 分离为两个独立图标按钮/信息块：暂停按钮独立、页码简化为"N / M"；复用既有 `pauseCarousel`/`resumeCarousel` 文案与 `CarouselPauseIcon`。
3. 评审 `BannerDots` 旁的细竖线（若存在装饰线）：删除或改为清晰分段进度，避免误判为滚动条。
4. 验证：`MainWindowHeadlessTests` 中轮播相关用例通过；`UiStyleContractTests` 断言点击热区尺寸令牌而非写死值。

## 任务 7（P1）：新闻列表信息密度

**文件：** `Views/Styles/RemoteContent.axaml`、`ViewModels/RemoteContentViewModel.cs`、四语种文件、契约测试

1. 默认展示 2~3 条新闻，每条固定高度、标题最多两行、日期右对齐、整行可点击；超长标题 `ToolTip` 给完整内容。
2. 列表项间距统一 `LauncherSpacingSm`；不写死颜色。
3. 验证：headless 断言新闻列表默认可见条数与单项高度一致性。

## 任务 8（P2）：顶部图标对比度与品牌冗余

**文件：** `Views/MainWindow.axaml`、`Views/MainWindow.Styles.axaml`、契约测试

1. 顶部 chrome 图标加半透明 hover 底板（复用 `LauncherChromeHoverBrush`），点击区 ≥32×32，关闭按钮增"危险"状态反馈（复用 `LauncherDangerBrush`）。
2. 品牌冗余审计：标题栏不存在与游戏 Logo 并列的重复品牌，确认无需调整，避免为不存在的问题引入视觉改动。
3. 验证：契约测试断言 chrome 按钮尺寸与令牌引用。

## 任务 9（P2）：统一控件高度/圆角/描边与键盘/高 DPI 适配

**文件：** `Views/MainWindow.Styles.axaml`、`App.axaml`、契约测试

1. 走查 `ConfirmDialog`/`SettingRow`/`icon-link`/`flat-action` 等控件高度令牌（`LauncherFieldHeight`/`LauncherChipHeight`/`LauncherDialogTitleHeight`）是否在所有控件被引用，消除零散写死高度。
2. 为关键按钮补全键盘焦点样式与 `ToolTip`；复核高 DPI 缩放下字号使用 `LauncherFontSize*` 而非裸双精度。
3. 验证：`UiStyleContractTests` 扩枚举令牌使用断言；Debug 构建 0 警告。

## 任务 10（P2）：状态组件化以覆盖完整流程

**文件：** `tests/Cafe.Launcher.Avalonia.Tests/MainWindowViewModelTests.cs`；必要时才修改 `ViewModels/ShellViewModel.cs`

- 不新增 `LauncherRuntimeState` 枚举值；枚举恰有 7 个值：NotInstalled/Corrupted/IoFailure/RemoteUnavailable/BelowLowestVersion/UpdateAvailable/Ready。
- 以表驱动测试验证 7 态均经过同一 `ShellViewModel` 状态文案、操作说明和图标管线；增加枚举全集守卫，确保未来新增值时必须补明确映射。
- 继续复用现有三个 `operation-status` 视图块，不为测试覆盖而臆造新控件或状态。
- 验证：`MainWindowViewModelTests` 的 7 态管线测试与三面板 headless 测试通过。

## 验收

- `dotnet build .\Cafe.Launcher.Avalonia.csproj -c Debug --no-restore`：0 警告 0 错误。
- `dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\... -c Debug --no-restore` 与 `.\tests\Cafe.Launcher.Avalonia.HeadlessTests\...`：全过；尤其 `UiStyleContractTests`、`LocalizationServiceTests`、`LocalizationTerminologyTests`、`SettingsCategoryTests`。
- `scripts\Test-LocalizationContract.ps1`：通过。
- 四语种键集保持对齐，无新增孤立键。
- P0 四项完成后，未安装状态下底部呈现"状态 → 安装条件（原值 + 结论后缀）→ 路径与安装按钮同操作行 → 刷新归位并语义清晰"的稳定结构，与用户确认的方案 A 一致。
