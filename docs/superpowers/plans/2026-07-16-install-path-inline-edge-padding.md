# Install Path Inline Edge Padding Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 调整安装路径字段的尾部内边距，使“更改路径”按钮背景与字段右边框的留白接近其上下留白，同时保持左侧文字对齐和全部既有交互契约。

**Architecture:** 在 `App.axaml` 定义唯一的语义化复合 Thickness 令牌 `LauncherPathFieldPadding`，由 `Border.path-field` 样式引用；不在具体 View 中写原始间距，也不改变按钮样式。用静态契约测试锁定令牌及样式引用，用现有双尺寸 Headless 测试比较按钮相对字段的右、上、下留白，复现并防止视觉失衡回归。

**Tech Stack:** .NET 10、Avalonia XAML、xUnit v3、Avalonia.Headless.XUnit

## Global Constraints

- `LauncherPathFieldPadding` 必须是 `Thickness`，精确值为 `16,0,4,0`。
- `Border.path-field` 必须通过 `{StaticResource LauncherPathFieldPadding}` 使用复合内边距，不可继续直接写 `16,0` 或新的原始间距。
- 字段左侧保留 `16px`，右侧为 `4px`；不得改变 `LauncherFieldHeight`、边框、圆角或背景。
- 不得改变 `Button.icon-link` 的内容内边距、高度、圆角、悬停、按压或焦点状态。
- 不得改变安装路径行的 Grid 结构、`12px` 字段内操作间距、`8px` 字段外间距或三个按钮的位置。
- 保留三个按钮现有命令、`IsEnabled`、Tooltip、`AutomationProperties.Name` 和语义样式类。
- 不新增文案、本地化键、ViewModel 状态、设置字段、依赖、颜色或新的标量间距值。
- 默认 `1300×754` 和最小 `1024×640` 下，内嵌按钮右侧与任一垂直留白的差值不得超过 `4px`；既有无重叠、无裁切和窗口可达性断言继续通过。

---

### Task 1: 统一字段内按钮的边缘留白

**Files:**
- Modify: `tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs:767-810,979-1008`
- Modify: `tests/Cafe.Launcher.Avalonia.HeadlessTests/MainWindowHeadlessTests.cs:579-632`
- Modify: `App.axaml:8-22`
- Modify: `Views/MainWindow.Styles.axaml:431-438`

**Interfaces:**
- Consumes: `LauncherSpacingXs=4`、`LauncherSpacingLg=16` 的现有设计尺度，以及 `Border.path-field`、`Button.icon-link` 的现有样式契约。
- Produces: `Thickness LauncherPathFieldPadding = 16,0,4,0`，仅供 `Border.path-field.Padding` 使用；不产生新的 C# 接口。

- [ ] **Step 1: 写入失败的设计令牌与样式契约测试**

在 `DesignTokens_ContainExactSpacingRadiusIconAndControlHeightValues` 中，紧接 `LauncherSpacingSection` 断言后加入：

```csharp
        Assert.Equal("16,0,4,0", resources["LauncherPathFieldPadding"]);
        var pathFieldPadding = document
            .Descendants()
            .Single(element =>
                element.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "Key"
                    && attribute.Value == "LauncherPathFieldPadding"));
        Assert.Equal("Thickness", pathFieldPadding.Name.LocalName);
```

在 `InteractiveControlStyles_UseSharedFocusAndHeightTokens` 中，将现有路径字段高度断言改为先取得同一个 setter 字典，并补充 Padding 断言：

```csharp
        var pathField = GetStyleSetters(document, "Border.path-field");
        Assert.Equal(
            "{StaticResource LauncherFieldHeight}",
            pathField["Height"]);
        Assert.Equal(
            "{StaticResource LauncherPathFieldPadding}",
            pathField["Padding"]);
```

- [ ] **Step 2: 写入失败的 Headless 边缘留白回归断言**

在 `MainWindow_InstallPathRow_AtDefaultAndMinimumWindowSizes_KeepsInlinePathActionAndExternalActionsReachable` 中，将 `changePathTopLeft`、`pathTextTopLeft` 的赋值和该方法后方现有的两个 `Assert.NotNull` 合并为以下连续代码：

