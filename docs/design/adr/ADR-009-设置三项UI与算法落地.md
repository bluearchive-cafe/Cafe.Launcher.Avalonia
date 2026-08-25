# ADR-009: 设置三项 UI 形态与取色算法落地（Q24）

- 状态：**已接受**（2026-08-25，P2 决策 Round 2）
- 背景：Q24 三设置项（取色算法/配色变体/中性色）需 UI；M3 字段已就绪（CelebiScore/TonalSpot/BrandBlue 默认）。
- 决策：
  - 外观组「主题颜色」分组内新增 3 个 SettingRow，次序 = 取色算法 → 配色变体 → 中性色；Action 均为 ComboBox；默认 = 字段默认；变更即预览（变体/中性已接入 ApplyScheme）。
  - **取色算法行需真正落地**：用包内 `QuantizerCelebi`+`Score` 实现 M3 壁纸提取器并接入壁纸刷新路径（默认 = M3/Celebi+Score；Octree 保留为选项并作为降级/对照）。
- 后果：既有壁纸模式用户切换默认算法后 accent 轻微变化（spec §7 已记录可接受）；4 语言 resx + AutomationProperties.Name 契约同步。
