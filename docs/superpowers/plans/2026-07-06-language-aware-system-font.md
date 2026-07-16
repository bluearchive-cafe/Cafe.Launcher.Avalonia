# Language-Aware System Font Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the root window use the Windows UI font that corresponds exactly to the effective launcher language without embedding font files.

**Architecture:** A focused `LanguageFontFamilyService` maps only resolved launcher language codes to `Avalonia.Media.FontFamily` instances. `ShellViewModel.ApplyLanguage` first lets `LocalizationService` resolve `auto`, then updates an observable root font property; `MainWindow` binds its inherited `FontFamily` to that property. Explicit monospace and icon fonts remain untouched.

**Tech Stack:** .NET 10, Avalonia 12, CommunityToolkit.Mvvm, xUnit v3, Avalonia.Headless.XUnit

---

## File map

- Create `Services/LanguageFontFamilyService.cs`: exact effective-language-to-system-font mapping.
- Create `tests/Cafe.Launcher.Avalonia.Tests/LanguageFontFamilyServiceTests.cs`: mapping and `auto` resolution contract tests.
- Modify `ViewModels/ShellViewModel.cs`: expose and update the observable root font.
- Modify `Views/MainWindow.axaml`: bind the window-level inherited `FontFamily`.
- Modify `tests/Cafe.Launcher.Avalonia.HeadlessTests/MainWindowHeadlessTests.cs`: verify initial inheritance and runtime language switching.
- Modify `Program.cs`: stop registering Inter as the default font collection.
- Modify `Cafe.Launcher.Avalonia.csproj`: remove the unused `Avalonia.Fonts.Inter` dependency.
- Modify `tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs`: lock down dependency removal and preserve the explicit `Consolas` exception.

### Task 1: Add the exact language font mapping

**Files:**
- Create: `Services/LanguageFontFamilyService.cs`
- Create: `tests/Cafe.Launcher.Avalonia.Tests/LanguageFontFamilyServiceTests.cs`

- [ ] **Step 1: Write failing mapping tests**

Create `tests/Cafe.Launcher.Avalonia.Tests/LanguageFontFamilyServiceTests.cs`:

```csharp
using System.Globalization;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class LanguageFontFamilyServiceTests
{
    [Theory]
    [InlineData(LauncherLanguages.English, "Segoe UI")]
    [InlineData(LauncherLanguages.SimplifiedChinese, "Microsoft YaHei UI")]
    [InlineData(LauncherLanguages.TraditionalChinese, "Microsoft JhengHei UI")]
    [InlineData(LauncherLanguages.Japanese, "Yu Gothic UI")]
    public void GetForEffectiveLanguage_ReturnsExactSystemFont(
        string language,
        string expectedFamilyName)
    {
        var result = LanguageFontFamilyService.GetForEffectiveLanguage(language);

        Assert.Equal(expectedFamilyName, result.Name);
    }

    [Fact]
    public void GetForEffectiveLanguage_WhenLanguageIsAuto_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => LanguageFontFamilyService.GetForEffectiveLanguage(LauncherLanguages.Auto));
    }

    [Fact]
    public void Auto_IsResolvedByLocalizationBeforeFontMapping()
    {
        var previousCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
            var localizer = new LocalizationService();

            var effectiveLanguage = localizer.SetLanguage(LauncherLanguages.Auto);
            var result = LanguageFontFamilyService.GetForEffectiveLanguage(effectiveLanguage);

            Assert.Equal(LauncherLanguages.SimplifiedChinese, effectiveLanguage);
            Assert.Equal("Microsoft YaHei UI", result.Name);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
        }
    }
}
```

- [ ] **Step 2: Run the mapping tests and verify they fail**

Run:

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj -c Debug --filter "FullyQualifiedName~LanguageFontFamilyServiceTests"
```

Expected: build fails with `CS0103` because `LanguageFontFamilyService` does not exist.

- [ ] **Step 3: Implement the exact mapping**

Create `Services/LanguageFontFamilyService.cs`:

```csharp
using System;
using Avalonia.Media;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Services;

public static class LanguageFontFamilyService
{
    private static readonly FontFamily English = new("Segoe UI");
    private static readonly FontFamily SimplifiedChinese = new("Microsoft YaHei UI");
    private static readonly FontFamily TraditionalChinese = new("Microsoft JhengHei UI");
    private static readonly FontFamily Japanese = new("Yu Gothic UI");

    public static FontFamily GetForEffectiveLanguage(string language) =>
        language switch
        {
            LauncherLanguages.English => English,
            LauncherLanguages.SimplifiedChinese => SimplifiedChinese,
            LauncherLanguages.TraditionalChinese => TraditionalChinese,
            LauncherLanguages.Japanese => Japanese,
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, "Unsupported effective launcher language.")
        };
}
```

- [ ] **Step 4: Run the mapping tests and verify they pass**

Run:

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~LanguageFontFamilyServiceTests"
```

