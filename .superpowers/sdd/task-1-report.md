# 任务 1 实施报告：定义可绑定的路径状态和本地化文案

## RED 证据

先在 `tests/Cafe.Launcher.Avalonia.Tests/LocalizationServiceTests.cs` 添加四种语言映射测试和语言切换更新测试，随后执行：

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~LocalizationServiceTests"
```

结果：失败（退出代码 1）。编译器报告新增的五个 `LocalizedStrings` 属性不存在，共 10 个 `CS1061` 错误：

- `SetupWizardGamePathAvailable`
- `SetupWizardGamePathChecking`
- `SetupWizardGamePathCorrupted`
- `SetupWizardGamePathInaccessible`
- `SetupWizardGamePathInstalled`

## GREEN 证据

添加枚举、四份语言资源和 `LocalizedStrings.Apply()` 映射后，重新执行相同命令：

```text
已通过! - 失败: 0，通过: 33，已跳过: 0，总计: 33
```

补充验证：

```powershell
dotnet build .\Cafe.Launcher.Avalonia.csproj --no-restore
git diff --check
```

结果：构建成功，0 个警告、0 个错误；`git diff --check` 成功。

## 修改文件

- 新建 `Features/SetupWizard/SetupWizardGamePathStatus.cs`：公开枚举，成员顺序为 `NotSelected`、`Checking`、`AvailableForInstallation`、`ValidInstallation`、`CorruptedInstallation`、`Inaccessible`。
- 修改 `Services/LocalizationService.cs`：为五个精确本地化键添加可观察属性并在 `Apply()` 中映射。
- 修改 `Assets/Locales/en.json`、`Assets/Locales/zh-Hans.json`、`Assets/Locales/zh-Hant.json`、`Assets/Locales/ja.json`：按序数顺序添加五个状态文案键。
- 修改 `tests/Cafe.Launcher.Avalonia.Tests/LocalizationServiceTests.cs`：覆盖四种语言映射及订阅 `LanguageChanged` 后的刷新。

## 自审

- 所有四份语言文件均由现有 `LocaleFiles_KeepKeysSortedOrdinal` 和键一致性测试覆盖；本次筛选测试已通过。
- 未修改设置字段、持久化格式、原版启动器文件或其他向导功能。
- 任务说明列出的 `ViewModels/LocalizedStrings.cs` 在仓库中不存在；经代码检索，`LocalizedStrings` 的唯一实际定义位于 `Services/LocalizationService.cs`，因此在该文件中完成要求的属性和映射。
- 独立规范审查仅指出上述路径与任务文档不一致；独立规格审查确认该实际文件修改为必要实现，未发现缺失、错误或范围蔓延。
