# 设置页下拉菜单 A：真实控件与复现

日期：2026-11-07。第一阶段样张已确认；第二阶段已推广到设置页全部下拉。

样张取自生产 `SettingsAppearanceSection` 中的“外观主题”分段行、“动态效果”
`SettingSelect` 与“主题色”行。保留实际控件、本地化选项、双向绑定、来源色点及
`ItemTemplate`。图片由 Avalonia Headless + Skia 渲染，窗口采用简体中文和默认蓝色种子。

## 查看

每张图左侧为浅色、右侧为深色，均为 100% 原始像素拼接。

- [收起状态](images/closed-light-dark.png)
- [动态效果展开](images/motion-light-dark.png)
- [主题色来源展开](images/source-light-dark.png)
- [主题色来源键盘焦点](images/source-focus-light-dark.png)
- [主题色来源选中项悬停与键盘焦点叠加](images/source-hover-light-dark.png)

## 已落地的边界

常规、游戏、下载与网络、外观、高级分区通过 `settings-section` 共享设置下拉主题。
向导、日志导出、画廊和命令菜单不应用此局部主题。生产样式归
`Views/MainWindow.Styles.axaml`，权威规格见
[设计规范 §3.4](../../docs/design/design-system-spec.md#34-下拉选择与菜单2026-11-01-增补)。

触发器保留 40 DIP 最小高度、14 DIP 字号、8 DIP 圆角、220 DIP 最小宽度，
使用页面底色、中性轮廓和独立活动描边。菜单为 8 DIP 内缩、4 DIP 选项填充圆角，
固定勾选槽；来源色点继续表示“选此来源会得到什么颜色”。输入、滚动和选择行为保留原生模板。

`ThemeApplier` 同步生成画刷的颜色和透明度，修复了 `Primary.Soft` 的 alpha 与旧画刷
透明度相乘的问题。选中底色现在具有生成器规定的 `0x24` alpha（约 14%）。
选中前景优先为主题色，仅在合成背景对比度不足时回退；文字、勾号与选中焦点同步。
共享 `Primary.Soft` 的分段按钮等控件也得到正确浅底。

`PreviewStyles.axaml` 保留第一阶段设计来源作对照，不再编译或加载到测试／应用。
当前导出测试直接验证生产样式，不注入实验主题，也不保存用户配置。

## 重现

在 Windows 仓库根目录使用 PowerShell 7：

```powershell
pwsh ./prototypes/settings-select-md3/Export-Preview.ps1
```

复用现有 `MainWindowHeadlessTests` 上下文、隔离数据根和五分钟挂起保护。
原始逐状态帧写到 `.artifacts/settings-select-md3/`，脚本仅拼接截图像素。
生产黄金基线通过 `pwsh ./test.ps1 -UpdateGolden` 单独维护。

## 验证范围

设置下拉渲染覆盖四语言、明暗主题、100%／150%／200% 字号及各设置分类；
Windows 不显示的 Linux 运行器选择框不宣称已经实机验证。
原生桌面弹窗避让及 macOS／Linux 输入和布局仍需实机走查。
配色测试覆盖八个变体、两种中性色策略与多种饱和／中性种子下的三态对比度。
另覆盖真实双向绑定、色点、焦点几何、Enter 提交、Escape 放弃、禁用与打开时换主题。
展开的实际设置页黄金基线为 `settings-select-source-light` / `settings-select-source-dark`。

第一阶段完整门禁因根目录原始聊天导出中的失效链接而中止，记录保留原样；
后续交付应继续报告实际门禁结果，不把这项历史阻断视为通过。

### 第二阶段验证结果（2026-11-07）

- `pwsh ./dev.ps1 ui`：179 项样式契约、487 项 Headless 全套通过。
- 主题色来源改为可约束宽度的 Grid + 换行文本后，再跑设置选择器专项 12 项与导出用例 2 项，均通过。
- 配色生成器 28 项通过，包含八变体／两种中性色策略的选中三态对比度；37 项黄金截图已再生并检查。
- 主程序 Debug／Release 与独立更新器 Debug 构建零警告；Release 资源契约 8 项通过。
- 配套 `cafe-docs` 已同步中英文设置说明，`npm ci` 与 `npm run docs:build` 通过。
- `pwsh ./verify.ps1` 在 Unit 链接契约处停止：2650 通过、16 跳过、1 失败。
  唯一失败仍来自原始聊天导出的失效链接；覆盖率合并门禁未完成，Release 检查已单独补跑。
