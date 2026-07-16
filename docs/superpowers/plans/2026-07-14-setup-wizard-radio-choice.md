# 设置向导单选组件实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将设置向导的下载源和代理选择改为语义明确、可键盘操作的 RadioButton 单选列表。

**Architecture:** 仅替换 `SetupWizardOverlay.axaml` 的五个选择控件，并直接双向绑定既有布尔状态属性。每个分组由明确的 `GroupName` 约束互斥关系；现有 ViewModel、设置保存和本地化资源保持不变。XAML 契约和无头测试验证控件语义、分组及既有状态同步。

**Tech Stack:** .NET 10、Avalonia、xUnit v3、Avalonia.Headless.XUnit。

## Global Constraints

- 只修改下载源与代理的向导选择控件；不得新增设置字段、服务、本地化键或原版启动器访问。
- 下载源组使用 `GroupName="SetupWizardDownloadSource"`；代理组使用 `GroupName="SetupWizardProxy"`。
- `IsChecked` 双向绑定既有 ViewModel 属性：`IsPatchUrlGroupCafe`、`IsPatchUrlGroupOfficial`、`IsProxyAuto`、`IsProxyDirect`、`IsProxySystem`。
- 保持既有本地化 `AutomationProperties.Name`、标题和说明文案。
- 删除这五个选项对 `wizard-choice`、`active` 类及选择命令的依赖；不删除可能被其他向导控件复用的样式定义。
- XAML 仅使用现有 `LauncherSpacing*` 资源和设计令牌；不得引入原始颜色或原始间距值。

---

### Task 1: 替换向导选择控件并更新样式契约

**Files:**
- Modify: `Views/SetupWizardOverlay.axaml:144-201`
- Modify: `tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs`

**Interfaces:**
- Consumes: `Dialogs.SetupWizard.IsPatchUrlGroupCafe`, `IsPatchUrlGroupOfficial`, `IsProxyAuto`, `IsProxyDirect`, `IsProxySystem`。
- Produces: 两个独立的 RadioButton 分组，供无头 UI 测试按控件类型和自动化名称定位。

- [ ] **Step 1: 编写失败的 XAML 契约测试**

  在 `UiStyleContractTests.cs` 新增 `SetupWizard_ChoiceSteps_UseGroupedRadioButtons`，读取 `SetupWizardOverlay.axaml` 并断言：

  ```csharp
  Assert.Equal(2, Regex.Matches(xaml, "GroupName=\"SetupWizardDownloadSource\"").Count);
  Assert.Equal(3, Regex.Matches(xaml, "GroupName=\"SetupWizardProxy\"").Count);
  Assert.DoesNotContain("Classes=\"wizard-choice\"", choiceSection);
  Assert.DoesNotContain("Classes.active=", choiceSection);
  ```

  同时断言五个 `RadioButton` 使用精确的 `IsChecked="{Binding Dialogs.SetupWizard.<property>, Mode=TwoWay}"` 和既有 `AutomationProperties.Name` 绑定。

- [ ] **Step 2: 运行契约测试确认 RED**

  运行：

  ```powershell
  dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~UiStyleContractTests"
  ```

  预期：新增测试因当前控件仍是 `Button`、没有 `RadioButton`/`GroupName` 而失败。

- [ ] **Step 3: 以最小 XAML 替换五个 Button**

  在下载源步骤使用下列结构，并对官方源使用 `IsPatchUrlGroupOfficial` 与 `DownloadSourceOfficial`：

  ```xml
  <RadioButton GroupName="SetupWizardDownloadSource"
               IsChecked="{Binding Dialogs.SetupWizard.IsPatchUrlGroupCafe, Mode=TwoWay}"
               AutomationProperties.Name="{Binding Shell.I18n.DownloadSourceCafe}">
      <StackPanel>
          <TextBlock Text="{Binding Shell.I18n.DownloadSourceCafe}" Classes="section-title"/>
          <TextBlock Text="{Binding Shell.I18n.SetupWizardDownloadSourceCafeDescription}" Classes="caption"/>
      </StackPanel>
  </RadioButton>
  ```

  在代理步骤使用相同结构，所有三个 RadioButton 设 `GroupName="SetupWizardProxy"`，分别绑定 `IsProxyAuto`、`IsProxyDirect`、`IsProxySystem` 与现有相应本地化标题和说明。移除五个 `Command`、`Classes="wizard-choice"` 和 `Classes.active` 属性；保留外层 `StackPanel` 的既有 `LauncherSpacingSm` 间距。

