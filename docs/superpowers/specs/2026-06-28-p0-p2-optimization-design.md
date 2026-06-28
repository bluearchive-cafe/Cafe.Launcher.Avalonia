# P0～P2 优化设计

## 目标

本设计覆盖三个已经确认的优化范围：

1. P0：收拢 ViewModel 的快照、刷新与窗口动作协调；
2. P1：增加跟随 Windows 且允许手动覆盖的减少动态效果设置；
3. P2：展示精确的磁盘空间、失败文件数量和安装校验重试状态。

本次不改变本地安装状态文档、官方 `manifest.json` wire contract、下载并发数、CRC64 校验规则或安装校验次数。

## P0：协调 seam

### `GameOperationsViewModel`

`GameOperationsViewModel` 保存最近一次 `ApplySnapshot()` 接收的
`LauncherStatusSnapshot`，删除以下可空委托：

- `GetSnapshot`
- `RequestRefreshAsync`
- `RequestRefreshAfterPersistedResumeAsync`
- `ApplySnapshotAsync`
- `MinimizeWindow`

新增两个明确事件：

- `RefreshRequested`：参数区分普通刷新和刷新后跳过持久化下载恢复；
- `MinimizeRequested`：游戏成功启动后请求最小化窗口。

`MainWindowViewModel` 订阅 `RefreshRequested`，集中设置
`skipNextPersistedResume` 并调用 `RefreshAsync()`。

操作结束后的面板恢复直接使用 `GameOperationsViewModel` 保存的快照，不再回调
`MainWindowViewModel.ApplySnapshotAsync()`。

### `ResourcePanelViewModel`

新增 `ApplySettings(LauncherSettings settings)`，从已应用的设置中保存精确的
`ProxyMode` 和 `PatchUrlGroup`。删除：

- `GetProxyMode`
- `GetPatchUrlGroup`

`MainWindowViewModel.ApplySnapshotAsync()` 在应用资源面板状态前调用
`ResourcePanel.ApplySettings(snapshot.Settings)`。

### `WindowChromeViewModel`

打开设置时使用 `Settings.Editor.GetSavedSnapshot()`，删除 `GetSnapshot`。

原生窗口动作使用以下事件：

- `MinimizeRequested`
- `RestoreRequested`
- `CloseRequested`

`MainWindow.axaml.cs` 订阅事件并执行对应 Avalonia `Window` 操作。
不为单一平台实现新增 `I...` interface。

## P1：减少动态效果

### 持久化设置

新增 `MotionModes`：

- `system`
- `full`
- `reduced`

`LauncherSettings` 新增 JSON 键 `motionMode`，默认值为 `system`。
`LauncherSettingsService.NormalizeSettings()` 将其他精确值恢复为 `system`。

设置界面使用三项本地化下拉框，不使用布尔开关。

### Windows 读取

新增 `WindowsAnimationSettingsProvider`，调用：

- `SystemParametersInfoW`
- `SPI_GETCLIENTAREAANIMATION`
- 常量值 `0x1042`

微软文档定义该参数返回 Windows 客户区动画是否启用：
<https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-systemparametersinfoa>

读取失败时按减少动态效果处理。

有效状态解析规则：

- `full`：不减少；
- `reduced`：减少；
- `system`：Windows 动画关闭或读取失败时减少。

解析规则保持为纯函数。平台读取通过可替换读取函数进行测试，不新增第二个生产
adapter。

### 应用范围

减少动态效果时：

- `RemoteContentViewModel` 不启动自动轮播计时器；
- 轮播页面切换时长为零；
- 通知卡片不执行 350ms 淡入；
- 手动上一张和下一张命令继续可用；
- 最小化和恢复窗口不得重新启动自动轮播。

完整动态效果模式保持当前行为。

设置加载和保存后都重新计算有效状态，使手动覆盖立即生效。

## P2：下载状态展示

### 数据模型

`GameOperationProgress` 新增：

- `RequiredDiskBytes`
- `AvailableDiskBytes`
- `FailedFileCount`
- `RetryAttempt`
- `RetryLimit`

`GameOperationResult` 新增 `FailedFileCount`。

`AvailableDiskBytes` 必须能表达读取失败，使用可空 `long`。

### 阶段

新增以下精确阶段值：

- `disk-check`
- `verification-retry`
- `verification-failed`

下载计划生成后读取一次可用空间并发送 `disk-check`。该次读取同时用于磁盘不足
判断，避免 UI 展示值和判断值来自不同时间点。

安装校验仍执行一次初始校验，并最多重试 3 次：

- 初始校验失败后：`RetryAttempt = 1`，`RetryLimit = 3`；
- 第一次重试失败后：`RetryAttempt = 2`；
- 第二次重试失败后：`RetryAttempt = 3`；
- 第三次重试失败后：发送 `verification-failed`。

每次状态携带当次 `FailedFileCount`。不得在 UI 或 diagnostics 中增加失败文件名。

### 界面

进度面板继续使用现有 `ProgressDetail`，不增加文件列表：

- `disk-check`：所需空间和可用空间；
- `verification-retry`：失败文件数量和重试次数；
- `verification-failed`：最终失败文件数量。

可用空间读取失败时显示 `--`。

磁盘不足的 `GameOperationResult.Message` 同时包含格式化后的所需空间和可用空间。

## 本地化

所有新增文本加入 `en`、`zh-Hans`、`ja` 三个字典，并在 `LocalizedStrings` 中增加
对应属性和 `Apply()` 映射。

动态效果下拉项由 `SettingsOptionsViewModel` 生成 `SettingOption`，保存精确 code，
显示本地化名称。

## 测试

所有行为按测试先行实施。

P0：

- 子 ViewModel 在没有父级回调接线时使用已应用快照；
- 普通刷新与跳过持久化恢复请求保持当前差异；
- 原生窗口事件只触发一次；
- 资源面板使用最近一次已应用设置。

P1：

- 三个 `motionMode` 值的序列化、规范化和深复制；
- Windows 返回开、关、失败时的有效状态；
- 减少动态效果时计时器不启动；
- 手动轮播仍工作；
- `CrossFade` 和通知淡入根据有效状态切换；
- 三语言键和 XAML 设计标记契约完整。

P2：

- 磁盘检查只读取一次可用空间；
- 所需空间、可用空间和未知值映射；
- 1～3 次重试状态的次数与失败数量；
- 最终失败结果携带最终 `FailedFileCount`；
- 现有首次校验加最多 3 次重试语义不变；
- Headless 进度面板显示对应文本。

## 提交顺序

1. `refactor(viewmodel): 收拢窗口工作流协调`
2. `feat(accessibility): 增加减少动态效果设置`
3. `feat(download): 展示磁盘与校验重试状态`
4. `test(ui): 覆盖动态效果与下载状态展示`

每个提交必须通过对应定向测试。最终运行 `.\verify.ps1`。
