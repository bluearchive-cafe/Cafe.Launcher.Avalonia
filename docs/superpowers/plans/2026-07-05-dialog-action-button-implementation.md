# Dialog Action Button Normalization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make every in-scope dialog action button use the approved 42px height, 108px minimum width, 16px icons, and consistent semantic styles.

**Architecture:** Keep semantic color and interaction behavior in `flat-action`, `primary-action`, and `danger-action`. Make `dialog-action` the sole owner of dialog action metrics, and enforce the separation through source-level XAML contract tests in a dedicated test file.

**Tech Stack:** .NET 10, Avalonia 12 XAML, xUnit 2.9.3, LINQ to XML

---

## File Structure

- Create `tests/Cafe.Launcher.Avalonia.Tests/DialogActionButtonContractTests.cs`: focused style and markup contracts for dialog action buttons.
- Modify `Views/MainWindow.Styles.axaml`: make `dialog-action` own all approved metrics and remove `settings-footer-action`.
- Modify `Views/MainWindowSettingsOverlay.axaml`: apply `dialog-action` and 16px icons to settings footer buttons.
- Modify `Views/MainWindowDialogsOverlay.axaml`: normalize the notice and crash-recovery action buttons.

### Task 1: Make `dialog-action` the sole metric owner

**Files:**
- Create: `tests/Cafe.Launcher.Avalonia.Tests/DialogActionButtonContractTests.cs`
- Modify: `Views/MainWindow.Styles.axaml:275-294`

- [ ] **Step 1: Write the failing style contract**

Create `DialogActionButtonContractTests.cs` with:

```csharp
using System.Xml.Linq;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class DialogActionButtonContractTests
{
    [Fact]
    public void DialogActionStyle_UsesUnifiedMetrics()
    {
        var document = XDocument.Load(ProjectFile("Views/MainWindow.Styles.axaml"));
        var setters = GetStyleSetters(document, "Button.dialog-action");

        Assert.Equal(
            "{StaticResource LauncherControlHeightDialog}",
            setters["Height"]);
        Assert.Equal("108", setters["MinWidth"]);
        Assert.Equal("16,0", setters["Padding"]);
        Assert.Equal("14", setters["FontSize"]);
        Assert.Equal("SemiBold", setters["FontWeight"]);
        Assert.DoesNotContain(
            document.Descendants()
                .Where(element => element.Name.LocalName == "Style")
                .Select(element => element.Attribute("Selector")?.Value),
            selector => selector == "Button.settings-footer-action");
    }

    private static IReadOnlyDictionary<string, string> GetStyleSetters(
        XDocument document,
        string selector) =>
        document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Style"
                && element.Attribute("Selector")?.Value == selector)
            .Elements()
            .Where(element => element.Name.LocalName == "Setter")
            .ToDictionary(
                element => element.Attribute("Property")?.Value
                    ?? throw new InvalidOperationException(
                        $"Setter in {selector} has no Property."),
                element => element.Attribute("Value")?.Value
                    ?? throw new InvalidOperationException(
                        $"Setter in {selector} has no Value."),
                StringComparer.Ordinal);

    private static string ProjectFile(string relativePath) =>
        Path.Combine(
            FindProjectRoot(),
            relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(
                directory.FullName,
                "Cafe.Launcher.Avalonia.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Cafe.Launcher.Avalonia.csproj was not found.");
    }
}
```

- [ ] **Step 2: Run the style contract and verify RED**

Run:

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~DialogActionButtonContractTests.DialogActionStyle_UsesUnifiedMetrics"
```

Expected: FAIL because `Button.dialog-action` has `MinHeight` instead of `Height`, lacks `FontWeight`, and `Button.settings-footer-action` still exists.

- [ ] **Step 3: Implement the unified style**

In `Views/MainWindow.Styles.axaml`, replace the two metric styles with:

```xml
<Style Selector="Button.dialog-action">
    <Setter Property="Height" Value="{StaticResource LauncherControlHeightDialog}"/>
    <Setter Property="MinWidth" Value="108"/>
    <Setter Property="Padding" Value="16,0"/>
    <Setter Property="FontSize" Value="14"/>
    <Setter Property="FontWeight" Value="SemiBold"/>
