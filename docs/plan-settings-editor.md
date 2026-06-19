# Plan: SettingsEditor 深模块提取

> 状态：全部完成 ✓
> 日期：2026-06-19
> 来源：架构审查候选 1（`/improve-codebase-architecture`）

## 目标

将 `SettingsViewModel`（1019 行浅模块）折叠为深模块 `SettingsEditor` +
薄绑定适配器 `SettingsViewModel`（~100 行）。

## 当前问题

`SettingsViewModel` 是一个浅模块——其接口（25+ 个 `[ObservableProperty]` 字段、
14+ 个 `partial void On*Changed` 脏标记方法、9 个选项集合）与实现规模几乎相同。
具体摩擦点：

- **透传属性**：25 个可观察属性是 `LauncherSettings` 字段的 1:1 镜像
- **样板脏标记**：14+ 个 `On*Changed` partial 方法全部做同一件事——调用 `MarkSettingsDirtyIfVisible()`
- **重复模型组装**：`SaveSettingsAsync()` 和 `ChooseGamePathAsync()` 各写了 16 行相同的属性→`LauncherSettings` 赋值代码
- **界面臃肿**：作为 ViewModel 承担了过多状态管理职责，难以独立测试

## 设计决策

### 新接口

```csharp
public interface ISettingsEditor : INotifyPropertyChanged
{
    LauncherSettings Current { get; }                   // 当前快照，供 XAML 直接绑定
    void ApplySnapshot(LauncherSettings settings);      // 加载，清除脏标记
    void Commit(Action<LauncherSettings> apply);        // 修改，自动脏标记
    void Discard();                                      // 回滚到上次 ApplySnapshot
    bool IsDirty { get; }
}
```

### 职责划分

| 移入 SettingsEditor（~300 行） | 留在 SettingsViewModel（~100 行） |
|---|---|
| 25 个设置字段值（通过 `Current` 暴露） | 9 个选项集合（Dropdown 数据源） |
| 脏标记追踪 | 9 个 `Refresh*Options()`（本地化） |
| `LauncherSettings` ↔ 状态映射 | `ToastService` / `ExternalLinkService` / `LauncherUpdateService` 调用 |
| 主题色面板状态（`ThemeColorPaletteItems`） | 持久化（`LauncherSettingsService`） |
| `GetSnapshot()` 模型组装 | `ApplyAccentBrushes()`（Avalonia 依赖） |
| | 命令方法（命令 → `editor.Commit` + 副作用） |

### 迁移策略：路径 A（自底向上）

1. **新建 `SettingsEditor`** + 9 个单元测试
2. **SettingsViewModel 双路径共存**：构造函数同时注入新旧两条路径，逐步把内部调用从直接字段操作迁移到 `editor.Commit()`
3. **验证测试**：确认全部通过后，删除 25 个 `[ObservableProperty]`、14 个 `On*Changed` partial 方法、重复模型组装代码
4. **切换 XAML 绑定**：`{Binding SelectedLanguage}` → `{Binding Editor.Current.Language}`
5. **清理**：删除 `BulkUpdate()`、`suppressSettingsDirty` 等不再需要的代码

### 排除范围

- 9 个选项集合 + `Refresh*Options()` → 留在 ViewModel
- `ToastService` / `ExternalLinkService` / `LauncherUpdateService` 调用 → 留在 ViewModel
- 持久化 → 留在 ViewModel（Editor 不碰文件 I/O）
- `ApplyAccentBrushes()` → 留在 ViewModel（Avalonia 依赖）
- `MainWindowViewModel.WireChildren()` → 不变
- XAML 布局和样式 → 不变（仅改绑定路径）
- DI 注册（`ServiceConfiguration`）→ 不变（SettingsEditor 注册为 transient）

## 新建测试

| # | 测试 | 说明 |
|---|---|---|
| 1 | `ApplySnapshot_LoadsAllFields` | 25 个字段全部正确加载 |
| 2 | `Commit_ModifiesField_IsDirtyTrue` | 修改任意字段后脏标记为 true |
| 3 | `ApplySnapshot_AfterCommit_ClearsDirty` | 保存后重新加载清除脏标记 |
| 4 | `Discard_RevertsToLastSnapshot` | 修改后 Discard，状态回滚 |
| 5 | `GetSnapshot_ReturnsCompleteLauncherSettings` | Current 包含所有字段 |
| 6 | `Discard_WithoutModification_NoOp` | 未修改时 Discard 状态不变 |
| 7 | `Commit_ThemeColorPalette_UpdatesCorrectly` | 主题色面板特殊逻辑 |
| 8 | `DefaultValues_MatchLauncherSettingsDefaults` | 初始状态与默认值一致 |
| 9 | `PropertyChanged_FiresOnCommit` | Commit 后 Current 变为新对象，触发 INPC |

## 删除的代码

- 14 个 `partial void On*Changed(string/bool/Color value)` → Editor 的 `Commit()` 自动处理
- `suppressSettingsDirty` / `MarkSettingsDirtyIfVisible()` → Editor 内部管理
- `SaveSettingsAsync` / `ChooseGamePathAsync` 中重复的属性→模型组装 → `GetSnapshot()` 替代
- `BulkUpdate()` → `ApplySnapshot()` 替代
- `LoadFromSnapshot()` / `ApplyLauncherSettings()` 中的属性逐行赋值 → Editor 内部处理

## 文件变更清单

| 文件 | 操作 |
|---|---|
| `Services/SettingsEditor.cs` | **新建** |
| `ViewModels/SettingsViewModel.cs` | 大幅删减（1019 → ~100 行） |
| `tests/.../SettingsEditorTests.cs` | **新建** |
| `tests/.../MainWindowViewModelTests.cs` | 绑定路径小改 |
| `Views/MainWindowSettingsOverlay.axaml` | 绑定路径替换（`SelectedLanguage` → `Editor.Current.Language` 等） |
| `Services/ServiceConfiguration.cs` | 注册 `ISettingsEditor` / `SettingsEditor` |

## 实施步骤

- [x] Step 1: 新建 `Services/SettingsEditor.cs`，实现 `ISettingsEditor`
- [x] Step 2: 新建 `tests/.../SettingsEditorTests.cs`，9 个测试全部通过
- [x] Step 3: DI 注册 `ISettingsEditor`（transient）
- [x] Step 4: `SettingsViewModel` 构造函数注入 `ISettingsEditor`，双路径共存
- [x] Step 5: 迁移 `LoadFromSnapshot` / `ApplyLauncherSettings` → `editor.ApplySnapshot()`
- [x] Step 6: 迁移 14 个 `On*Changed` → `PushToEditor(editor.Commit())`
- [x] Step 7: 迁移 `SaveSettingsAsync` / `ChooseGamePathAsync` 模型组装 → `editor.Current`
- [x] Step 8: 迁移 `GetThemeColorPaletteHexes` / 面板状态 → `PushToEditor()`
- [x] Step 9: 切换 XAML 绑定路径（`SelectedLanguage` → `Editor.Current.Language`）
- [x] Step 10: 删除旧代码（25 属性、14 partial、BulkUpdate、重复组装等）
- [x] Step 11: 运行全部测试确认通过
- [x] Step 12: 构建 `dotnet build -c Release --no-restore`，零警告零错误