Expected: 6 tests pass, 0 fail.

- [ ] **Step 5: Commit the mapping**

```powershell
git add Services/LanguageFontFamilyService.cs tests/Cafe.Launcher.Avalonia.Tests/LanguageFontFamilyServiceTests.cs
git commit -m "feat(ui): 添加界面语言字体映射"
```

### Task 2: Apply the language font at the root window

**Files:**
- Modify: `ViewModels/ShellViewModel.cs`
- Modify: `Views/MainWindow.axaml`
- Modify: `tests/Cafe.Launcher.Avalonia.HeadlessTests/MainWindowHeadlessTests.cs`

- [ ] **Step 1: Write a failing headless inheritance test**

Add to `tests/Cafe.Launcher.Avalonia.HeadlessTests/MainWindowHeadlessTests.cs`:

```csharp
[AvaloniaFact]
public void LanguageFont_WhenLanguageChanges_UpdatesWindowAndInheritedText()
{
    using var context = CreateContext();
    context.Window.Show();
    Dispatcher.UIThread.RunJobs();

    var visibleText = context.Window
        .GetVisualDescendants()
        .OfType<TextBlock>()
        .First(control => control.IsEffectivelyVisible);

    Assert.Equal("Segoe UI", context.Window.FontFamily.Name);
    Assert.Equal("Segoe UI", visibleText.FontFamily.Name);

    context.ViewModel.Shell.ApplyLanguage(
        LauncherLanguages.TraditionalChinese,
        context.ViewModel.Settings,
        context.ViewModel.ResourcePanel,
        hasSnapshot: false);
    Dispatcher.UIThread.RunJobs();

    Assert.Equal("Microsoft JhengHei UI", context.Window.FontFamily.Name);
    Assert.Equal("Microsoft JhengHei UI", visibleText.FontFamily.Name);

    context.ViewModel.Shell.ApplyLanguage(
        LauncherLanguages.Japanese,
        context.ViewModel.Settings,
        context.ViewModel.ResourcePanel,
        hasSnapshot: false);
    Dispatcher.UIThread.RunJobs();

    Assert.Equal("Yu Gothic UI", context.Window.FontFamily.Name);
    Assert.Equal("Yu Gothic UI", visibleText.FontFamily.Name);
}
```

- [ ] **Step 2: Run the headless test and verify it fails**

Run:

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.HeadlessTests\Cafe.Launcher.Avalonia.HeadlessTests.csproj -c Debug --no-restore --filter "FullyQualifiedName~LanguageFont_WhenLanguageChanges_UpdatesWindowAndInheritedText"
```

Expected: assertions fail because the root window does not bind a language-specific font.

- [ ] **Step 3: Expose the effective font from `ShellViewModel`**

In `ViewModels/ShellViewModel.cs`, add the Avalonia namespace:

```csharp
using Avalonia.Media;
```

Add the observable field:

```csharp
[ObservableProperty]
private FontFamily fontFamily =
    LanguageFontFamilyService.GetForEffectiveLanguage(LauncherLanguages.English);
```

At the beginning of `ApplyLanguage`, replace the unobserved `SetLanguage` call:

```csharp
var effectiveLanguage = localizer.SetLanguage(language);
FontFamily = LanguageFontFamilyService.GetForEffectiveLanguage(effectiveLanguage);
```

Keep all existing localization updates after these two statements.

- [ ] **Step 4: Bind the root window font**

In `Views/MainWindow.axaml`, add the property beside `Title`:

```xml
FontFamily="{Binding Shell.FontFamily}"
```

Do not add `FontFamily` setters to child controls. Avalonia inheritance must carry the root value except where a control already declares an explicit font.

- [ ] **Step 5: Run the headless font test and verify it passes**

Run:

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.HeadlessTests\Cafe.Launcher.Avalonia.HeadlessTests.csproj -c Debug --no-restore --filter "FullyQualifiedName~LanguageFont_WhenLanguageChanges_UpdatesWindowAndInheritedText"
```

Expected: 1 test passes, 0 fail.

- [ ] **Step 6: Run existing language and typography tests**

