# 汉化管理对话框调整 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将“资源面板”明确命名为“汉化管理”，恢复截图中的三项简体中文译文，并将其对话框固定为 `720 × 592`。

**Architecture:** 保留现有本地化键、ViewModel、模型、API 字段和资源代码，只修改三种语言的显示文案。对话框继续使用现有内部 `ScrollViewer`，仅将外层 `Border` 的 `MaxWidth`/`MaxHeight` 改为 `Width`/`Height`。

**Tech Stack:** .NET 10、Avalonia 12、xUnit、JSON 本地化资源

---

## File map

- `Assets/Locales/en.json`：英文功能名称。
- `Assets/Locales/zh-Hans.json`：中文功能名称、说明及截图译文回滚。
- `Assets/Locales/ja.json`：日文功能名称。
- `Views/MainWindowDialogsOverlay.axaml`：汉化管理对话框固定尺寸。
- `tests/Cafe.Launcher.Avalonia.Tests/LocalizationServiceTests.cs`：功能名称和截图译文契约。
- `tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs`：固定尺寸契约。

### Task 1: 锁定本地化文案

**Files:**
- Modify: `tests/Cafe.Launcher.Avalonia.Tests/LocalizationServiceTests.cs`
- Modify: `Assets/Locales/en.json`
- Modify: `Assets/Locales/zh-Hans.json`
- Modify: `Assets/Locales/ja.json`

- [ ] **Step 1: 修改本地化测试，使其表达确认后的文案**

将 `T_WhenCanonicalTermsRequested_ReturnsConsistentTerminology` 的 `resourcePanel` 期望值改为：

```csharp
[InlineData(LauncherLanguages.English, "Remote Manifest", "Download Source", "Chinese Localization Settings")]
[InlineData(LauncherLanguages.SimplifiedChinese, "远程文件清单", "下载源", "汉化管理")]
[InlineData(LauncherLanguages.Japanese, "リモートマニフェスト", "ダウンロードソース", "中国語化設定")]
```

新增截图译文回归测试：

```csharp
[Fact]
public void T_WhenChineseLocalizationItemsRequested_ReturnsEstablishedTerminology()
{
    var service = new LocalizationService();
    service.SetLanguage(LauncherLanguages.SimplifiedChinese);

    Assert.Equal("汉化", service.T("resourcePanelLocalizedVersion"));
    Assert.Equal("主线中配", service.T("resourcePanelMainVoice"));
    Assert.Equal("图像视频", service.T("resourcePanelMedia"));
}
```

- [ ] **Step 2: 运行测试并确认先失败**

运行：

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~LocalizationServiceTests"
```

预期：`T_WhenCanonicalTermsRequested_ReturnsConsistentTerminology` 和 `T_WhenChineseLocalizationItemsRequested_ReturnsEstablishedTerminology` 失败，实际值仍为当前文案。

- [ ] **Step 3: 修改三种语言资源**

精确修改以下值，不改键名：

```json
// en.json
"resourcePanel": "Chinese Localization Settings"
"resourcePanelDescription": "Enable or disable Chinese localization features for this UID."
"resourcePanelCafeOnlyDescription": "Chinese Localization Settings is powered by the Cafe download source."
"resourcePanelCafeOnlyMessage": "Chinese Localization Settings is powered by the Cafe download source. It is not available with the Official download source.\n\nSwitch to Cafe download source?"
"resourcePanelLoadFailed": "Chinese localization settings error: {0}"

// zh-Hans.json
"resourcePanel": "汉化管理"
"resourcePanelDescription": "调整此 UID 的各项汉化是否启用。"
"resourcePanelCafeOnlyDescription": "汉化管理由 Cafe 下载源提供。"
"resourcePanelCafeOnlyMessage": "汉化管理由 Cafe 下载源提供，使用官方下载源时不可用。\n\n是否切换到 Cafe 下载源？"
"resourcePanelLoadFailed": "汉化管理错误：{0}"
"resourcePanelLocalizedVersion": "汉化"
"resourcePanelMainVoice": "主线中配"
"resourcePanelMedia": "图像视频"

