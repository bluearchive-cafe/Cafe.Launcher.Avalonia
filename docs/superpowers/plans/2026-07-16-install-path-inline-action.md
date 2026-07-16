# Install Path Inline Action Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将“更改路径”作为无分隔线的字段内附属操作放入安装路径框，同时保持“检测”和“安装游戏”为字段外部操作，并确保默认与最小窗口尺寸下布局可达。

**Architecture:** 仅调整安装面板现有 XAML 层级：外层操作行从四列收敛为路径字段、检测按钮、安装按钮三列；路径字段内部使用两层 Grid，分别保留标签与内容的 `8px` 间距，以及路径文本与“更改路径”按钮的 `12px` 间距。复用现有 `icon-link`、按钮状态、命令和可访问性绑定，不修改 ViewModel、样式资源、本地化或业务逻辑。

**Tech Stack:** .NET 10、Avalonia XAML、xUnit v3、Avalonia.Headless.XUnit

## Global Constraints

- `path-field` 内部顺序必须是路径标签、可省略路径文本、“更改路径”按钮。
- 路径框本身不可点击；只有独立 Button 执行 `Settings.ChangePersistedGamePathCommand`。
- “更改路径”与路径文本之间不显示分隔线，使用 `LauncherSpacingMd` 保留 `12px` 间距。
- “检测”保持在 `path-field` 外部，使用现有次要操作样式，并通过 `LauncherSpacingSm` 与字段保持 `8px` 间距。
- “安装游戏”保持最右侧主操作；“检测”和“安装游戏”不可被压缩或裁切。
- 窗口收窄时只能由路径文本通过现有 `CharacterEllipsis` 缩短。
- 保留三个按钮现有的命令、`IsEnabled`、Tooltip、`AutomationProperties.Name` 和语义样式类。
- 不新增文案、本地化键、ViewModel 状态、设置字段、依赖、原始颜色或新的间距值。
- 默认 `1300×754` 和最小 `1024×640` 下，路径字段及三个操作均须正尺寸、无重叠且位于窗口内。

---

### Task 1: 将更改路径操作内嵌到路径字段

**Files:**
- Modify: `tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs:64-115`
- Modify: `tests/Cafe.Launcher.Avalonia.HeadlessTests/MainWindowHeadlessTests.cs:579-610`
- Modify: `Views/MainWindow.axaml:308-342`

**Interfaces:**
- Consumes: `Settings.ChangePersistedGamePathCommand`、`Settings.SelectInstalledGameCommand`、`Operations.InstallOrUpdateCommand` 及现有本地化、忙碌状态与无障碍绑定。
- Produces: `path-field` 内唯一的“更改路径”Button，以及 `install-path-row` 下依次位于第 1、2 列的“检测”和“安装游戏”Button；不产生新的 C# 接口。

- [ ] **Step 1: 将 XAML 契约测试改为期望字段内附属操作**

在 `UiStyleContractTests.MainWindow_InstallPanel_AlignsPathWithPrimaryActionAndKeepsRefreshInStatusHeader` 中，用以下内容替换从 `var pathField` 到该测试最后一个断言的代码：

```csharp
        var pathField = pathRow
            .Elements()
            .Single(element =>
                element.Name.LocalName == "Border"
                && HasClass(element, "path-field"));
        var pathLayout = pathField
            .Elements()
            .Single(element => element.Name.LocalName == "Grid");
        var pathContent = pathLayout
            .Elements()
            .Single(element => element.Name.LocalName == "Grid");
        var changePathButton = pathContent
            .Elements()
            .Single(element =>
                element.Name.LocalName == "Button"
                && element.Attribute("Command")?.Value == "{Binding Settings.ChangePersistedGamePathCommand}");
        var pathButtons = pathRow
            .Elements()
            .Where(element => element.Name.LocalName == "Button")
            .ToArray();
        var detectButton = pathButtons
            .Single(element =>
                element.Attribute("Command")?.Value == "{Binding Settings.SelectInstalledGameCommand}");
        var installButton = pathButtons
            .Single(element =>
                element.Attribute("Command")?.Value == "{Binding Operations.InstallOrUpdateCommand}");

        Assert.Equal("1", refreshButton.Attribute("Grid.Column")?.Value);
        Assert.Equal("*,Auto,Auto", pathRow.Attribute("ColumnDefinitions")?.Value);
        Assert.Equal("{StaticResource LauncherSpacingSm}", pathRow.Attribute("ColumnSpacing")?.Value);
        Assert.Equal(2, pathButtons.Length);
        Assert.Equal("Auto,*", pathLayout.Attribute("ColumnDefinitions")?.Value);
        Assert.Equal("{StaticResource LauncherSpacingSm}", pathLayout.Attribute("ColumnSpacing")?.Value);
        Assert.Equal("*,Auto", pathContent.Attribute("ColumnDefinitions")?.Value);
        Assert.Equal("{StaticResource LauncherSpacingMd}", pathContent.Attribute("ColumnSpacing")?.Value);
        Assert.Equal("1", changePathButton.Attribute("Grid.Column")?.Value);
        Assert.Empty(pathField.Descendants().Where(element => element.Name.LocalName == "Border"));
        Assert.Equal("1", detectButton.Attribute("Grid.Column")?.Value);
        Assert.Equal("2", installButton.Attribute("Grid.Column")?.Value);
        Assert.True(HasClass(installButton, "primary-operation"));
        Assert.True(HasClass(installButton, "path-operation"));
        Assert.DoesNotContain(
            installLayout.DescendantsAndSelf().Attributes(),
            attribute => attribute.Name.LocalName == "Margin"
                && !attribute.Value.StartsWith("{StaticResource Launcher", StringComparison.Ordinal));
        Assert.DoesNotContain(
            actions.Descendants(),
            element => element.Name.LocalName == "Button");
```