Run:

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.HeadlessTests\Cafe.Launcher.Avalonia.HeadlessTests.csproj -c Debug --no-restore --filter "FullyQualifiedName~SettingsTypography_WhenShown_AppliesNormalAndStrongWeights|FullyQualifiedName~SettingsNavigation_AfterSave_KeepsSelectedItemVisuallySelectedWithoutFocus"
```

Expected: all matched tests pass; the existing `Normal` and `SemiBold` assertions remain unchanged.

- [ ] **Step 7: Commit root font application**

```powershell
git add ViewModels/ShellViewModel.cs Views/MainWindow.axaml tests/Cafe.Launcher.Avalonia.HeadlessTests/MainWindowHeadlessTests.cs
git commit -m "feat(ui): 按界面语言应用全局字体"
```

### Task 3: Remove Inter and lock down explicit font exceptions

**Files:**
- Modify: `Program.cs`
- Modify: `Cafe.Launcher.Avalonia.csproj`
- Modify: `tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs`

- [ ] **Step 1: Write a failing dependency contract test**

Add to `tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs`:

```csharp
[Fact]
public void FontConfiguration_UsesLanguageFontsWithoutInterDefault()
{
    var program = File.ReadAllText(ProjectFile("Program.cs"));
    var project = XDocument.Load(ProjectFile("Cafe.Launcher.Avalonia.csproj"));
    var packageNames = project
        .Descendants()
        .Where(element => element.Name.LocalName == "PackageReference")
        .Select(element => element.Attribute("Include")?.Value)
        .ToArray();

    Assert.DoesNotContain(".WithInterFont()", program, StringComparison.Ordinal);
    Assert.DoesNotContain("Avalonia.Fonts.Inter", packageNames);

    var appDocument = XDocument.Load(ProjectFile("App.axaml"));
    var monospace = appDocument
        .Descendants()
        .Single(element =>
            element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Key"
                && attribute.Value == "LauncherFontFamilyMonospace"));
    Assert.Equal("Consolas", monospace.Value.Trim());
}
```

- [ ] **Step 2: Run the contract test and verify it fails**

Run:

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~FontConfiguration_UsesLanguageFontsWithoutInterDefault"
```

Expected: test fails because `.WithInterFont()` and `Avalonia.Fonts.Inter` still exist.

- [ ] **Step 3: Remove the Inter runtime configuration**

In `Program.cs`, change `BuildAvaloniaApp` to:

```csharp
public static AppBuilder BuildAvaloniaApp()
    => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .LogToTrace();
```

- [ ] **Step 4: Remove the Inter package**

Delete this exact line from `Cafe.Launcher.Avalonia.csproj`:

```xml
<PackageReference Include="Avalonia.Fonts.Inter" Version="12.0.5" />
```

Run restore so the asset graph reflects the removal:

```powershell
dotnet restore .\Cafe.Launcher.Avalonia.csproj -r win-x64
```

Expected: restore succeeds with exit code 0.

- [ ] **Step 5: Run the dependency contract test**

Run:

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj -c Debug --filter "FullyQualifiedName~FontConfiguration_UsesLanguageFontsWithoutInterDefault"
```

Expected: 1 test passes, 0 fail.

- [ ] **Step 6: Confirm there are no remaining Inter references**

Run:

```powershell
rg -n "Avalonia\.Fonts\.Inter|WithInterFont" -g "*.cs" -g "*.csproj" -g "*.axaml"
```

Expected: no matches.

- [ ] **Step 7: Commit dependency cleanup**

```powershell
git add Program.cs Cafe.Launcher.Avalonia.csproj tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs
git commit -m "chore(deps): 移除 Inter 默认字体依赖"
```

### Task 4: Complete verification

**Files:**
- Verify only

- [ ] **Step 1: Check formatting and task scope**

Run:

```powershell
git diff --check HEAD~3..HEAD
git status --short
```

Expected: no whitespace errors and no uncommitted implementation files.

- [ ] **Step 2: Run the repository verification script**

Ensure no running `Cafe.Launcher.Avalonia` process is locking Debug output, then run:

```powershell
.\verify.ps1
```

Expected:

- Debug build: 0 warnings, 0 errors.
- Unit tests: 0 failures.
- Headless tests: 0 failures.
- Release build: 0 warnings, 0 errors.

- [ ] **Step 3: Verify dependency state**

Run:

```powershell
dotnet package list --project .\Cafe.Launcher.Avalonia.csproj --outdated --format json
dotnet package list --project .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --outdated --format json
dotnet package list --project .\tests\Cafe.Launcher.Avalonia.HeadlessTests\Cafe.Launcher.Avalonia.HeadlessTests.csproj --outdated --format json
```

Expected: none of the three JSON results contains a `topLevelPackages` entry.

- [ ] **Step 4: Record the final commit range**

Run:

```powershell
git log --oneline -3
```

Expected commit subjects, newest first:

```text
chore(deps): 移除 Inter 默认字体依赖
feat(ui): 按界面语言应用全局字体
feat(ui): 添加界面语言字体映射
```