// ja.json
"resourcePanel": "中国語化設定"
"resourcePanelDescription": "この UID の中国語化項目を有効または無効にします。"
"resourcePanelCafeOnlyDescription": "中国語化設定は Cafe ダウンロードソースで提供されます。"
"resourcePanelCafeOnlyMessage": "中国語化設定は Cafe ダウンロードソースで提供されるため、公式ダウンロードソースでは利用できません。\n\nCafe ダウンロードソースに切り替えますか？"
"resourcePanelLoadFailed": "中国語化設定エラー：{0}"
```

- [ ] **Step 4: 运行本地化测试并确认通过**

运行：

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~LocalizationServiceTests"
```

预期：全部 `LocalizationServiceTests` 通过。

- [ ] **Step 5: 提交文案改动**

```powershell
git add -- Assets/Locales/en.json Assets/Locales/zh-Hans.json Assets/Locales/ja.json tests/Cafe.Launcher.Avalonia.Tests/LocalizationServiceTests.cs
git commit -m "fix(i18n): 恢复汉化项目译文并调整功能名称"
```

### Task 2: 固定汉化管理对话框尺寸

**Files:**
- Modify: `tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs`
- Modify: `Views/MainWindowDialogsOverlay.axaml`

- [ ] **Step 1: 新增固定尺寸契约测试**

在 `UiStyleContractTests` 中新增：

```csharp
[Fact]
public void LocalizationManagement_UsesFixedDialogDimensions()
{
    var document = XDocument.Load(ProjectFile("Views/MainWindowDialogsOverlay.axaml"));
    var dialog = document
        .Descendants()
        .Single(element =>
            element.Name.LocalName == "Grid"
            && element.Attribute("IsVisible")?.Value
                == "{Binding ResourcePanel.IsResourcePanelVisible}")
        .Elements()
        .Single(element =>
            element.Name.LocalName == "Border"
            && HasClass(element, "overlay-dialog"));

    Assert.Equal("720", dialog.Attribute("Width")?.Value);
    Assert.Equal("592", dialog.Attribute("Height")?.Value);
    Assert.Null(dialog.Attribute("MaxWidth"));
    Assert.Null(dialog.Attribute("MaxHeight"));
}
```

- [ ] **Step 2: 运行测试并确认先失败**

运行：

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~UiStyleContractTests.LocalizationManagement_UsesFixedDialogDimensions"
```

预期：测试失败，当前对话框仅具有 `MaxWidth="720"` 和 `MaxHeight="592"`。

- [ ] **Step 3: 将对话框改为固定尺寸**

将 `Views/MainWindowDialogsOverlay.axaml` 中资源面板外层边框改为：

```xml
<Border Width="720"
        Height="592"
        Classes="dialog overlay-dialog">
```

保留现有内部 `ScrollViewer`，不修改其他对话框。

- [ ] **Step 4: 运行固定尺寸测试并确认通过**

运行：

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~UiStyleContractTests.LocalizationManagement_UsesFixedDialogDimensions"
```

预期：测试通过。

- [ ] **Step 5: 提交尺寸改动**

```powershell
git add -- Views/MainWindowDialogsOverlay.axaml tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs
git commit -m "fix(ui): 固定汉化管理对话框尺寸"
```

### Task 3: 完整验证

**Files:**
- Verify only

- [ ] **Step 1: 检查差异和未提交文件**

运行：

```powershell
git diff --check
git status --short
```

预期：`git diff --check` 无输出；用户已有的 `CHANGELOG_RELEASE.md` 仍保持未提交。

- [ ] **Step 2: 运行完整验证**

运行：

```powershell
.\verify.ps1
```

预期：Debug 与 Release 构建均为 0 警告、0 错误；单元测试和无头测试均无失败。
