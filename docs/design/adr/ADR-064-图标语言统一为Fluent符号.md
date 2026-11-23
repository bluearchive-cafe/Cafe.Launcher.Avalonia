# ADR-064 图标语言统一为 Fluent 符号

- 状态：已接受（2026-11-15）
- 日期：2026-11-15
- 替代范围：[ADR-013](ADR-013-设置页重设计方向.md) 第 3 条的 Material Symbols 图标选型。主题底座、动态色管线与业务契约不变。

## 背景

[ADR-063](ADR-063-全应用统一Fluent2视觉.md) 将全应用切到 `FluentAvaloniaTheme` 后，控件模板自带的展开器箭头、下拉箭头与勾选标记已经是 Fluent 符号；业务图标仍来自 `Material.Icons.Avalonia` 3.0.2。两套图标的网格、重心和笔画粗细不同，同一条工具栏、对话框动作带或设置导航里并排出现时，Fluent 2 表面呈现明显的拼接感。用户要求全局改用 Fluent 图标。

## 决策

- **唯一字形源**：`Constants/FluentGlyphs.cs` 定义用到的 FluentAvalonia 符号字体码点。XAML 通过 `{x:Static glyphs:FluentGlyphs.*}` 引用，视图模型的图标属性也取同一常量，不写 `\uXXXX` 字面量。
- **渲染元素**：业务图标一律 `fluent:FAFontIcon`，`Glyph` 取常量或绑定视图模型属性；大小取 `Launcher.Icon.*`（16/18/20/22/24），不写 `Width`/`Height`，字形尺寸由排版自行决定。
- **字体绑定**：`App.axaml` 用全局样式 `fluent|FAFontIcon` 绑定 `SymbolThemeFontFamily`（FluentAvalonia 自带符号字体）。隔离报告进程的 `CrashReportApp.axaml` 就地声明同名字体资源与样式，保持自包含边界（[ADR-020](ADR-020-崩溃窗口独立Fluent设计系统.md)）。
- **视图模型**：图标字段持有 `FluentGlyphs` 常量而不是 Material 枚举（`SettingOption.CategoryGlyph`、`RemoteContentItem.SocialGlyph`、`ResourcePanelItem.StatusGlyph`、任务卡/进度/主操作字形）。
- **语义映射**：安装=`Download`、更新=`Update`、进行中=`Sync`、暂停/继续=`Play`/`Pause`、失败=`Error`、完成=`Check`、就绪=`Success`；社交入口按平台映射 `Message`/`Video`/`Globe`/`Image`/`People`/`Mail`/`Account`/`Music`，缺省 `Link`。
- **依赖与致谢**：移除 `Material.Icons.Avalonia` 依赖；关于页删除 Material Icons 致谢行及四语 `aboutCreditMaterialIcons` 键。

## 未采用

- **两套图标语言并存**：Material 业务图标继续配 Fluent 部件，正是本次要消除的拼接；只换局部入口会让剩余图标更显突兀。
- **`FASymbolIcon` 枚举**：FluentAvalonia 的符号枚举缺少 `Info`、`Lock` 等本次需要的字形，且枚举名与产品语义的对应不如 `FluentGlyphs` 常量直白；统一用 `FAFontIcon` 承载码点。
- **保留 `Material.Icons` 以减少改动**：仍会把 24 DIP 网格与 Material 语义带进 Fluent 2 表面，并多分发一套图标字体。

## 后果

- 图标契约加严：`Views_UseSemanticColorsAndTokenizedFluentIconSizes` 约束 `FAFontIcon` 不写盒尺寸、`FontSize` 只取语义令牌；新增 `IconGlyphs_ComeFromTheFluentGlyphsCatalog` 要求视图与控件的 `Glyph` 只来自 `FluentGlyphs` 或视图模型绑定。`AssemblySplitContractTests` 将 `FluentGlyphs` 列入表现层公开面，并继续拒绝 `Material.Icons` 依赖。
- 字形渲染依赖 `SymbolThemeFontFamily`：缺字体时 `FAFontIcon` 显示空框。主应用与崩溃报告进程各自注册该字体，`CrashReportTests` 与无头黄金截图共同守护。
- 图标视觉变化使 `settings-about-credits-zh`、`settings-saving-dark-scaled`、`settings-dark-zh-scaled`、`settings-appearance-dynamic-dark-scaled` 四个黄金基线定向再生；其余基线在既有容差内通过。
- 新增图标必须先在 `FluentGlyphs` 核对码位并在无头渲染中确认字形，再按语义命名收录。

## 验收

四语浅深主题下设置、关于、对话框、Toast、资源面板、向导与崩溃窗口的图标全部由符号字体渲染，无空框；`verify.ps1` 与无头黄金基线全绿；Material.Icons 依赖、致谢行与本地化键不再出现。

## 2026-11-18 渲染修订

小尺寸图标在分数 DPI 下容易失去边缘清晰度。启动器自有图标改用 `FluentIcon`，从同一个 `SymbolThemeFontFamily` 缓存矢量轮廓，在绘制时将轮廓偏移对齐物理像素。菜单经 `FluentIconSource` 使用同一控件，主应用和独立崩溃报告进程各自绑定主题字体。字形映射、尺寸令牌与前景画刷沿用现有定义，FluentAvalonia 继续绘制模板自带的图标。
