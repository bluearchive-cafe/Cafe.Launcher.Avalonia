# 对话框操作按钮规范化设计

## 目标

统一设置窗口、日志查看器、资源面板及全部确认、警告对话框中的操作按钮尺寸与语义样式，消除主要按钮、危险按钮和次要按钮之间的高度差异。

## 范围

纳入规范：

- 设置窗口页脚操作按钮
- 日志查看器页脚操作按钮
- 资源面板内的操作按钮及页脚操作按钮
- 修复、资源源切换、更新、停止下载、下载中关闭、卸载、放弃设置和崩溃恢复对话框的操作按钮

不纳入规范：

- 对话框标题栏关闭按钮
- 日志筛选标签
- 轮播按钮
- 设置内容区的普通操作按钮

## 统一尺寸

所有纳入范围的按钮使用 `dialog-action` 类，并由该类统一提供：

- `Height`: `{StaticResource LauncherControlHeightDialog}`，当前值为 42
- `MinWidth`: 108
- 水平内边距：16
- 字号：14
- 字重：`SemiBold`
- 图标尺寸：`LauncherIconSm`，当前值为 16
- 圆角：由语义按钮基类使用 `LauncherRadiusSm`，当前值为 4

按钮宽度不固定。短文本保持 108 的最小宽度，长文本及不同语言按内容自动扩展。

## 语义样式职责

- `flat-action`：次要操作，透明背景和描边
- `primary-action`：主要操作，强调色背景
- `danger-action`：破坏性操作，危险色背景
- `dialog-action`：仅负责对话框操作按钮的统一尺寸、排版和密度

组合类的最终尺寸必须由 `dialog-action` 覆盖。`primary-action` 当前固定的 48 高度不得继续影响对话框按钮。

## 特殊按钮

标题栏关闭按钮继续使用 `dialog-close`，保持 36×36。该按钮是纯图标导航操作，不与页脚操作按钮共享尺寸规则。

设置窗口页脚删除独立的 `settings-footer-action` 尺寸规则，改用 `dialog-action`。保存和取消按钮继续分别组合 `primary-action` 与 `flat-action`。

## 验证

增加样式契约测试，验证：

- `dialog-action` 精确使用统一高度、最小宽度、内边距、字号和字重
- `settings-footer-action` 不再承担独立尺寸
- 纳入范围的操作按钮全部包含 `dialog-action`
- 对话框操作按钮中的图标全部使用 `LauncherIconSm`
- `dialog-close` 继续保持独立的 36×36 图标按钮规则

运行单元测试、Avalonia Headless 测试及 Debug/Release 构建。现有与本设计无关的测试失败不在本次范围内修改。
