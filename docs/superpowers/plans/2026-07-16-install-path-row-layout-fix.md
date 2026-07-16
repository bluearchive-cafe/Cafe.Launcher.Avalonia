# Install Path Row Layout Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让未安装面板的路径输入框只占用路径区域，并让“更改路径”“检测”“安装游戏”三个按钮在边框外保持同一操作行。

**Architecture:** 仅重排 `MainWindow.axaml` 的安装路径行：新增一个带 `install-path-row` 类的四列 Grid，第一列为自适应路径框，后三列为按钮。通过 XAML 契约测试锁定层级，通过 headless 测试锁定最小窗口宽度下不重叠和可达性。

**Tech Stack:** .NET 10、Avalonia XAML、xUnit v3、Avalonia.Headless.XUnit。

## Global Constraints

- 仅修改未安装面板布局及其聚焦测试，不改变命令、绑定、文案、ViewModel 或远程契约。
- 路径框使用剩余宽度，禁止添加固定宽度。
- 间距复用 `LauncherSpacingSm`，不新增裸颜色、间距、圆角或依赖。
- 保留现有 Tooltip、AutomationProperties、`primary-operation` 和 `secondary-operation` 样式类。

---

### Task 1: 分离路径框与安装操作按钮

**Files:**
- Modify: `Views/MainWindow.axaml:307-345`
- Modify: `tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs:67-103`
- Modify: `tests/Cafe.Launcher.Avalonia.HeadlessTests/MainWindowHeadlessTests.cs:525-578`

**Interfaces:**
- Consumes: 现有 `Settings.ChangePersistedGamePathCommand`、`Settings.SelectInstalledGameCommand`、`Operations.InstallOrUpdateCommand` 及其绑定和样式类。
- Produces: `Grid.install-path-row`，直接包含一个 `Border.path-field` 和三个操作按钮；`path-field` 内只包含路径标签与路径文本。

- [ ] **Step 1: 写入失败的 XAML 契约测试**

将 `MainWindow_InstallPanel_AlignsPathWithPrimaryActionAndKeepsRefreshInStatusHeader` 中路径与安装按钮断言改为：

```csharp
var pathRow = status
    .Descendants()
    .Single(element =>
        element.Name.LocalName == "Grid"
        && HasClass(element, "install-path-row"));
var pathField = pathRow
    .Elements()
    .Single(element =>
        element.Name.LocalName == "Border"
        && HasClass(element, "path-field"));
var installButton = pathRow
    .Elements()
    .Single(element =>
        element.Name.LocalName == "Button"
        && element.Attribute("Command")?.Value == "{Binding Operations.InstallOrUpdateCommand}");

Assert.Equal("*,Auto,Auto,Auto", pathRow.Attribute("ColumnDefinitions")?.Value);
Assert.Empty(pathField.Descendants().Where(element => element.Name.LocalName == "Button"));
Assert.Equal("3", installButton.Attribute("Grid.Column")?.Value);
Assert.True(HasClass(installButton, "primary-operation"));
Assert.True(HasClass(installButton, "path-operation"));
```

- [ ] **Step 2: 写入失败的最小窗口 headless 测试**

新增用例：

```csharp
[AvaloniaFact]
public void MainWindow_InstallPathRow_AtMinimumWindowWidth_KeepsPathFieldSeparateFromActions()
{
    using var context = CreateContext();
    context.Window.Width = 1024;
    context.Window.Height = 640;
    context.ViewModel.Operations.PanelMode = GameOperationPanelMode.Install;
    context.Window.Show();
    Dispatcher.UIThread.RunJobs();

    var pathRow = context.Window
        .GetVisualDescendants()
        .OfType<Grid>()
        .Single(control => control.Classes.Contains("install-path-row"));
    var pathField = pathRow.Children
        .OfType<Border>()
        .Single(control => control.Classes.Contains("path-field"));
    var actions = pathRow.Children
        .OfType<Button>()
        .OrderBy(control => control.Bounds.Left)
        .ToArray();

    Assert.Equal(3, actions.Length);
    Assert.True(pathField.Bounds.Width > 0);
    Assert.True(pathField.Bounds.Right <= actions[0].Bounds.Left);
    AssertControlInsideWindow(pathField, context.Window);
    Assert.All(actions, action => AssertControlInsideWindow(action, context.Window));
}
```

- [ ] **Step 3: 运行聚焦测试，确认因缺少外层路径行而失败**

