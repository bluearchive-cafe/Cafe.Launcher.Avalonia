# 设置状态摘要高度统一

## 目标

将设置界面状态摘要中的左侧图标容器和右侧两个状态信息块统一为 32px 高度。

## 设计

- 左侧 `settings-icon` 继续使用 `LauncherChipHeight`，值为 32px。
- 右侧 `status-detail` 使用同一个 `LauncherChipHeight` 作为固定高度。
- `status-detail` 的内边距由四边 12px 调整为水平 12px、垂直 0px。
- 状态文本继续垂直居中。
- 不修改普通设置行的图标尺寸，不修改其他使用 `status-detail` 的布局结构。

## 验证

- XAML 样式契约测试确认 `status-detail` 的高度和内边距使用上述精确值。
- 无头测试确认状态摘要中一个 `settings-icon` 和两个 `status-detail` 的实际高度均为 32px。
- 执行 Debug 构建、单元测试、无头测试和 Release 构建。