该测试继续与 `MainWindow_OperationButtons_ExposeLocalizedNamesAndActionPriority` 共同锁定命令、Tooltip、自动化名称和操作优先级，避免在布局测试中重复相同契约。

- [ ] **Step 2: 将 Headless 测试改为检查一个字段内按钮和两个字段外按钮**

用以下测试替换现有 `MainWindow_InstallPathRow_AtDefaultAndMinimumWindowSizes_KeepsPathFieldSeparateFromActions`：

```csharp
    [AvaloniaTheory]
    [InlineData(1300, 754)]
    [InlineData(1024, 640)]
    public void MainWindow_InstallPathRow_AtDefaultAndMinimumWindowSizes_KeepsInlinePathActionAndExternalActionsReachable(
        double width,
        double height)
    {
        using var context = CreateContext();
        context.Window.Width = width;
        context.Window.Height = height;
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
        var changePathButton = pathField
            .GetVisualDescendants()
            .OfType<Button>()
            .Single(control => control.Classes.Contains("secondary-operation"));
        var pathText = pathField
            .GetVisualDescendants()
            .OfType<TextBlock>()
            .Single(control => control.Classes.Contains("caption"));
        var externalActions = pathRow.Children
            .OfType<Button>()
            .OrderBy(control => control.Bounds.Left)
            .ToArray();
        var changePathTopLeft = changePathButton.TranslatePoint(default, pathField);
        var pathTextTopLeft = pathText.TranslatePoint(default, pathField);

        Assert.Equal(2, externalActions.Length);
        Assert.True(pathField.Bounds.Width > 0);
        Assert.True(pathField.Bounds.Right <= externalActions[0].Bounds.Left);
        Assert.NotNull(changePathTopLeft);
        Assert.NotNull(pathTextTopLeft);
        Assert.True(pathTextTopLeft.Value.X + pathText.Bounds.Width <= changePathTopLeft.Value.X);
        Assert.True(changePathTopLeft.Value.X >= 0);
        Assert.True(changePathTopLeft.Value.X + changePathButton.Bounds.Width <= pathField.Bounds.Width);
        Assert.True(changePathTopLeft.Value.Y >= 0);
        Assert.True(changePathTopLeft.Value.Y + changePathButton.Bounds.Height <= pathField.Bounds.Height);
        Assert.True(externalActions[0].Classes.Contains("secondary-operation"));
        Assert.True(externalActions[1].Classes.Contains("primary-operation"));
        AssertControlInsideWindow(pathField, context.Window);
        AssertControlInsideWindow(changePathButton, context.Window);
        Assert.All(externalActions, action => AssertControlInsideWindow(action, context.Window));
    }
```

- [ ] **Step 3: 运行两个聚焦测试并确认它们先失败**