Run:

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~MainWindow_InstallPanel_AlignsPathWithPrimaryActionAndKeepsRefreshInStatusHeader"
dotnet test .\tests\Cafe.Launcher.Avalonia.HeadlessTests\Cafe.Launcher.Avalonia.HeadlessTests.csproj -c Debug --no-restore --filter "FullyQualifiedName~MainWindow_InstallPathRow_AtMinimumWindowWidth_KeepsPathFieldSeparateFromActions"
```

Expected: 两个用例均 FAIL，因为当前 XAML 中不存在 `Grid.install-path-row`，且安装按钮仍位于 `Border.path-field` 内。

- [ ] **Step 4: 最小化重排安装路径行**

将安装面板第三行替换为：

```xml
<Grid Grid.Row="2"
      Grid.Column="1"
      Classes="install-path-row"
      ColumnDefinitions="*,Auto,Auto,Auto"
      ColumnSpacing="{StaticResource LauncherSpacingSm}">
    <Border Classes="path-field">
        <Grid ColumnDefinitions="Auto,*" ColumnSpacing="{StaticResource LauncherSpacingSm}">
            <TextBlock Text="{Binding Shell.I18n.Path}" Classes="value" VerticalAlignment="Center"/>
            <TextBlock Grid.Column="1" Text="{Binding Shell.PathText}" Classes="caption"
                       VerticalAlignment="Center" TextTrimming="CharacterEllipsis"/>
        </Grid>
    </Border>
    <Button Grid.Column="1" Classes="icon-link secondary-operation"
            Command="{Binding Settings.ChangePersistedGamePathCommand}" VerticalAlignment="Center"
            IsEnabled="{Binding Shell.IsBusy, Converter={x:Static BoolConverters.Not}}"
            ToolTip.Tip="{Binding Shell.I18n.ChangePath}"
            AutomationProperties.Name="{Binding Shell.I18n.ChangePath}">
        <StackPanel Classes="button-content">
            <materialIcons:MaterialIcon Kind="FolderOpen" Width="{StaticResource LauncherIconSm}" Height="{StaticResource LauncherIconSm}"/>
            <TextBlock Text="{Binding Shell.I18n.ChangePath}"/>
        </StackPanel>
    </Button>
    <Button Grid.Column="2" Classes="icon-link secondary-operation"
            Command="{Binding Settings.SelectInstalledGameCommand}" VerticalAlignment="Center"
            IsEnabled="{Binding Shell.IsBusy, Converter={x:Static BoolConverters.Not}}"
            ToolTip.Tip="{Binding Shell.I18n.SelectInstalledGame}"
            AutomationProperties.Name="{Binding Shell.I18n.SelectInstalledGame}">
        <StackPanel Classes="button-content">
            <materialIcons:MaterialIcon Kind="Magnify" Width="{StaticResource LauncherIconSm}" Height="{StaticResource LauncherIconSm}"/>
            <TextBlock Text="{Binding Shell.I18n.SelectInstalledGame}"/>
        </StackPanel>
    </Button>
    <Button Grid.Column="3" Classes="primary-action bottom-action path-operation primary-operation"
            Command="{Binding Operations.InstallOrUpdateCommand}"
            ToolTip.Tip="{Binding Operations.InstallButtonText}"
            IsEnabled="{Binding Shell.IsBusy, Converter={x:Static BoolConverters.Not}}"
            AutomationProperties.Name="{Binding Operations.InstallButtonText}"
            HorizontalAlignment="Right">
        <StackPanel Classes="button-content">
            <materialIcons:MaterialIcon Kind="Download" Width="{StaticResource LauncherIconXl}" Height="{StaticResource LauncherIconXl}"/>
            <TextBlock Text="{Binding Operations.InstallButtonText}"/>
        </StackPanel>
    </Button>
</Grid>
```

- [ ] **Step 5: 运行聚焦测试，确认修复通过**

Run:

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~MainWindow_InstallPanel_AlignsPathWithPrimaryActionAndKeepsRefreshInStatusHeader"
dotnet test .\tests\Cafe.Launcher.Avalonia.HeadlessTests\Cafe.Launcher.Avalonia.HeadlessTests.csproj -c Debug --no-restore --filter "FullyQualifiedName~MainWindow_InstallPathRow_AtMinimumWindowWidth_KeepsPathFieldSeparateFromActions"
```

Expected: 两个用例均 PASS；路径框具有正宽度，右边界不超过第一个操作按钮左边界。

- [ ] **Step 6: 运行完整 UI 验证**

Run:

```powershell
.\dev.ps1 ui
git diff --check
```

Expected: `UiStyleContractTests` 与全部 headless 测试通过，`git diff --check` 无错误。

- [ ] **Step 7: 提交修复**

```powershell
git add -- Views/MainWindow.axaml tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs tests/Cafe.Launcher.Avalonia.HeadlessTests/MainWindowHeadlessTests.cs docs/superpowers/plans/2026-07-16-install-path-row-layout-fix.md
git commit -m "fix(ui): 修正安装路径输入框宽度"
```
