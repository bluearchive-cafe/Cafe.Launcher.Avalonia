# 设置向导确认页分隔列表实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将确认页摘要重构为垂直居中、具有明确行分隔的四项列表。

**Architecture:** 在既有 `info-strip` 内保留四个摘要 Grid 和既有绑定/命令，仅为 Grid 添加统一向导专属行类，并在前三行后插入向导专属分隔 Border。样式定义统一行高和垂直对齐，测试锁定布局结构与现有编辑导航行为。

**Tech Stack:** .NET 10、Avalonia、xUnit v3、Avalonia.Headless.XUnit。

## Global Constraints

- 保持语言、下载源、游戏安装路径、代理的顺序、值绑定、截断与四个 `GoToStepCommand` 参数 `0`、`2`、`1`、`3`。
- 保持既有 `AutomationProperties.Name="{Binding Shell.I18n.SetupWizardEditStep}"`。
- 摘要容器继续使用 `info-strip`；仅前三项后存在分隔线。
- 仅使用现有间距与设计令牌，不引入原始颜色或间距；不修改 ViewModel、设置、语言资源或服务。

---

### Task 1: 实现确认页分隔列表并锁定 XAML 样式契约

**Files:**
- Modify: `Views/SetupWizardOverlay.axaml:206-254`
- Modify: `Views/Styles/SetupWizard.axaml`
- Modify: `tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs`

**Interfaces:**
- Consumes: 现有四个摘要值绑定和 `GoToStepCommand`。
- Produces: `Grid.wizard-review-row` 与 `Border.wizard-review-divider`，供无头测试继续按按钮自动化名称定位。

- [ ] **Step 1: 编写失败的布局契约测试**

  在 `UiStyleContractTests.cs` 增加 `SetupWizard_Review_UsesSeparatedCenteredRows`。从确认步骤的 `info-strip` 中获取子元素，断言：

  ```csharp
  Assert.Equal(4, rows.Count);
  Assert.All(rows, row => Assert.True(HasClass(row, "wizard-review-row")));
  Assert.Equal(3, dividers.Count);
  Assert.All(dividers, divider => Assert.True(HasClass(divider, "wizard-review-divider")));
  ```

  读取 `Views/Styles/SetupWizard.axaml` 并断言 `Grid.wizard-review-row` 设置 `MinHeight`、`VerticalAlignment`，`Border.wizard-review-divider` 只使用已有 `LauncherCardBorderBrush` 与 `BorderThickness`。

- [ ] **Step 2: 运行契约测试确认 RED**

  ```powershell
  dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~UiStyleContractTests"
  ```

  预期：新增测试因当前四个 Grid 没有行类且没有分隔 Border 而失败。

- [ ] **Step 3: 添加最小 XAML 与样式**

  为四个摘要 Grid 都添加 `Classes="wizard-review-row"`。在前三个 Grid 后添加：

  ```xml
  <Border Classes="wizard-review-divider"/>
  ```

  在 `SetupWizard.axaml` 添加：

  ```xml
  <Style Selector="Grid.wizard-review-row">
      <Setter Property="MinHeight" Value="{StaticResource LauncherControlHeightDialog}"/>
      <Setter Property="VerticalAlignment" Value="Center"/>
  </Style>
  <Style Selector="Border.wizard-review-divider">
      <Setter Property="Height" Value="1"/>
      <Setter Property="Background" Value="{DynamicResource LauncherCardBorderBrush}"/>
  </Style>
  ```

  为每个标签、值和编辑按钮补充 `VerticalAlignment="Center"`，保持其余属性与命令完全不变。

- [ ] **Step 4: 运行契约测试确认 GREEN**

  运行相同命令，预期 `UiStyleContractTests` 全部通过。

- [ ] **Step 5: 提交**

  ```powershell
  git add Views/SetupWizardOverlay.axaml Views/Styles/SetupWizard.axaml tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs
  git commit -m "refactor(setup): 优化确认页摘要列表布局"
  ```

### Task 2: 验证编辑导航与完整回归

**Files:**
- Modify: `tests/Cafe.Launcher.Avalonia.HeadlessTests/MainWindowHeadlessTests.cs`

**Interfaces:**
- Consumes: 四个既有编辑按钮的本地化自动化名称和命令参数。
- Produces: 编辑按钮在分隔列表中仍能导航到对应步骤的回归覆盖。

- [ ] **Step 1: 编写失败的无头测试**

  新增 `SetupWizard_ReviewList_EditButtonsNavigateToTheirSteps`，显示向导并导航到确认页，按 `AutomationProperties.Name` 找到四个编辑按钮并依次点击，断言 `Step` 依次成为 `0`、`2`、`1`、`3`；每次返回确认页后继续下一项。

- [ ] **Step 2: 运行无头测试确认 RED**

  ```powershell
  dotnet test .\tests\Cafe.Launcher.Avalonia.HeadlessTests\Cafe.Launcher.Avalonia.HeadlessTests.csproj --filter "FullyQualifiedName~MainWindowHeadlessTests"
  ```

  预期：新增测试在分隔列表变更前尚不存在而失败。

- [ ] **Step 3: 仅使用既有编辑命令行为**

  不修改生产行为。使用 Avalonia UI 线程既有帮助器点击找到的按钮，并在每次点击后等待 Dispatcher 处理。

- [ ] **Step 4: 运行无头测试确认 GREEN**

  运行相同命令，预期 `MainWindowHeadlessTests` 全部通过。

- [ ] **Step 5: 完整验证与提交**

  ```powershell
  .\test.ps1
  .\build.ps1
  git add tests/Cafe.Launcher.Avalonia.HeadlessTests/MainWindowHeadlessTests.cs
  git commit -m "test(setup): 覆盖确认页分隔列表导航"
  ```
