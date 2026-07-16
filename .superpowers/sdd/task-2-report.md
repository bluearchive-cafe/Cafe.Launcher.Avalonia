# 任务 2 报告：设置向导游戏路径状态

## RED / GREEN 证据

- RED：新增 `NextCommand_FirstEntryToStep1WithEmptyGamePath_FillsDefaultGamePath` 后执行 `dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~SetupWizardViewModelTests"`。新增测试失败：预期 `GameInstallationPath.GetDefaultGamePath()` 的结果，实际为 `""`。
- GREEN：在 `OnStepChanged` 增加仅首次进入第二步的默认路径初始化后，继续完成状态检测实现。最终执行任务指定筛选命令，结果为 155 个通过、0 个失败、0 个跳过。
- 状态检测 API 与构造函数注入的测试先于相应生产实现写入；初次执行因缺少三参数构造函数、`GamePathStatus` 和 `IsGamePathReady` 而编译失败，随后实现使其通过。

## 修改文件

- `ViewModels/SetupWizardViewModel.cs`
- `tests/Cafe.Launcher.Avalonia.Tests/SetupWizardViewModelTests.cs`
- `tests/Cafe.Launcher.Avalonia.Tests/SetupWizardStepItemTests.cs`
- `tests/Cafe.Launcher.Avalonia.Tests/GameOperationsViewModelTests.cs`
- `tests/Cafe.Launcher.Avalonia.Tests/DialogsViewModelTests.cs`
- `tests/Cafe.Launcher.Avalonia.Tests/MainWindowViewModelTests.cs`

## 覆盖与实现摘要

- 注入并只调用 `LocalInstallationStateStore.ReadAsync(...)`；不修改服务注册、`LauncherSettings` 或游戏状态文件。
- 首次进入第二步时只在空路径场景填充默认目录；已有值和用户清空后再次进入均保持原值。
- 覆盖不存在、有效、损坏、文件占用、无法规范化、检查中与新路径取消旧读取的状态映射及继续条件。
- 本地化切换会通知 `GamePathStatusText`。

## 验证

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~SetupWizardViewModelTests|FullyQualifiedName~SetupWizardStepItemTests|FullyQualifiedName~GameOperationsViewModelTests|FullyQualifiedName~DialogsViewModelTests|FullyQualifiedName~MainWindowViewModelTests"
```

结果：155 通过，0 失败，0 跳过。

`git diff --check` 已执行，无空白错误。

## 自审

- `BuildSettings()` 未改动。
- 生产代码没有调用 `CommitAsync`、`DeleteAsync` 或任何文件写入 API。
- 状态写入受取消令牌、版本号与当前 `CancellationTokenSource` 三重校验保护。
- 未新增设置字段、服务注册或原版 `downloadPath` 访问。