- [ ] **Step 4: 运行契约测试确认 GREEN**

  运行同一命令，预期：`UiStyleContractTests` 全部通过。

- [ ] **Step 5: 提交任务**

  ```powershell
  git add Views/SetupWizardOverlay.axaml tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs
  git commit -m "refactor(setup): 使用单选控件选择下载源和代理"
  ```

### Task 2: 添加无头 UI 单选行为回归

**Files:**
- Modify: `tests/Cafe.Launcher.Avalonia.HeadlessTests/MainWindowHeadlessTests.cs`

**Interfaces:**
- Consumes: Task 1 产生的 `RadioButton`，按既有本地化 `AutomationProperties.Name` 定位。
- Produces: 下载源和代理各自互斥、互不干扰、与 ViewModel 设置状态同步的回归证据。

- [ ] **Step 1: 编写失败的无头测试**

  在现有设置向导无头测试旁新增 `SetupWizard_RadioChoices_KeepGroupsIndependent`。显示向导后进入下载源步骤，按 `AutomationProperties.Name` 找到 Cafe 与官方 RadioButton；进入代理步骤后找到自动、直连、系统代理 RadioButton。断言初始选择与 ViewModel 一致，并执行：

  ```csharp
  officialRadioButton.IsChecked = true;
  Assert.True(context.ViewModel.Dialogs.SetupWizard.IsPatchUrlGroupOfficial);
  Assert.False(context.ViewModel.Dialogs.SetupWizard.IsPatchUrlGroupCafe);

  directRadioButton.IsChecked = true;
  Assert.True(context.ViewModel.Dialogs.SetupWizard.IsProxyDirect);
  Assert.True(context.ViewModel.Dialogs.SetupWizard.IsPatchUrlGroupOfficial);
  ```

  再选 `systemRadioButton`，断言 `IsProxySystem` 为真而 `IsProxyAuto`、`IsProxyDirect` 为假。

- [ ] **Step 2: 运行无头测试确认 RED**

  运行：

  ```powershell
  dotnet test .\tests\Cafe.Launcher.Avalonia.HeadlessTests\Cafe.Launcher.Avalonia.HeadlessTests.csproj --filter "FullyQualifiedName~MainWindowHeadlessTests"
  ```

  预期：新增测试因当前视觉树没有 `RadioButton` 而失败。

- [ ] **Step 3: 只在测试中使用 Task 1 的 RadioButton 语义**

  不修改生产代码。以 Avalonia UI 线程既有测试帮助器设置 `IsChecked`，并在每次状态变更后等待 Dispatcher 空闲，确保双向绑定已写入 `SetupWizardViewModel`。

- [ ] **Step 4: 运行无头测试确认 GREEN**

  运行同一命令，预期：`MainWindowHeadlessTests` 全部通过。

- [ ] **Step 5: 提交任务**

  ```powershell
  git add tests/Cafe.Launcher.Avalonia.HeadlessTests/MainWindowHeadlessTests.cs
  git commit -m "test(setup): 覆盖向导单选组件行为"
  ```

### Task 3: 完整回归与范围复核

**Files:** 无新增文件；仅在前两项发现失败时作最小修正。

- [ ] **Step 1: 复核变更范围**

  运行：

  ```powershell
  rg -n -S "wizard-choice|Classes\.active|Select(CafeDownloadSource|OfficialDownloadSource|ProxyAuto|ProxyDirect|ProxySystem)Command" Views\SetupWizardOverlay.axaml
  ```

  预期：下载源和代理段落不再包含这些引用；其他未关联向导段落不受影响。

- [ ] **Step 2: 运行完整验证**

  ```powershell
  .\test.ps1
  .\build.ps1
  ```

  预期：所有测试通过，Debug 构建为 0 警告、0 错误。

- [ ] **Step 3: 提交必要修正**

  若验证发现并修复范围内问题：

  ```powershell
  git add Views/SetupWizardOverlay.axaml tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs tests/Cafe.Launcher.Avalonia.HeadlessTests/MainWindowHeadlessTests.cs
  git commit -m "test(setup): 验证向导单选组件回归"
  ```