运行：

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~MainWindow_InstallPanel_AlignsPathWithPrimaryActionAndKeepsRefreshInStatusHeader"
dotnet test .\tests\Cafe.Launcher.Avalonia.HeadlessTests\Cafe.Launcher.Avalonia.HeadlessTests.csproj --filter "FullyQualifiedName~MainWindow_InstallPathRow_AtDefaultAndMinimumWindowSizes_KeepsInlinePathActionAndExternalActionsReachable"
```

预期：第一个测试因当前列定义仍为 `*,Auto,Auto,Auto` 或字段中不存在 Button 而失败；第二个测试因当前 `path-field` 中不存在 `secondary-operation` Button 而失败。

- [ ] **Step 4: 用最小 XAML 改动实现字段内附属操作**

在 `Views/MainWindow.axaml` 中，用以下代码替换完整的 `install-path-row` Grid：

```xml
                            <Grid Grid.Row="2" Grid.Column="1" Classes="install-path-row" ColumnDefinitions="*,Auto,Auto" ColumnSpacing="{StaticResource LauncherSpacingSm}">
                                <Border Classes="path-field">
                                    <Grid ColumnDefinitions="Auto,*" ColumnSpacing="{StaticResource LauncherSpacingSm}">
                                        <TextBlock Text="{Binding Shell.I18n.Path}" Classes="value" VerticalAlignment="Center"/>
                                        <Grid Grid.Column="1" ColumnDefinitions="*,Auto" ColumnSpacing="{StaticResource LauncherSpacingMd}">
                                            <TextBlock Text="{Binding Shell.PathText}" Classes="caption" VerticalAlignment="Center" TextTrimming="CharacterEllipsis"/>
                                            <Button Grid.Column="1" Classes="icon-link secondary-operation" Command="{Binding Settings.ChangePersistedGamePathCommand}" VerticalAlignment="Center"
                                                    IsEnabled="{Binding Shell.IsBusy, Converter={x:Static BoolConverters.Not}}"
                                                    ToolTip.Tip="{Binding Shell.I18n.ChangePath}"
                                                    AutomationProperties.Name="{Binding Shell.I18n.ChangePath}">
                                                <StackPanel Classes="button-content">
                                                    <materialIcons:MaterialIcon Kind="FolderOpen" Width="{StaticResource LauncherIconSm}" Height="{StaticResource LauncherIconSm}"/>
                                                    <TextBlock Text="{Binding Shell.I18n.ChangePath}"/>
                                                </StackPanel>
                                            </Button>
                                        </Grid>
                                    </Grid>
                                </Border>
                                <Button Grid.Column="1" Classes="icon-link secondary-operation" Command="{Binding Settings.SelectInstalledGameCommand}" VerticalAlignment="Center"
                                        IsEnabled="{Binding Shell.IsBusy, Converter={x:Static BoolConverters.Not}}"
                                        ToolTip.Tip="{Binding Shell.I18n.SelectInstalledGame}"
                                        AutomationProperties.Name="{Binding Shell.I18n.SelectInstalledGame}">
                                    <StackPanel Classes="button-content">
                                        <materialIcons:MaterialIcon Kind="Magnify" Width="{StaticResource LauncherIconSm}" Height="{StaticResource LauncherIconSm}"/>
                                        <TextBlock Text="{Binding Shell.I18n.SelectInstalledGame}"/>
                                    </StackPanel>
                                </Button>
                                <Button Grid.Column="2" Classes="primary-action bottom-action path-operation primary-operation" Command="{Binding Operations.InstallOrUpdateCommand}" ToolTip.Tip="{Binding Operations.InstallButtonText}"
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

不要修改 `Views/MainWindow.Styles.axaml`：现有 `Button.icon-link` 已提供透明默认背景、悬停背景、按压状态和圆角；共享 `Button:focus-visible` 已提供焦点环。嵌套 Grid 的 `ColumnSpacing` 已表达层级，无需新增 Border 或分隔线样式。

- [ ] **Step 5: 重新运行聚焦测试并确认通过**

运行：

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~MainWindow_InstallPanel_AlignsPathWithPrimaryActionAndKeepsRefreshInStatusHeader"
dotnet test .\tests\Cafe.Launcher.Avalonia.HeadlessTests\Cafe.Launcher.Avalonia.HeadlessTests.csproj --filter "FullyQualifiedName~MainWindow_InstallPathRow_AtDefaultAndMinimumWindowSizes_KeepsInlinePathActionAndExternalActionsReachable"
```

预期：两个命令均退出码为 `0`，对应测试全部通过。

- [ ] **Step 6: 运行完整 UI 回归验证**

运行：

```powershell
.\dev.ps1 ui
git diff --check
```

预期：`UiStyleContractTests` 和全部 Headless 测试通过，`git diff --check` 无输出。验证结果应同时覆盖默认 `1300×754` 和最小 `1024×640` 两组尺寸。

- [ ] **Step 7: 进行一次可见布局检查**

运行：

```powershell
dotnet run --project .\Cafe.Launcher.Avalonia.csproj
```

预期：启动器打开后，安装状态下的底部操作行符合以下检查项：

- 路径字段内依次显示标签、可省略路径和“更改路径”。
- 路径文本与“更改路径”之间没有分隔线，按钮默认透明，悬停时出现现有轻量背景。
- “检测”位于路径框外部，“安装游戏”仍是最右侧主按钮。
- 将窗口调整到 `1024×640` 时，只有路径文本缩短，三个按钮仍完整可见且互不重叠。

完成检查后正常关闭启动器。

- [ ] **Step 8: 提交聚焦改动**

```powershell
git add -- Views/MainWindow.axaml tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs tests/Cafe.Launcher.Avalonia.HeadlessTests/MainWindowHeadlessTests.cs
git commit -m "fix(ui): 将更改路径操作内嵌到路径字段"
```

预期：提交成功，提交只包含上述三个文件。
