# 安装路径字段内附属操作设计

## 目标

让用户一眼理解“更改路径”只作用于当前安装路径，同时保持“检测已有游戏”和“安装游戏”的独立操作层级。

本设计仅替代 `2026-07-16-install-path-row-layout-design.md` 中“更改路径位于 `path-field` 外部”的布局决定。上一设计关于路径框自适应宽度、按钮同行、最小窗口可达性及不使用固定宽度的约束继续有效。

## 已选方案

采用“字段内附属操作”布局：

1. 安装路径行仍为单行结构。
2. `path-field` 依次包含路径标签、可省略的路径文本和“更改路径”按钮。
3. “更改路径”位于字段尾部，保留文件夹图标与本地化文字；按钮与路径文本之间不显示分隔线，通过现有间距令牌区分内容与操作。
4. “检测”和“安装游戏”继续位于 `path-field` 外部。“检测”是扫描磁盘并查找已有安装的独立辅助流程，不应表现为只检查当前路径文本；“安装游戏”是最右侧主操作。
5. 路径框本身不响应点击，避免只读文本区域表现为未声明的按钮；只有尾部按钮执行 `ChangePersistedGamePathCommand`。

## 响应式行为

- 路径框占用两个外部按钮之外的全部剩余宽度，不设置固定宽度。
- 窗口收窄时，仅路径文本使用现有 `CharacterEllipsis` 缩短。
- “更改路径”“检测”“安装游戏”保留内容所需宽度，不被压缩或裁切。
- 默认 `1300×754` 与最小 `1024×640` 两种窗口尺寸下，路径框和三个按钮均须保持正尺寸、无重叠且位于窗口内。

## 交互与可访问性

- 保留 `Settings.ChangePersistedGamePathCommand`、`Settings.SelectInstalledGameCommand` 和 `Operations.InstallOrUpdateCommand`。
- 保留所有现有 `IsEnabled`、Tooltip、`AutomationProperties.Name`、`primary-operation`、`secondary-operation` 与 `path-operation` 契约。
- “更改路径”仍是独立 Button，支持键盘焦点和现有全局焦点环。
- 不新增文案、本地化键、ViewModel 状态、设置字段或依赖。

## 视觉规则

- 复用 `LauncherFieldBackgroundBrush`、`LauncherFieldBorderBrush`、`LauncherSpacingMd`、`LauncherIconSm`、`LauncherFieldHeight` 与现有圆角令牌。
- 字段尾部操作与路径文本之间不使用分隔线，使用 `LauncherSpacingMd` 保留 `12px` 间距。
- “更改路径”默认保持透明背景，悬停与键盘聚焦继续复用现有按钮状态和焦点环，不新增阴影或强调色。
- `path-field` 使用语义化复合内边距令牌 `LauncherPathFieldPadding`，其值为 `16,0,4,0`：左侧保留 `16px` 以维持标签对齐，右侧缩减为 `4px`，使尾部按钮背景与字段右边框的留白接近按钮上下留白。
- 该内边距调整只作用于字段容器，不改变 `icon-link` 的内容内边距、高度、圆角、悬停、按压或焦点状态。
- “检测”复用现有次要操作样式，位于 `path-field` 外部，并使用 `LauncherSpacingSm` 与字段保持 `8px` 间距。
- 不改变底部面板其他状态、元数据或刷新按钮布局。

## 验证

- XAML 契约测试确认“更改路径”是 `path-field` 后代，而“检测”和“安装游戏”不是。
- 契约测试确认字段内部顺序为路径标签、路径文本、尾部更改按钮，并锁定原有命令、Tooltip、无障碍名称和样式类。
- 样式契约测试确认 `Border.path-field` 通过 `LauncherPathFieldPadding` 使用复合内边距，不在 View 中直接写入新的原始间距。
- Headless 测试在默认与最小窗口尺寸下验证路径字段、内嵌按钮和两个外部按钮不重叠、未裁切并位于窗口内。
- Headless 测试比较内嵌按钮相对字段的右侧、顶部和底部留白，要求右侧与任一垂直留白的差值不超过 `4px`，从而捕获截图所示的边缘留白失衡回归。
- 运行 `./dev.ps1 ui` 与 `git diff --check`。