```csharp
        var changePathTopLeft = changePathButton.TranslatePoint(default, pathField);
        var pathTextTopLeft = pathText.TranslatePoint(default, pathField);

        Assert.NotNull(changePathTopLeft);
        Assert.NotNull(pathTextTopLeft);

        var changePathRightInset = pathField.Bounds.Width
            - (changePathTopLeft.Value.X + changePathButton.Bounds.Width);
        var changePathTopInset = changePathTopLeft.Value.Y;
        var changePathBottomInset = pathField.Bounds.Height
            - (changePathTopLeft.Value.Y + changePathButton.Bounds.Height);
```

在字段内按钮边界断言后加入：

```csharp
        Assert.InRange(Math.Abs(changePathRightInset - changePathTopInset), 0, 4);
        Assert.InRange(Math.Abs(changePathRightInset - changePathBottomInset), 0, 4);
```

- [ ] **Step 3: 运行聚焦测试并确认按预期失败**

依次运行，避免两个测试项目争用共享输出目录：

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~DesignTokens_ContainExactSpacingRadiusIconAndControlHeightValues|FullyQualifiedName~InteractiveControlStyles_UseSharedFocusAndHeightTokens"
dotnet test .\tests\Cafe.Launcher.Avalonia.HeadlessTests\Cafe.Launcher.Avalonia.HeadlessTests.csproj --filter "FullyQualifiedName~MainWindow_InstallPathRow_AtDefaultAndMinimumWindowSizes_KeepsInlinePathActionAndExternalActionsReachable"
```

预期：契约测试因 `LauncherPathFieldPadding` 尚不存在或 `Border.path-field.Padding` 仍为 `16,0` 而失败；Headless 理论测试的两个尺寸均因当前右侧留白约为 `16px`、与垂直留白差值超过 `4px` 而失败。失败必须来自目标行为缺失，不得是编译错误。

- [ ] **Step 4: 定义复合令牌并让路径字段样式引用它**

在 `App.axaml` 的现有 Thickness 令牌之后加入：

```xml
            <Thickness x:Key="LauncherPathFieldPadding">16,0,4,0</Thickness>
```

在 `Views/MainWindow.Styles.axaml` 的 `Border.path-field` 样式中，将：

```xml
        <Setter Property="Padding" Value="16,0"/>
```

替换为：

```xml
        <Setter Property="Padding" Value="{StaticResource LauncherPathFieldPadding}"/>
```

不要修改 `Views/MainWindow.axaml` 或 `Button.icon-link` 样式；本次修复只改变字段容器的右侧留白。

- [ ] **Step 5: 重新运行聚焦测试并确认通过**

运行：

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~DesignTokens_ContainExactSpacingRadiusIconAndControlHeightValues|FullyQualifiedName~InteractiveControlStyles_UseSharedFocusAndHeightTokens"
dotnet test .\tests\Cafe.Launcher.Avalonia.HeadlessTests\Cafe.Launcher.Avalonia.HeadlessTests.csproj --filter "FullyQualifiedName~MainWindow_InstallPathRow_AtDefaultAndMinimumWindowSizes_KeepsInlinePathActionAndExternalActionsReachable"
```

预期：契约测试 `2/2` 通过；Headless 理论测试 `2/2` 通过，两个尺寸下右侧与顶部、底部留白差值均不超过 `4px`。

- [ ] **Step 6: 运行完整 UI 回归验证**

运行：

```powershell
.\dev.ps1 ui
git diff --check
```

预期：`UiStyleContractTests` 与全部 Headless 测试通过，`git diff --check` 无输出；不需要运行本地化契约脚本，因为本次不修改 locale JSON。

- [ ] **Step 7: 进行真实桌面视觉检查**

运行当前 Debug 构建并在安装状态下检查默认与最小窗口尺寸：

```powershell
dotnet run --project .\Cafe.Launcher.Avalonia.csproj
```

验收项：

- 字段左侧标签仍与原位置对齐。
- “更改路径”的默认透明背景、悬停背景、按压状态和焦点环未改变。
- 按钮背景右侧留白在视觉上接近上下留白，不再出现截图中的大块右侧空隙。
- `1024×640` 下路径文本仍正常省略，三个按钮完整且互不重叠。

完成检查后正常关闭启动器。

- [ ] **Step 8: 提交聚焦改动**

```powershell
git add -- App.axaml Views/MainWindow.Styles.axaml tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs tests/Cafe.Launcher.Avalonia.HeadlessTests/MainWindowHeadlessTests.cs
git commit -m "fix(ui): 统一路径按钮边缘留白"
```

预期：提交成功且只包含上述四个文件。
