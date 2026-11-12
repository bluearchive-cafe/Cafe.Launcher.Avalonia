# Markdown 渲染器选型结果

> 状态：原 fork 评估已结束。当前项目使用 `MarkView.Avalonia`，未维护 `Markdown.Avalonia` fork；依赖版本见 [Directory.Packages.props](../../Directory.Packages.props)。

## 已采用方案

更新说明由 [RemoteMarkdownView](../../src/Cafe.Launcher.UI/Controls/RemoteMarkdownView.cs) 封装 MarkView，解析使用其 Markdig 管线。图片加载、链接打开与资源释放由宿主适配，现行行为见[领域契约](../../CONTEXT.md#设置与操作)，发布说明格式见[开发指南](../DEVELOPMENT.md#发布)。

原评估针对 `Markdown.Avalonia.Tight 12.0.0-a3`，比较了薄适配、Markdig 驱动控件和完整 fork 三条路线。项目采用了替代控件路线；旧包版本、迁移前步骤和 fork 实施清单不再作为当前待办。原始调查及上游证据保留在本文件的 Git 历史。

## 重新评估条件

若未来需要现有控件无法提供的原生交互、可访问性或规范兼容性，先用真实发布说明夹具复现缺口，再评估适配或上游贡献。只有替代方案无法满足需求、且愿意长期承担解析器测试、Avalonia 兼容及发布成本时，才考虑独立 fork。

切换渲染器时仍须验证标题、列表、表格、任务列表、代码、Alerts、图片、恶意链接及浅/深主题；不能以视觉样例替代解析与资源访问边界验证。
