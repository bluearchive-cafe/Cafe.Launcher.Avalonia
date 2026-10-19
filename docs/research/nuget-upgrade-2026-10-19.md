# 2026-10-19 NuGet 升级变更核对

核对范围来自本次 `Directory.Packages.props` 差异；只引用上游发布说明和本仓库实际配置。下表区分上游变化与对启动器的影响，未把上游所有修复都视为本项目曾经触发的问题。测试工具链随后在同一变更中迁移到 MTP 2，表内测试相关行按最终状态记录；迁移依据见 [MTP 迁移核对](mtp-migration.md)。

| 包 | 版本变化 | 上游关键变化 | 对本仓库的影响 |
| --- | --- | --- | --- |
| Avalonia、Controls.ColorPicker、Desktop、Headless.XUnit、Themes.Fluent | 12.1.3 → 12.1.4 | 修复 Button/MenuItem 样式 Command 泄漏、弹出层关闭后的焦点、文本布局释放与省略号、窗口关闭后的 chrome hit test 空引用、托盘模式屏幕信息过期，以及 ColorPicker 对齐。[发布说明](https://github.com/AvaloniaUI/Avalonia/releases/tag/12.1.4) | 控件、弹窗、文本和托盘稳定性可能受益；属于框架补丁。NativeMenuItem 的 CommandParameter 启用态修复不直接对应当前托盘实现，当前实现使用 Click 与 IsEnabled。 |
| MarkView.Avalonia | 12.2.1 → 12.3.0 | 外链默认自动打开是破坏性行为变化；修复链接的事件、命中和鼠标样式；支持 Source 的 fragment 锚点滚动；改善 grid 表格列宽，pipe 表格比例列宽需主动启用。[发布说明](https://github.com/Kryptos-FR/MarkView.Avalonia/releases/tag/v12.3.0) | 更新说明渲染受益于链接修复；`ReleaseNotesMarkdownViewer` 已将 `LinkClicked` 标记为 `Handled`，所以默认外链打开行为仍被拦截。当前没有启用 pipe 表格比例列宽。 |
| Microsoft.Extensions.DependencyInjection | 10.0.12 → 10.0.13 | 官方包页确认版本及对同版 Abstractions 的依赖，但其 Release Notes 只给出通用 .NET 发布链接，未找到可明确归因于 DI 包的独立变更条目。[官方包页](https://www.nuget.org/packages/Microsoft.Extensions.DependencyInjection/10.0.13) | 不能据此声称本次增加了 DI 功能或修复某项容器问题，也不能把整个 .NET runtime 的修复算成本包变化。 |
| coverlet（msbuild → MTP） | 10.0.1 → 10.1.0 | 修复 lambda 与 `is … or …` 模式的分支覆盖率，改善 record 覆盖率及自动属性跳过逻辑；修复进程退出写入与覆盖率读取的竞争。[发布说明](https://github.com/coverlet-coverage/coverlet/releases/tag/v10.1.0) | 覆盖率统计的准确性和稳定性可能改善。迁移后先改用 `coverlet.MTP`；因它跳过控制器已加载的 `Cafe.Launcher.UI`，最终改用 Microsoft Code Coverage，统计口径仍是 Cobertura（见 [MTP 迁移核对](mtp-migration.md)）。 |
| Microsoft.NET.Test.Sdk | 18.10.1 → 18.11.0（随后移除） | 修复 TRX 报告对非 BMP 字符的破坏、Unix 上 RunSettings 反斜杠规范化，并尊重配置文件里的控制台日志详细度。[发布说明](https://github.com/microsoft/vstest/releases/tag/v18.11.0) | MTP 迁移移除了 VSTest 宿主，这些报告与 RunSettings 修复不进入最终依赖图，也不会进入启动器功能。 |
| xunit.v3（`xunit.v3.mtp-v2`） | 4.0.1 → 4.0.2 | 修复非并行测试被误并行，以及串行测试后不能恢复并行；修复 theory 数据未尊重部分条件跳过配置、ANSI 输出和非 Windows TRX 换行问题。[发布说明](https://xunit.net/releases/v3/4.0.2) | 普通单元测试使用新版本；Headless 测试通过 `VersionOverride` 保持 3.2.2，因此不获得这些核心框架修复。 |
| xunit.runner.visualstudio | 3.1.5 → 4.0.1（随后移除） | 4.0.0 增加对 xUnit 4 项目及新配置 schema 的支持，并修复残留前台线程时命令行测试错误通过；4.0.1 修复小线程倍率算为 0 而导致无限线程的问题。[4.0.0](https://xunit.net/releases/visualstudio/4.0.0)、[4.0.1](https://xunit.net/releases/visualstudio/4.0.1) | 最终依赖图不再包含 VSTest 适配器；这些修复不进入最终测试路径。 |

## 传递依赖与配置边界

MarkView 的传递依赖 Markdig 从 1.3.2 升至 1.4.0。它修复表格分隔行、标题自动链接、未配对强调结束符、列表首项缩进及 emoji 紧邻粗体等解析情况；更新说明中出现对应 Markdown 时可能受益。[Markdig 1.4.0 发布说明](https://github.com/xoofx/markdig/releases/tag/1.4.0)

当前应用只配置 MarkView 的受支持 Markdown 扩展与 Alerts，清空图片加载器，也没有引用其 SVG、Mermaid 或 Math 扩展。因此这些可选扩展的 SkiaSharp 等变化不应被描述为本次应用功能变化。依据：`src/Cafe.Launcher.UI/Controls/ReleaseNotesMarkdownViewer.cs` 与生产项目锁文件。

Headless 保留 xUnit 3.2.2 是仓库明确记录的兼容约束：Avalonia.Headless.XUnit 12.1.4 使用该扩展 API，NuGet 最低版本约束不能证明它与 xUnit 4 的扩展接口兼容。依据：`tests/Cafe.Launcher.HeadlessTests/Cafe.Launcher.HeadlessTests.csproj`。这不是上游 4.0.2 发布说明中的限制。

本文是发布变更调查；没有据此增加新的启动器业务功能，也未把依赖版本升级表述为 .NET SDK/runtime 升级。
