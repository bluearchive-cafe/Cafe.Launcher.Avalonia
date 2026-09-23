# Markdown.Avalonia Fork 可行性评估

## 结论

技术上可行，许可上也可行，但当前不建议立即维护完整 fork。

Cafe Launcher 目前只在更新说明弹窗使用 `Markdown.Avalonia.Tight`。现阶段需求是 GFM 兼容、安全过滤、Alerts 和主题适配；这些需求不足以抵消长期同步 Avalonia、维护解析器正确性、发布私有包和补齐规范测试的成本。更合理的顺序是：先做隔离接缝与替代控件验证；只有替代方案无法满足安全、主题或行为要求时，再 fork。

## 已确认事实

- 当前项目使用 `Markdown.Avalonia.Tight 12.0.0-a3`，而应用使用 Avalonia `12.1.2`。该 Markdown 包是预发布版本。
- 上游仓库使用 MIT 许可证，允许修改、再分发和商业使用；分发 fork 时必须保留原版权与许可声明。[上游许可证](https://github.com/whistyun/Markdown.Avalonia/blob/master/LICENSE.txt)
- `Markdown.Avalonia.Tight` 直接把 Markdown 转换为 Avalonia 控件，内建表格、删除线、代码块等解析器，也公开了插件接口；但核心解析器大量依赖内部正则和内部类型，并不是 CommonMark/GFM AST 驱动的实现。[上游源码](https://github.com/whistyun/Markdown.Avalonia/tree/master/Markdown.Avalonia.Tight)
- 上游为 Avalonia 12 发布了 `12.0.0-a1` 至 `a3`，NuGet 当前仍将 `a3` 标为预发布；上游曾因 Avalonia 12 API 变化发生运行时不兼容问题。[NuGet](https://www.nuget.org/packages/Markdown.Avalonia.Tight/)、[Avalonia 12 兼容问题](https://github.com/whistyun/Markdown.Avalonia/issues/179)
- 正式 GFM 是 CommonMark 的严格超集，并额外定义表格、任务列表、删除线、扩展自动链接和危险 HTML 标签过滤。单靠修补若干正则无法证明规范一致性。[GFM 0.29 规范](https://github.github.com/gfm/)
- Markdig 提供 CommonMark AST、扩展管线和规范测试基础；它比继续扩张项目内的字符串预处理器更适合作为长期解析层。[Markdig](https://github.com/xoofx/markdig)
- 另有基于 Markdig、面向 Avalonia 12 的原生控件 `MarkView.Avalonia`，支持主题、表格、任务列表和可自定义管线，但项目较新、采用量较小，仍需本地 spike 验证。[MarkView.Avalonia](https://www.nuget.org/packages/MarkView.Avalonia)

## 路线比较

| 路线 | 初始成本 | 长期成本 | GFM 正确性 | UI 控制力 | 建议 |
|---|---:|---:|---:|---:|---|
| 继续薄适配现有包 | 低 | 中 | 低至中 | 中 | 适合短期，仅限更新说明 |
| 换用 Markdig 驱动的 Avalonia 控件 | 中 | 低至中 | 高 | 高 | 优先验证 |
| 完整 fork Markdown.Avalonia | 高 | 高 | 默认仍不高 | 最高 | 最后手段 |

## 如果决定 Fork

建议不要把 fork 直接嵌进主应用源码，而是保持独立仓库与独立包身份，例如 `Cafe.Markdown.Avalonia`：

1. 从项目当前使用包所对应的提交建立固定基线，并记录 upstream remote。
2. 更改程序集名、根命名空间和 NuGet 包 ID，避免与官方包冲突。
3. 保留 MIT 许可证、原作者版权和第三方 notices。
4. 先建立 GFM/CommonMark 差异测试；解析正确性必须由规范样例驱动，而不是视觉样例驱动。
5. 将网络图片、链接打开、HTML、资源加载全部改为显式策略接口，默认拒绝远程副作用。
6. 将主题资源改为宿主注入，不在库内硬编码浅色或深色。
7. 为每次 Avalonia 升级建立兼容矩阵、锁文件更新和发布流水线。
8. 定期从 upstream 合并安全修复和 Avalonia 兼容性修复，避免 fork 永久分叉。

## 推荐执行顺序

1. 在应用内先抽出一个很窄的 `MarkdownPreview` 接缝，隔离具体第三方控件。
2. 用项目真实发布说明建立一套固定夹具：标题、列表、表格、任务列表、代码、Alerts、恶意链接、图片和浅/深主题。
3. 对 `Markdown.Avalonia.Tight` 与 `MarkView.Avalonia` 做一次独立 spike，比对渲染、包体、启动耗时、安全边界和跨平台表现。
4. 若 MarkView 满足要求，迁移而不 fork；若不满足，优先向其贡献或维护小型适配层。
5. 只有在必须控制 Avalonia 控件树、上游 API 无法扩展且替代库也失败时，才批准完整 fork。

## 决策门槛

满足以下至少两项才值得 fork：

- 必须实现替代库无法提供的原生 Avalonia 交互或可访问性；
- 必须离线、安全地控制所有链接、图片和 HTML 行为；
- 必须达到规范级 GFM 一致性，且团队愿意长期拥有解析器测试；
- 上游长期无法跟进本项目使用的 Avalonia 版本；
- 该渲染器将扩展为多个产品表面，而不再只是一个更新说明弹窗。

就当前范围而言，这些门槛尚未满足。