</Style>
```

Delete the entire `Button.settings-footer-action` style. Keep `Button.primary-action.settings-footer-action:disabled` temporarily; Task 2 replaces its selector before removing the last markup use.

- [ ] **Step 4: Run the style contract and verify GREEN**

Run the command from Step 2.

Expected: PASS.

- [ ] **Step 5: Commit the style contract and style change**

```powershell
git add -- tests/Cafe.Launcher.Avalonia.Tests/DialogActionButtonContractTests.cs Views/MainWindow.Styles.axaml
git commit -m "refactor(ui): 统一对话框操作按钮尺寸"
```

### Task 2: Normalize every in-scope dialog action

**Files:**
- Modify: `tests/Cafe.Launcher.Avalonia.Tests/DialogActionButtonContractTests.cs`
- Modify: `Views/MainWindowSettingsOverlay.axaml:121-138`
- Modify: `Views/MainWindowDialogsOverlay.axaml:122-127`
- Modify: `Views/MainWindowDialogsOverlay.axaml:473-477`
- Modify: `Views/MainWindow.Styles.axaml:288`

- [ ] **Step 1: Write the failing markup contract**

Add this test and helper methods to `DialogActionButtonContractTests`:

```csharp
[Fact]
public void DialogActionButtons_UseUnifiedClassAndIconSize()
{
    var relativePaths = new[]
    {
        "Views/MainWindowDialogsOverlay.axaml",
        "Views/MainWindowLogViewerOverlay.axaml",
        "Views/MainWindowSettingsOverlay.axaml"
    };
    var actionButtons = relativePaths
        .Select(ProjectFile)
        .Select(XDocument.Load)
        .SelectMany(document => document.Descendants())
        .Where(element => element.Name.LocalName == "Button")
        .Where(element => HasAnyClass(
            element,
            "flat-action",
            "primary-action",
            "danger-action"))
        .ToArray();

    Assert.Equal(25, actionButtons.Length);
    Assert.All(actionButtons, button =>
    {
        Assert.True(HasClass(button, "dialog-action"));
        Assert.Null(button.Attribute("Height"));
        Assert.Null(button.Attribute("Width"));
        Assert.All(
            button.Descendants()
                .Where(element => element.Name.LocalName == "MaterialIcon"),
            icon =>
            {
                Assert.Equal(
                    "{StaticResource LauncherIconSm}",
                    icon.Attribute("Width")?.Value);
                Assert.Equal(
                    "{StaticResource LauncherIconSm}",
                    icon.Attribute("Height")?.Value);
            });
    });
}

private static bool HasAnyClass(XElement element, params string[] classNames) =>
    classNames.Any(className => HasClass(element, className));

private static bool HasClass(XElement element, string className) =>
    element.Attribute("Classes")?.Value
        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Contains(className, StringComparer.Ordinal) == true;
```

- [ ] **Step 2: Run the markup contract and verify RED**

Run:

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~DialogActionButtonContractTests.DialogActionButtons_UseUnifiedClassAndIconSize"
```

Expected: FAIL because the notice button lacks `dialog-action` and has a local width, the two settings footer buttons use `settings-footer-action`, and the crash-recovery continue button has a local height and `LauncherIconMd`.

- [ ] **Step 3: Normalize the notice button**

In `Views/MainWindowDialogsOverlay.axaml`, change:

```xml
Classes="primary-action"
```

on `Dialogs.DismissNoticeCommand` to:

```xml
Classes="primary-action dialog-action"
```

Remove `Width="120"`. Keep `HorizontalAlignment="Center"` unchanged.

- [ ] **Step 4: Normalize settings footer buttons**

In `Views/MainWindowSettingsOverlay.axaml`, change:

```xml
Classes="flat-action settings-footer-action"
```

to:

```xml
Classes="flat-action dialog-action"
```

Change:

```xml
Classes="primary-action settings-footer-action"
```

to:

```xml
Classes="primary-action dialog-action"
```

For both footer `MaterialIcon` elements, change `LauncherIconMd` to `LauncherIconSm` for both `Width` and `Height`.

- [ ] **Step 5: Normalize crash-recovery continue button**

In `Views/MainWindowDialogsOverlay.axaml`, remove:

```xml
Height="{StaticResource LauncherControlHeightDialog}"
```

from `Dialogs.ContinueAfterCrashCommand`, and change its icon `Width` and `Height` from `LauncherIconMd` to `LauncherIconSm`.

- [ ] **Step 6: Preserve the disabled settings-save appearance**

In `Views/MainWindow.Styles.axaml`, replace:

```xml
<Style Selector="Button.primary-action.settings-footer-action:disabled">
```

with:

```xml
<Style Selector="Button.primary-action.dialog-action:disabled">
```

Keep its existing setters unchanged.

- [ ] **Step 7: Run both contracts and verify GREEN**

Run:

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~DialogActionButtonContractTests"
```

Expected: 2 passed, 0 failed.

- [ ] **Step 8: Commit normalized dialog markup**

```powershell
git add -- tests/Cafe.Launcher.Avalonia.Tests/DialogActionButtonContractTests.cs Views/MainWindowSettingsOverlay.axaml Views/MainWindowDialogsOverlay.axaml Views/MainWindow.Styles.axaml
git commit -m "refactor(ui): 规范全部对话框操作按钮"
```

### Task 3: Verify the complete change

**Files:**
- Verify only; no planned file changes.

- [ ] **Step 1: Run all unit tests except the documented unrelated density failure**

Run:

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName!~UiStyleContractTests.SemanticComponents_UseBalancedDensityTokens"
```

Expected: all selected tests pass; the two symbolic-link tests may be skipped on the current Windows environment.

- [ ] **Step 2: Run all Headless tests**

Run:

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.HeadlessTests\Cafe.Launcher.Avalonia.HeadlessTests.csproj -c Debug --no-restore
```

Expected: all tests pass.

- [ ] **Step 3: Run Release build**

Run:

```powershell
dotnet restore .\Cafe.Launcher.Avalonia.csproj -r win-x64
dotnet build .\Cafe.Launcher.Avalonia.csproj -c Release --no-restore
```

Expected: build succeeds with 0 warnings and 0 errors.

- [ ] **Step 4: Inspect the final diff**

Run:

```powershell
git diff --check
git status --short
```

Expected: no whitespace errors; only the planned dialog files remain modified by this implementation.
