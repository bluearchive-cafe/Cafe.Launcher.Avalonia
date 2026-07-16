# 设置向导下载源与代理单选组件设计

## 目标

将设置向导中的下载源和代理选择从带选中边框的卡片按钮改为紧凑的 `RadioButton` 列表，让互斥选择、键盘行为和辅助功能语义一致且清晰。

## 范围

- 下载源步骤的 Cafe 与官方源。
- 代理步骤的自动、直连与系统代理。
- 现有向导视图、样式契约测试和无头 UI 测试。

不修改路径检测、保存设置、网络逻辑、代理逻辑、语言资源或公共服务。

## 组件与绑定

每个选项使用一个 `RadioButton`，其内容保持现有的标题和说明文本。下载源选项使用同一 `GroupName`，代理选项使用另一 `GroupName`，保证两组互斥但彼此独立。

`IsChecked` 双向绑定既有 ViewModel 布尔属性：下载源绑定 `IsPatchUrlGroupCafe` 与 `IsPatchUrlGroupOfficial`；代理绑定 `IsProxyAuto`、`IsProxyDirect` 与 `IsProxySystem`。因此现有 `PatchUrlGroup`、`ProxyMode`、确认页摘要和设置保存行为保持不变，原有选择命令不再是该界面的选择入口。

每个控件继续使用既有本地化标题作为 `AutomationProperties.Name`。原生 `RadioButton` 提供单选圆点、Tab 焦点与方向键组内选择语义；不另行模拟这些交互。

## 视觉规则

- 移除下载源和代理选项的 `wizard-choice` 卡片背景、边框、圆角与按压状态。
- 选中态使用 RadioButton 的实心圆点，未选中态使用空心圆点；不额外叠加卡片高亮。
- 标题与说明继续使用已有 `section-title`、`caption` 类，间距仅使用现有 `LauncherSpacing*` 资源。
- 若需要向导专属 RadioButton 样式，仅限于排版、对齐或令牌化画刷，不使用原始颜色或原始间距值，且不影响其他页面的 RadioButton。

## 测试与验收

- 更新 `UiStyleContractTests`，验证下载源与代理使用 `RadioButton`、各自的组名、既有选中绑定和本地化自动化名称；不再要求 `wizard-choice` 或 `active` 类。
- 更新向导无头 UI 测试，验证点击或键盘选择会切换组内选项、保留另一组的选择，并反映到既有 ViewModel 状态。
- 执行 `UiStyleContractTests`、向导 ViewModel 测试、无头 UI 测试、`test.ps1` 与 `build.ps1`。

## 自检

- 组件范围仅限两个向导步骤，没有引入新设置、服务或本地化键。
- 互斥关系由两组明确的 `GroupName` 表达，下载源与代理不会交叉取消选择。
- 视觉规则和测试规则均与现有设计令牌、自动化名称和绑定契约一致。
