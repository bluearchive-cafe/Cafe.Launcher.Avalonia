# 移除旧启动器迁移功能设计

## 目标

完整移除旧 Electron 启动器配置迁移功能及其 LevelDB 原生依赖，恢复仓库在干净检出、CI 和发布环境中的独立构建能力。

用户仍可通过现有“选择已安装游戏”入口手动指定游戏目录。

## 删除范围

删除以下模块及其测试：

- `LevelDbReader`
- `OldLauncherDetectionService`
- `OriginalLauncherMigrationService`
- `MigrationWizardViewModel`

删除以下关联行为：

- 首次启动时检测旧启动器
- 从 Chromium localStorage 读取游戏路径、代理模式和关闭行为
- 从旧启动器复制 `clickCode`
- 首次启动迁移弹窗及其应用、跳过和 Escape 处理
- `HasCompletedFirstLaunchWizard` 设置字段
- `leveldb.net` 项目引用、测试项目引用、原生 DLL 复制及裁剪根程序集配置

## 保留范围

- 保留设置页中的“选择已安装游戏”入口。
- 目录选择器不再读取旧启动器数据，初始目录固定使用 `Environment.SpecialFolder.UserProfile`。
- 保留 `ClickCodeService` 的正常归因逻辑，只删除旧启动器 `clickCode` 文件复制。
- 保留游戏目录规范化、安装状态识别及现有本地安装文件兼容逻辑。

## 界面和状态

从 `MainWindow` 组合中移除迁移 ViewModel、迁移弹窗和文件夹选择委托。

从窗口 Escape 策略中移除迁移状态及 `SkipMigration` 动作。其他弹窗的 Escape 优先级和行为保持不变。

从 `LauncherSettings` 中删除 `HasCompletedFirstLaunchWizard`。`LauncherSettingsService` 当前使用的 JSON 反序列化行为允许旧 `settings.json` 继续包含 `hasCompletedFirstLaunchWizard`，该未知字段会被忽略，不需要设置迁移步骤。

## 本地化和文档

从三种语言资源和 `LocalizedStrings` 中删除全部迁移专用字符串。

更新 `README.md`、`AGENTS.md` 和 `CLAUDE.md`，删除迁移能力、模块、测试类和数据流说明。不得改动与迁移无关的架构说明。

## 测试策略

删除只覆盖已移除模块的测试。

调整仍然有效的测试：

- `MainWindowViewModelTests` 不再构造迁移依赖，不再设置首次启动向导完成状态。
- `SettingsEditorTests` 和 `LauncherSettingsServiceTests` 不再断言已删除字段。
- `WindowEscapeStrategyTests` 不再覆盖迁移动作，并继续验证剩余优先级。
- Headless 测试不再直接隐藏迁移弹窗。

验证顺序：

1. 搜索仓库，确认不存在迁移模块、迁移设置字段、迁移本地化键和 `leveldb.net` 引用。
2. `dotnet test`
3. Debug 构建，要求 0 warnings、0 errors。
4. Release 构建，要求 0 warnings、0 errors。
5. self-contained publish，确认不再输出 LevelDB 原生 DLL。

## 提交策略

实现提交使用 Conventional Commits：

`refactor: 移除旧启动器迁移功能`

设计文档和实施计划可作为独立的 `docs:` 提交，便于追踪删除依据。
