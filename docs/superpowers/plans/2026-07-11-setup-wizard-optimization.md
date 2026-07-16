# 设置向导优化实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 将首次启动设置向导优化为固定尺寸、左侧受限导航、右侧决策卡片的引导式界面，并提升下载源与代理选项的可理解性。

**架构：** 保留现有 `SetupWizardViewModel` 作为向导状态机和设置输出源，在其上增加步骤导航、摘要和推荐状态投影。保留现有 `SetupWizardOverlay.axaml` 专用视图，但改为固定尺寸两栏布局；本地化继续通过 `LocalizationService` / `LocalizedStrings` 和 4 个 locale JSON 文件提供。

**技术栈：** .NET 10、Avalonia 12、CommunityToolkit.Mvvm、xUnit v3、Avalonia Headless、Coverlet。

---

## 文件结构

- 修改：`ViewModels/SetupWizardViewModel.cs`
  - 继续负责步骤推进、设置输出和浏览目录委托。
  - 新增左侧导航状态、步骤摘要、推荐状态、按步骤返回命令和选项卡选择投影。
- 修改：`Views/SetupWizardOverlay.axaml`
  - 改为固定尺寸弹窗，内部使用 Header / Body / Footer。
  - Body 使用左侧步骤导航和右侧滚动内容。
  - 下载源与代理使用选择卡片，推荐 chip 固定在卡片右上角。
- 修改：`Services/LocalizationService.cs`
  - 在 `LocalizedStrings` 中新增向导优化所需属性，并在 `Apply()` 中绑定对应 key。
- 修改：`Assets/Locales/en.json`
- 修改：`Assets/Locales/zh-Hans.json`
- 修改：`Assets/Locales/zh-Hant.json`
- 修改：`Assets/Locales/ja.json`
  - 补充新增用户可见文案。4 个文件必须保持 key 完整。
- 修改：`tests/Cafe.Launcher.Avalonia.Tests/SetupWizardViewModelTests.cs`
  - 覆盖受限导航、步骤摘要、推荐状态和兼容设置输出。
- 修改：`tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs`
  - 增加固定尺寸、右侧滚动区、推荐 chip 右上角和选择卡片结构约束。

---

### 任务 1：为受限步骤导航添加 ViewModel 测试

**文件：**
- 修改：`tests/Cafe.Launcher.Avalonia.Tests/SetupWizardViewModelTests.cs`
- 修改：`ViewModels/SetupWizardViewModel.cs`

- [ ] **步骤 1：编写失败的导航状态测试**

在 `SetupWizardViewModelTests` 中追加以下测试：

```csharp
[Fact]
public void InitialState_StepNavigationLocksFutureSteps()
{
    var vm = CreateViewModel();

    Assert.Equal(5, vm.TotalSteps);
    Assert.Equal(1, vm.StepNumber);
    Assert.True(vm.IsStep0Current);
    Assert.True(vm.IsStep0Accessible);
    Assert.False(vm.IsStep0Completed);
    Assert.True(vm.IsStep1Locked);
    Assert.True(vm.IsStep2Locked);
    Assert.True(vm.IsStep3Locked);
    Assert.True(vm.IsStep4Locked);
}

[Fact]
public void NextCommand_CompletesPreviousStepAndUnlocksCurrentStep()
{
    var vm = CreateViewModel();

    vm.NextCommand.Execute(null);

    Assert.Equal(1, vm.Step);
    Assert.Equal(2, vm.StepNumber);
    Assert.True(vm.IsStep0Completed);
    Assert.True(vm.IsStep0Accessible);
    Assert.True(vm.IsStep1Current);
    Assert.True(vm.IsStep1Accessible);
    Assert.False(vm.IsStep1Locked);
    Assert.True(vm.IsStep2Locked);
}
```

- [ ] **步骤 2：运行测试并确认失败**

运行：

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~SetupWizardViewModelTests"
```

预期：失败，包含类似 `SetupWizardViewModel does not contain a definition for 'TotalSteps'` 或缺少 `IsStep0Current` 的编译错误。

- [ ] **步骤 3：实现最少导航状态属性**

在 `ViewModels/SetupWizardViewModel.cs` 中修改 `Step` 的通知列表，补充导航相关属性：

```csharp
[ObservableProperty]
[NotifyPropertyChangedFor(nameof(IsFirstStep))]
[NotifyPropertyChangedFor(nameof(IsLastStep))]
[NotifyPropertyChangedFor(nameof(CanGoNext))]
[NotifyPropertyChangedFor(nameof(CanGoPrevious))]
[NotifyPropertyChangedFor(nameof(StepTitle))]
[NotifyPropertyChangedFor(nameof(StepNumber))]
[NotifyPropertyChangedFor(nameof(IsStep0Current))]
[NotifyPropertyChangedFor(nameof(IsStep1Current))]
[NotifyPropertyChangedFor(nameof(IsStep2Current))]
[NotifyPropertyChangedFor(nameof(IsStep3Current))]
[NotifyPropertyChangedFor(nameof(IsStep4Current))]
[NotifyPropertyChangedFor(nameof(IsStep0Completed))]
[NotifyPropertyChangedFor(nameof(IsStep1Completed))]
[NotifyPropertyChangedFor(nameof(IsStep2Completed))]
[NotifyPropertyChangedFor(nameof(IsStep3Completed))]
[NotifyPropertyChangedFor(nameof(IsStep4Completed))]
[NotifyPropertyChangedFor(nameof(IsStep0Accessible))]
[NotifyPropertyChangedFor(nameof(IsStep1Accessible))]
[NotifyPropertyChangedFor(nameof(IsStep2Accessible))]
[NotifyPropertyChangedFor(nameof(IsStep3Accessible))]
[NotifyPropertyChangedFor(nameof(IsStep4Accessible))]
[NotifyPropertyChangedFor(nameof(IsStep0Locked))]
[NotifyPropertyChangedFor(nameof(IsStep1Locked))]
[NotifyPropertyChangedFor(nameof(IsStep2Locked))]
[NotifyPropertyChangedFor(nameof(IsStep3Locked))]
[NotifyPropertyChangedFor(nameof(IsStep4Locked))]
[NotifyPropertyChangedFor(nameof(IsStep1))]
[NotifyPropertyChangedFor(nameof(IsStep2))]
[NotifyPropertyChangedFor(nameof(IsStep3))]
private int step;

public int TotalSteps => 5;
public int StepNumber => Step + 1;

public bool IsStep0Current => Step == 0;
public bool IsStep1Current => Step == 1;
public bool IsStep2Current => Step == 2;
public bool IsStep3Current => Step == 3;
public bool IsStep4Current => Step == 4;

public bool IsStep0Completed => Step > 0;
public bool IsStep1Completed => Step > 1;
public bool IsStep2Completed => Step > 2;
public bool IsStep3Completed => Step > 3;
public bool IsStep4Completed => false;

public bool IsStep0Accessible => Step >= 0;
public bool IsStep1Accessible => Step >= 1;
public bool IsStep2Accessible => Step >= 2;
public bool IsStep3Accessible => Step >= 3;
public bool IsStep4Accessible => Step >= 4;

public bool IsStep0Locked => false;
public bool IsStep1Locked => Step < 1;
public bool IsStep2Locked => Step < 2;
public bool IsStep3Locked => Step < 3;
public bool IsStep4Locked => Step < 4;
```

- [ ] **步骤 4：运行测试验证通过**

运行：

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~SetupWizardViewModelTests"
```

预期：`SetupWizardViewModelTests` 全部通过。

- [ ] **步骤 5：Commit**

```powershell
git add .\ViewModels\SetupWizardViewModel.cs .\tests\Cafe.Launcher.Avalonia.Tests\SetupWizardViewModelTests.cs
git commit -m "feat(setup): add wizard step navigation state"
```

---

### 任务 2：添加步骤返回命令与摘要投影

**文件：**
- 修改：`tests/Cafe.Launcher.Avalonia.Tests/SetupWizardViewModelTests.cs`
- 修改：`ViewModels/SetupWizardViewModel.cs`

- [ ] **步骤 1：编写失败的跳转与摘要测试**

在 `SetupWizardViewModelTests` 中追加以下测试：

```csharp
[Fact]
public void GoToStepCommand_WhenStepCompleted_ReturnsToThatStep()
{
    var vm = CreateViewModel();
    vm.GamePath = @"D:\Games\BlueArchive_JP";
    vm.NextCommand.Execute(null);
    vm.NextCommand.Execute(null);
    vm.NextCommand.Execute(null);

    vm.GoToStepCommand.Execute(1);

    Assert.Equal(1, vm.Step);
    Assert.True(vm.IsStep1Current);
}

[Fact]
public void GoToStepCommand_WhenStepLocked_DoesNotJumpForward()
{
    var vm = CreateViewModel();

    vm.GoToStepCommand.Execute(3);

    Assert.Equal(0, vm.Step);
    Assert.True(vm.IsStep3Locked);
}

[Fact]
public void CompletedStepSummaries_ReflectCurrentSelections()
{
    var vm = CreateViewModel();
    vm.Language = LauncherLanguages.SimplifiedChinese;
    vm.PatchUrlGroup = PatchUrlGroups.Cafe;
    vm.GamePath = @"D:\Games\BlueArchive_JP";
    vm.ProxyMode = ProxyModes.System;

    vm.NextCommand.Execute(null);
    Assert.Equal("简体中文", vm.Step0Summary);

    vm.NextCommand.Execute(null);
    Assert.Equal(vm.DownloadSourceDisplayName, vm.Step1Summary);

    vm.NextCommand.Execute(null);
    Assert.Equal(vm.GamePath, vm.Step2Summary);

    vm.NextCommand.Execute(null);
    Assert.Equal(vm.ProxyDisplayName, vm.Step3Summary);
}
```

- [ ] **步骤 2：运行测试并确认失败**

运行：

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~SetupWizardViewModelTests"
```

预期：失败，包含缺少 `GoToStepCommand` 或 `Step0Summary` 等成员的错误。

- [ ] **步骤 3：实现跳转命令、摘要属性和摘要刷新**

在 `SetupWizardViewModel.cs` 中给 `Language`、`PatchUrlGroup`、`GamePath`、`ProxyMode` 补充通知：

```csharp
[ObservableProperty]
[NotifyPropertyChangedFor(nameof(Step0Summary))]
[NotifyPropertyChangedFor(nameof(IsCafeDownloadSourceRecommended))]
[NotifyPropertyChangedFor(nameof(DownloadSourceRecommendationText))]
private string language;

[ObservableProperty]
[NotifyPropertyChangedFor(nameof(CanGoNext))]
[NotifyPropertyChangedFor(nameof(IsPatchUrlGroupCafe))]
[NotifyPropertyChangedFor(nameof(IsPatchUrlGroupOfficial))]
[NotifyPropertyChangedFor(nameof(Step1Summary))]
private string patchUrlGroup;

[ObservableProperty]
[NotifyPropertyChangedFor(nameof(CanGoNext))]
[NotifyPropertyChangedFor(nameof(IsGamePathEmpty))]
[NotifyPropertyChangedFor(nameof(Step2Summary))]
private string gamePath;

[ObservableProperty]
[NotifyPropertyChangedFor(nameof(IsProxyAuto))]
[NotifyPropertyChangedFor(nameof(IsProxyDirect))]
[NotifyPropertyChangedFor(nameof(IsProxySystem))]
[NotifyPropertyChangedFor(nameof(Step3Summary))]
private string proxyMode;
```

添加摘要属性：

```csharp
public string Step0Summary => Language switch
{
    LauncherLanguages.English => "English",
    LauncherLanguages.SimplifiedChinese => "简体中文",
    LauncherLanguages.TraditionalChinese => "繁體中文",
    LauncherLanguages.Japanese => "日本語",
    _ => localizer.T("language") + " (Auto)"
};

public string Step1Summary => DownloadSourceDisplayName ?? ResolveDownloadSourceDisplayName();
public string Step2Summary => GamePath;
public string Step3Summary => ProxyDisplayName ?? ResolveProxyDisplayName();
```

在命令区域添加：

```csharp
[RelayCommand]
private void GoToStep(int targetStep)
{
    if (targetStep < 0 || targetStep > 4)
    {
        return;
    }

    if (targetStep > Step)
    {
        return;
    }

    Step = targetStep;
}
```

将 `RefreshSummaryDisplayNames()` 拆出可复用的解析方法，并复用到摘要属性：

```csharp
private string ResolveDownloadSourceDisplayName() => PatchUrlGroup switch
{
    PatchUrlGroups.Cafe => localizer.T("downloadSourceCafe"),
    _ => localizer.T("downloadSourceOfficial")
};

private string ResolveProxyDisplayName() => ProxyMode switch
{
    ProxyModes.Direct => localizer.T("proxyDirect"),
    ProxyModes.Auto => localizer.T("proxyAuto"),
    ProxyModes.System => localizer.T("proxySystem"),
    _ => ProxyMode
};

private void RefreshSummaryDisplayNames()
{
    LanguageDisplayName = Step0Summary;
    DownloadSourceDisplayName = ResolveDownloadSourceDisplayName();
    ProxyDisplayName = ResolveProxyDisplayName();
    OnPropertyChanged(nameof(LanguageDisplayName));
    OnPropertyChanged(nameof(DownloadSourceDisplayName));
    OnPropertyChanged(nameof(ProxyDisplayName));
    OnPropertyChanged(nameof(Step0Summary));
    OnPropertyChanged(nameof(Step1Summary));
    OnPropertyChanged(nameof(Step2Summary));
    OnPropertyChanged(nameof(Step3Summary));
}
```

- [ ] **步骤 4：运行测试验证通过**

运行：

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~SetupWizardViewModelTests"
```

预期：`SetupWizardViewModelTests` 全部通过。

- [ ] **步骤 5：Commit**

```powershell
git add .\ViewModels\SetupWizardViewModel.cs .\tests\Cafe.Launcher.Avalonia.Tests\SetupWizardViewModelTests.cs
git commit -m "feat(setup): support wizard step return navigation"
```

---

### 任务 3：添加下载源推荐状态和代理说明投影

**文件：**
- 修改：`tests/Cafe.Launcher.Avalonia.Tests/SetupWizardViewModelTests.cs`
- 修改：`ViewModels/SetupWizardViewModel.cs`

- [ ] **步骤 1：编写失败的推荐状态测试**

在 `SetupWizardViewModelTests` 中追加以下测试：

```csharp
[Theory]
[InlineData(LauncherLanguages.SimplifiedChinese)]
[InlineData(LauncherLanguages.TraditionalChinese)]
public void IsCafeDownloadSourceRecommended_ForChineseLanguages_ReturnsTrue(string language)
{
    var vm = CreateViewModel();

    vm.Language = language;

    Assert.True(vm.IsCafeDownloadSourceRecommended);
    Assert.NotEmpty(vm.DownloadSourceRecommendationText);
}

[Theory]
[InlineData(LauncherLanguages.English)]
[InlineData(LauncherLanguages.Japanese)]
public void IsCafeDownloadSourceRecommended_ForNonChineseLanguages_ReturnsFalse(string language)
{
    var vm = CreateViewModel();

    vm.Language = language;

    Assert.False(vm.IsCafeDownloadSourceRecommended);
}

[Fact]
public void ProxyOptionDescriptions_ReturnNonEmptyLocalizedText()
{
    var vm = CreateViewModel();

    Assert.NotEmpty(vm.ProxyAutoDescription);
    Assert.NotEmpty(vm.ProxyDirectDescription);
    Assert.NotEmpty(vm.ProxySystemDescription);
}
```

- [ ] **步骤 2：运行测试并确认失败**

运行：

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~SetupWizardViewModelTests"
```

预期：失败，包含缺少 `IsCafeDownloadSourceRecommended`、`DownloadSourceRecommendationText` 或代理说明属性的错误。

- [ ] **步骤 3：实现推荐和说明属性**

在 `SetupWizardViewModel.cs` 添加：

```csharp
public bool IsCafeDownloadSourceRecommended =>
    Language is LauncherLanguages.SimplifiedChinese or LauncherLanguages.TraditionalChinese;

public string DownloadSourceRecommendationText => IsCafeDownloadSourceRecommended
    ? localizer.T("setupWizardDownloadSourceCafeRecommendedHint")
    : localizer.T("setupWizardDownloadSourceNeutralHint");

public string DownloadSourceCafeDescription => localizer.T("setupWizardDownloadSourceCafeDescription");
public string DownloadSourceOfficialDescription => localizer.T("setupWizardDownloadSourceOfficialDescription");
public string RecommendedChipText => localizer.T("setupWizardRecommendedChip");

public string ProxyAutoDescription => localizer.T("setupWizardProxyAutoDescription");
public string ProxyDirectDescription => localizer.T("setupWizardProxyDirectDescription");
public string ProxySystemDescription => localizer.T("setupWizardProxySystemDescription");
```

确认任务 2 中的 `Language` 属性已有：

```csharp
[NotifyPropertyChangedFor(nameof(IsCafeDownloadSourceRecommended))]
[NotifyPropertyChangedFor(nameof(DownloadSourceRecommendationText))]
```

- [ ] **步骤 4：运行测试验证通过**

运行：

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~SetupWizardViewModelTests"
```

预期：`SetupWizardViewModelTests` 全部通过。

- [ ] **步骤 5：Commit**

```powershell
git add .\ViewModels\SetupWizardViewModel.cs .\tests\Cafe.Launcher.Avalonia.Tests\SetupWizardViewModelTests.cs
git commit -m "feat(setup): add wizard contextual recommendations"
```

---

### 任务 4：补充本地化 key 与 LocalizedStrings 属性

**文件：**
- 修改：`Services/LocalizationService.cs`
- 修改：`Assets/Locales/en.json`
- 修改：`Assets/Locales/zh-Hans.json`
- 修改：`Assets/Locales/zh-Hant.json`
- 修改：`Assets/Locales/ja.json`

- [ ] **步骤 1：在 `LocalizedStrings` 中添加属性**

在 `Services/LocalizationService.cs` 的现有 setup wizard 属性附近添加：

```csharp
[ObservableProperty] private string setupWizardSubtitle = "";
[ObservableProperty] private string setupWizardProgress = "";
[ObservableProperty] private string setupWizardLockedStep = "";
[ObservableProperty] private string setupWizardCompletedStep = "";
[ObservableProperty] private string setupWizardRecommendedChip = "";
[ObservableProperty] private string setupWizardDownloadSourceCafeRecommendedHint = "";
[ObservableProperty] private string setupWizardDownloadSourceNeutralHint = "";
[ObservableProperty] private string setupWizardDownloadSourceCafeDescription = "";
[ObservableProperty] private string setupWizardDownloadSourceOfficialDescription = "";
[ObservableProperty] private string setupWizardLanguageScopeHint = "";
[ObservableProperty] private string setupWizardProxyAutoDescription = "";
[ObservableProperty] private string setupWizardProxyDirectDescription = "";
[ObservableProperty] private string setupWizardProxySystemDescription = "";
[ObservableProperty] private string setupWizardEditStep = "";
```

在 `Apply()` 的 setup wizard 区域添加：

```csharp
SetupWizardSubtitle = localizer.T("setupWizardSubtitle");
SetupWizardProgress = localizer.T("setupWizardProgress");
SetupWizardLockedStep = localizer.T("setupWizardLockedStep");
SetupWizardCompletedStep = localizer.T("setupWizardCompletedStep");
SetupWizardRecommendedChip = localizer.T("setupWizardRecommendedChip");
SetupWizardDownloadSourceCafeRecommendedHint = localizer.T("setupWizardDownloadSourceCafeRecommendedHint");
SetupWizardDownloadSourceNeutralHint = localizer.T("setupWizardDownloadSourceNeutralHint");
SetupWizardDownloadSourceCafeDescription = localizer.T("setupWizardDownloadSourceCafeDescription");
SetupWizardDownloadSourceOfficialDescription = localizer.T("setupWizardDownloadSourceOfficialDescription");
SetupWizardLanguageScopeHint = localizer.T("setupWizardLanguageScopeHint");
SetupWizardProxyAutoDescription = localizer.T("setupWizardProxyAutoDescription");
SetupWizardProxyDirectDescription = localizer.T("setupWizardProxyDirectDescription");
SetupWizardProxySystemDescription = localizer.T("setupWizardProxySystemDescription");
SetupWizardEditStep = localizer.T("setupWizardEditStep");
```

- [ ] **步骤 2：添加英文 locale key**

在 `Assets/Locales/en.json` 中现有 `setupWizard*` key 附近添加：

```json
"setupWizardSubtitle": "Review the essentials before the launcher starts.",
"setupWizardProgress": "Setup progress",
"setupWizardLockedStep": "Locked until previous steps are complete",
"setupWizardCompletedStep": "Completed",
"setupWizardRecommendedChip": "Recommended",
"setupWizardDownloadSourceCafeRecommendedHint": "You selected a Chinese interface language. Cafe source is recommended for users who want localized game resources.",
"setupWizardDownloadSourceNeutralHint": "Choose where the launcher should download game resources from. You can change this later in Settings.",
"setupWizardDownloadSourceCafeDescription": "Best for users who want localized game resources and the Cafe CDN.",
"setupWizardDownloadSourceOfficialDescription": "Best for users who want the original official resources.",
"setupWizardLanguageScopeHint": "This changes the launcher interface language. Game resource language depends on the download source.",
"setupWizardProxyAutoDescription": "Use the launcher's default network behavior.",
"setupWizardProxyDirectDescription": "Connect without using the system proxy.",
"setupWizardProxySystemDescription": "Use the system proxy when refresh or downloads are affected by your local network.",
"setupWizardEditStep": "Edit"
```

- [ ] **步骤 3：添加简体中文 locale key**

在 `Assets/Locales/zh-Hans.json` 中现有 `setupWizard*` key 附近添加：

```json
"setupWizardSubtitle": "在启动器开始工作前，先确认几个关键设置。",
"setupWizardProgress": "设置进度",
"setupWizardLockedStep": "完成前置步骤后可用",
"setupWizardCompletedStep": "已完成",
"setupWizardRecommendedChip": "推荐给你",
"setupWizardDownloadSourceCafeRecommendedHint": "你选择了中文界面。若希望使用中文内容，推荐选择 Cafe 下载源。",
"setupWizardDownloadSourceNeutralHint": "选择启动器从哪里下载游戏资源。完成后仍可在设置中修改。",
"setupWizardDownloadSourceCafeDescription": "适合希望使用中文内容和 Cafe CDN 的用户。",
"setupWizardDownloadSourceOfficialDescription": "适合希望使用官方原始资源的用户。",
"setupWizardLanguageScopeHint": "这里会切换启动器界面语言；游戏资源语言取决于下载源。",
"setupWizardProxyAutoDescription": "使用启动器默认网络行为。",
"setupWizardProxyDirectDescription": "不使用系统代理，直接连接。",
"setupWizardProxySystemDescription": "当刷新或下载受本地网络影响时，使用系统代理。",
"setupWizardEditStep": "修改"
```

- [ ] **步骤 4：添加繁体中文 locale key**

在 `Assets/Locales/zh-Hant.json` 中现有 `setupWizard*` key 附近添加：

```json
"setupWizardSubtitle": "在啟動器開始運作前，先確認幾個關鍵設定。",
"setupWizardProgress": "設定進度",
"setupWizardLockedStep": "完成前置步驟後可用",
"setupWizardCompletedStep": "已完成",
"setupWizardRecommendedChip": "推薦給你",
"setupWizardDownloadSourceCafeRecommendedHint": "你選擇了中文介面。若希望使用中文內容，推薦選擇 Cafe 下載源。",
"setupWizardDownloadSourceNeutralHint": "選擇啟動器要從哪裡下載遊戲資源。完成後仍可在設定中修改。",
"setupWizardDownloadSourceCafeDescription": "適合希望使用中文內容和 Cafe CDN 的使用者。",
"setupWizardDownloadSourceOfficialDescription": "適合希望使用官方原始資源的使用者。",
"setupWizardLanguageScopeHint": "這裡會切換啟動器介面語言；遊戲資源語言取決於下載源。",
"setupWizardProxyAutoDescription": "使用啟動器預設網路行為。",
"setupWizardProxyDirectDescription": "不使用系統代理，直接連線。",
"setupWizardProxySystemDescription": "當重新整理或下載受本機網路影響時，使用系統代理。",
"setupWizardEditStep": "修改"
```

- [ ] **步骤 5：添加日文 locale key**

在 `Assets/Locales/ja.json` 中现有 `setupWizard*` key 附近添加：

```json
"setupWizardSubtitle": "ランチャーを開始する前に、重要な設定を確認します。",
"setupWizardProgress": "設定の進行状況",
"setupWizardLockedStep": "前の手順が完了すると利用できます",
"setupWizardCompletedStep": "完了",
"setupWizardRecommendedChip": "おすすめ",
"setupWizardDownloadSourceCafeRecommendedHint": "中国語の表示言語が選択されています。ローカライズされたゲームリソースを使う場合は Cafe ダウンロード元がおすすめです。",
"setupWizardDownloadSourceNeutralHint": "ゲームリソースのダウンロード元を選択します。この設定は後から変更できます。",
"setupWizardDownloadSourceCafeDescription": "ローカライズされたゲームリソースと Cafe CDN を使いたいユーザー向けです。",
"setupWizardDownloadSourceOfficialDescription": "公式のオリジナルリソースを使いたいユーザー向けです。",
"setupWizardLanguageScopeHint": "これはランチャーの表示言語です。ゲームリソースの言語はダウンロード元によって変わります。",
"setupWizardProxyAutoDescription": "ランチャーの既定のネットワーク動作を使用します。",
"setupWizardProxyDirectDescription": "システムプロキシを使用せずに接続します。",
"setupWizardProxySystemDescription": "更新やダウンロードがローカルネットワークの影響を受ける場合に、システムプロキシを使用します。",
"setupWizardEditStep": "変更"
```

- [ ] **步骤 6：构建验证本地化代码生成可用**

运行：

```powershell
dotnet build .\Cafe.Launcher.Avalonia.csproj -c Debug --no-restore
```

预期：构建成功，输出 0 warnings、0 errors。

- [ ] **步骤 7：Commit**

```powershell
git add .\Services\LocalizationService.cs .\Assets\Locales\en.json .\Assets\Locales\zh-Hans.json .\Assets\Locales\zh-Hant.json .\Assets\Locales\ja.json
git commit -m "feat(setup): localize wizard guidance copy"
```

---

### 任务 5：为固定尺寸和选择卡片编写 XAML 合约测试

**文件：**
- 修改：`tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs`
- 修改：`Views/SetupWizardOverlay.axaml`

- [ ] **步骤 1：编写失败的 XAML 结构测试**

在 `UiStyleContractTests` 中追加以下测试：

```csharp
[Fact]
public void SetupWizardOverlay_UsesFixedDialogDimensionsAndInternalContentScroll()
{
    var document = XDocument.Load(ProjectFile("Views/SetupWizardOverlay.axaml"));
    var dialog = document
        .Descendants()
        .Single(element =>
            element.Name.LocalName == "Border"
            && HasClass(element, "overlay-dialog")
            && HasClass(element, "setup-wizard-dialog"));

    Assert.Equal("920", dialog.Attribute("Width")?.Value);
    Assert.Equal("560", dialog.Attribute("Height")?.Value);
    Assert.Null(dialog.Attribute("MaxWidth"));
    Assert.Null(dialog.Attribute("MaxHeight"));

    var layout = dialog.Elements().Single(element => element.Name.LocalName == "Grid");
    Assert.Equal("Auto,*,Auto", layout.Attribute("RowDefinitions")?.Value);

    var workspace = layout
        .Descendants()
        .Single(element =>
            element.Name.LocalName == "Grid"
            && HasClass(element, "setup-wizard-workspace"));
    Assert.Equal("220,*", workspace.Attribute("ColumnDefinitions")?.Value);

    var contentScroll = workspace
        .Descendants()
        .Single(element =>
            element.Name.LocalName == "ScrollViewer"
            && HasClass(element, "setup-wizard-content-scroll"));
    Assert.Equal("Auto", contentScroll.Attribute("VerticalScrollBarVisibility")?.Value);
    Assert.Equal("Disabled", contentScroll.Attribute("HorizontalScrollBarVisibility")?.Value);
}

[Fact]
public void SetupWizardOverlay_RecommendationChipIsInsideChoiceCardTopRight()
{
    var document = XDocument.Load(ProjectFile("Views/SetupWizardOverlay.axaml"));
    var recommendedCard = document
        .Descendants()
        .Single(element =>
            element.Name.LocalName == "Button"
            && HasClass(element, "wizard-choice-card")
            && HasClass(element, "recommended"));

    var chip = recommendedCard
        .Descendants()
        .Single(element =>
            element.Name.LocalName == "Border"
            && HasClass(element, "wizard-recommendation-chip"));

    Assert.Equal("Right", chip.Attribute("HorizontalAlignment")?.Value);
    Assert.Equal("Top", chip.Attribute("VerticalAlignment")?.Value);
    Assert.Contains(
        recommendedCard.Descendants(),
        element =>
            element.Name.LocalName == "TextBlock"
            && element.Attribute("Text")?.Value == "{Binding Dialogs.SetupWizard.RecommendedChipText}");
}
```

- [ ] **步骤 2：运行测试并确认失败**

运行：

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~UiStyleContractTests"
```

预期：失败，提示找不到 `setup-wizard-dialog`、`setup-wizard-workspace`、固定尺寸属性或推荐 chip 结构。

- [ ] **步骤 3：给现有 XAML 添加最小结构占位以满足测试编译目标**

先不要完成视觉重排，只在 `Views/SetupWizardOverlay.axaml` 的根弹窗 `Border` 上加入目标 class 和固定尺寸，保证下一任务能在明确结构上重排：

```xml
<Border Width="920"
        Height="560"
        Classes="dialog overlay-dialog confirm-panel setup-wizard-dialog">
```

如果该步骤会让现有布局过挤，允许暂时保留内部结构不变；任务 6 会完成完整布局。

- [ ] **步骤 4：运行测试确认仍因工作区 / chip 结构失败**

运行：

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~SetupWizardOverlay_UsesFixedDialogDimensionsAndInternalContentScroll|FullyQualifiedName~SetupWizardOverlay_RecommendationChipIsInsideChoiceCardTopRight"
```

预期：固定尺寸断言通过；工作区和 chip 断言仍失败。

- [ ] **步骤 5：Commit 红灯测试和固定尺寸骨架**

```powershell
git add .\tests\Cafe.Launcher.Avalonia.Tests\UiStyleContractTests.cs .\Views\SetupWizardOverlay.axaml
git commit -m "test(setup): cover wizard fixed layout contracts"
```

---

### 任务 6：重排 SetupWizardOverlay 为固定尺寸两栏布局

**文件：**
- 修改：`Views/SetupWizardOverlay.axaml`
- 修改：`tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs`

- [ ] **步骤 1：替换弹窗主体结构**

将 `Views/SetupWizardOverlay.axaml` 中 `Border` 内部改为以下结构。保留文件头部 namespace 和 `x:DataType`。

```xml
<Border Width="920"
        Height="560"
        Classes="dialog overlay-dialog confirm-panel setup-wizard-dialog">
    <Grid RowDefinitions="Auto,*,Auto" Classes="confirm-layout setup-wizard-layout">
        <Grid Classes="dialog-heading" ColumnDefinitions="Auto,*,Auto">
            <Border Classes="dialog-icon" VerticalAlignment="Center">
                <materialIcons:MaterialIcon Kind="StarOutline"
                                           Width="{StaticResource LauncherIconXl}"
                                           Height="{StaticResource LauncherIconXl}"
                                           Foreground="{DynamicResource LauncherAccentBrush}"
                                           HorizontalAlignment="Center"
                                           VerticalAlignment="Center"/>
            </Border>
            <StackPanel Grid.Column="1" Classes="dialog-heading-copy">
                <TextBlock Text="{Binding Shell.I18n.SetupWizardStepTitle}" Classes="dialog-title"/>
                <TextBlock Text="{Binding Shell.I18n.SetupWizardSubtitle}" Classes="caption"/>
            </StackPanel>
            <Button Grid.Column="2"
                    Classes="flat-action dialog-action"
                    Command="{Binding Dialogs.SetupWizard.SkipCommand}"
                    ToolTip.Tip="{Binding Shell.I18n.SetupWizardSkip}"
                    AutomationProperties.Name="{Binding Shell.I18n.SetupWizardSkip}">
                <StackPanel Classes="button-content">
                    <materialIcons:MaterialIcon Kind="Close"
                                               Width="{StaticResource LauncherIconSm}"
                                               Height="{StaticResource LauncherIconSm}"/>
                    <TextBlock Text="{Binding Shell.I18n.SetupWizardSkip}"/>
                </StackPanel>
            </Button>
        </Grid>

        <Grid Grid.Row="1"
              Classes="setup-wizard-workspace"
              ColumnDefinitions="220,*">
            <StackPanel Classes="setup-wizard-navigation">
                <TextBlock Text="{Binding Shell.I18n.SetupWizardProgress}" Classes="caption"/>
                <!-- Step buttons are added in step 2 of this task. -->
            </StackPanel>

            <ScrollViewer Grid.Column="1"
                          Classes="setup-wizard-content-scroll"
                          VerticalScrollBarVisibility="Auto"
                          HorizontalScrollBarVisibility="Disabled">
                <Grid Classes="setup-wizard-content">
                    <!-- Existing per-step content is moved here in step 3. -->
                </Grid>
            </ScrollViewer>
        </Grid>

        <StackPanel Grid.Row="2" Classes="confirm-actions">
            <Button Classes="flat-action dialog-action"
                    Command="{Binding Dialogs.SetupWizard.PreviousCommand}"
                    IsVisible="{Binding Dialogs.SetupWizard.CanGoPrevious}"
                    AutomationProperties.Name="{Binding Shell.I18n.SetupWizardPrevious}">
                <StackPanel Classes="button-content">
                    <materialIcons:MaterialIcon Kind="ChevronLeft"
                                               Width="{StaticResource LauncherIconSm}"
                                               Height="{StaticResource LauncherIconSm}"/>
                    <TextBlock Text="{Binding Shell.I18n.SetupWizardPrevious}"/>
                </StackPanel>
            </Button>
            <Button Classes="primary-action dialog-action"
                    Command="{Binding Dialogs.SetupWizard.NextCommand}"
                    IsVisible="{Binding Dialogs.SetupWizard.IsLastStep, Converter={x:Static BoolConverters.Not}}"
                    IsEnabled="{Binding Dialogs.SetupWizard.CanGoNext}"
                    AutomationProperties.Name="{Binding Shell.I18n.SetupWizardNext}">
                <StackPanel Classes="button-content">
                    <TextBlock Text="{Binding Shell.I18n.SetupWizardNext}"/>
                    <materialIcons:MaterialIcon Kind="ChevronRight"
                                               Width="{StaticResource LauncherIconSm}"
                                               Height="{StaticResource LauncherIconSm}"/>
                </StackPanel>
            </Button>
            <Button Classes="primary-action dialog-action"
                    Command="{Binding Dialogs.SetupWizard.CompleteCommand}"
                    IsVisible="{Binding Dialogs.SetupWizard.IsLastStep}"
                    AutomationProperties.Name="{Binding Shell.I18n.SetupWizardFinish}">
                <StackPanel Classes="button-content">
                    <materialIcons:MaterialIcon Kind="Check"
                                               Width="{StaticResource LauncherIconSm}"
                                               Height="{StaticResource LauncherIconSm}"/>
                    <TextBlock Text="{Binding Shell.I18n.SetupWizardFinish}"/>
                </StackPanel>
            </Button>
        </StackPanel>
    </Grid>
</Border>
```

- [ ] **步骤 2：添加左侧步骤按钮**

在 `StackPanel Classes="setup-wizard-navigation"` 中标题后加入 5 个按钮。每个按钮都绑定对应步骤状态和 `GoToStepCommand`：

```xml
<Button Classes="wizard-step-link"
        Classes.current="{Binding Dialogs.SetupWizard.IsStep0Current}"
        Classes.completed="{Binding Dialogs.SetupWizard.IsStep0Completed}"
        IsEnabled="{Binding Dialogs.SetupWizard.IsStep0Accessible}"
        Command="{Binding Dialogs.SetupWizard.GoToStepCommand}"
        CommandParameter="0"
        AutomationProperties.Name="{Binding Shell.I18n.SetupWizardStep0Title}">
    <Grid ColumnDefinitions="Auto,*" Classes="wizard-step-link-content">
        <TextBlock Text="1" Classes="wizard-step-index"/>
        <StackPanel Grid.Column="1" Classes="wizard-step-copy">
            <TextBlock Text="{Binding Shell.I18n.SetupWizardStep0Title}" Classes="value"/>
            <TextBlock Text="{Binding Dialogs.SetupWizard.Step0Summary}" Classes="caption"/>
        </StackPanel>
    </Grid>
</Button>
<Button Classes="wizard-step-link"
        Classes.current="{Binding Dialogs.SetupWizard.IsStep1Current}"
        Classes.completed="{Binding Dialogs.SetupWizard.IsStep1Completed}"
        IsEnabled="{Binding Dialogs.SetupWizard.IsStep1Accessible}"
        Command="{Binding Dialogs.SetupWizard.GoToStepCommand}"
        CommandParameter="1"
        AutomationProperties.Name="{Binding Shell.I18n.SetupWizardStep1Title}">
    <Grid ColumnDefinitions="Auto,*" Classes="wizard-step-link-content">
        <TextBlock Text="2" Classes="wizard-step-index"/>
        <StackPanel Grid.Column="1" Classes="wizard-step-copy">
            <TextBlock Text="{Binding Shell.I18n.SetupWizardStep1Title}" Classes="value"/>
            <TextBlock Text="{Binding Dialogs.SetupWizard.Step1Summary}" Classes="caption"/>
        </StackPanel>
    </Grid>
</Button>
<Button Classes="wizard-step-link"
        Classes.current="{Binding Dialogs.SetupWizard.IsStep2Current}"
        Classes.completed="{Binding Dialogs.SetupWizard.IsStep2Completed}"
        IsEnabled="{Binding Dialogs.SetupWizard.IsStep2Accessible}"
        Command="{Binding Dialogs.SetupWizard.GoToStepCommand}"
        CommandParameter="2"
        AutomationProperties.Name="{Binding Shell.I18n.SetupWizardStep2Title}">
    <Grid ColumnDefinitions="Auto,*" Classes="wizard-step-link-content">
        <TextBlock Text="3" Classes="wizard-step-index"/>
        <StackPanel Grid.Column="1" Classes="wizard-step-copy">
            <TextBlock Text="{Binding Shell.I18n.SetupWizardStep2Title}" Classes="value"/>
            <TextBlock Text="{Binding Dialogs.SetupWizard.Step2Summary}" Classes="caption" TextTrimming="CharacterEllipsis"/>
        </StackPanel>
    </Grid>
</Button>
<Button Classes="wizard-step-link"
        Classes.current="{Binding Dialogs.SetupWizard.IsStep3Current}"
        Classes.completed="{Binding Dialogs.SetupWizard.IsStep3Completed}"
        IsEnabled="{Binding Dialogs.SetupWizard.IsStep3Accessible}"
        Command="{Binding Dialogs.SetupWizard.GoToStepCommand}"
        CommandParameter="3"
        AutomationProperties.Name="{Binding Shell.I18n.SetupWizardStep3Title}">
    <Grid ColumnDefinitions="Auto,*" Classes="wizard-step-link-content">
        <TextBlock Text="4" Classes="wizard-step-index"/>
        <StackPanel Grid.Column="1" Classes="wizard-step-copy">
            <TextBlock Text="{Binding Shell.I18n.SetupWizardStep3Title}" Classes="value"/>
            <TextBlock Text="{Binding Dialogs.SetupWizard.Step3Summary}" Classes="caption"/>
        </StackPanel>
    </Grid>
</Button>
<Button Classes="wizard-step-link"
        Classes.current="{Binding Dialogs.SetupWizard.IsStep4Current}"
        IsEnabled="{Binding Dialogs.SetupWizard.IsStep4Accessible}"
        Command="{Binding Dialogs.SetupWizard.GoToStepCommand}"
        CommandParameter="4"
        AutomationProperties.Name="{Binding Shell.I18n.SetupWizardStep4Title}">
    <Grid ColumnDefinitions="Auto,*" Classes="wizard-step-link-content">
        <TextBlock Text="5" Classes="wizard-step-index"/>
        <StackPanel Grid.Column="1" Classes="wizard-step-copy">
            <TextBlock Text="{Binding Shell.I18n.SetupWizardStep4Title}" Classes="value"/>
            <TextBlock Text="{Binding Shell.I18n.SetupWizardLockedStep}" Classes="caption"/>
        </StackPanel>
    </Grid>
</Button>
```

- [ ] **步骤 3：移动现有步骤内容到右侧 content Grid**

把原来 `ScrollViewer` 中的 5 个 `StackPanel IsVisible=...` 移入 `Grid Classes="setup-wizard-content"`。每个步骤根节点使用 `StackPanel Spacing="{StaticResource LauncherSpacingMd}"` 并保留现有 `IsVisible` 绑定。

语言步骤额外在语言 hint 后添加：

```xml
<TextBlock Text="{Binding Shell.I18n.SetupWizardLanguageScopeHint}"
           Classes="caption"
           TextWrapping="Wrap"/>
```

- [ ] **步骤 4：运行 XAML 合约测试**

运行：

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~UiStyleContractTests"
```

预期：任务 5 中新增的固定尺寸 / 滚动测试通过；推荐 chip 测试仍可能失败，任务 7 会补齐选择卡片结构。

- [ ] **步骤 5：Commit**

```powershell
git add .\Views\SetupWizardOverlay.axaml .\tests\Cafe.Launcher.Avalonia.Tests\UiStyleContractTests.cs
git commit -m "feat(setup): introduce fixed wizard two-column layout"
```

---

### 任务 7：将下载源和代理改为选择卡片

**文件：**
- 修改：`Views/SetupWizardOverlay.axaml`
- 修改：`tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs`

- [ ] **步骤 1：替换下载源步骤为选择卡片**

在 `Views/SetupWizardOverlay.axaml` 的下载源步骤中，用以下结构替代原 `RadioButton` 列表：

```xml
<StackPanel Spacing="{StaticResource LauncherSpacingMd}"
            IsVisible="{Binding Dialogs.SetupWizard.IsStep1}">
    <TextBlock Text="{Binding Shell.I18n.SetupWizardDownloadSource}" Classes="section-title"/>
    <TextBlock Text="{Binding Shell.I18n.SetupWizardDownloadSourceHint}"
               Classes="body"
               TextWrapping="Wrap"/>
    <Border Classes="info-strip">
        <TextBlock Text="{Binding Dialogs.SetupWizard.DownloadSourceRecommendationText}"
                   Classes="caption"
                   TextWrapping="Wrap"/>
    </Border>
    <Grid ColumnDefinitions="*,*" ColumnSpacing="{StaticResource LauncherSpacingMd}">
        <Button Classes="wizard-choice-card recommended"
                Classes.selected="{Binding Dialogs.SetupWizard.IsPatchUrlGroupCafe}"
                Command="{Binding Dialogs.SetupWizard.SelectCafeDownloadSourceCommand}"
                AutomationProperties.Name="{Binding Shell.I18n.DownloadSourceCafe}">
            <Grid RowDefinitions="Auto,*">
                <Border Classes="wizard-recommendation-chip"
                        IsVisible="{Binding Dialogs.SetupWizard.IsCafeDownloadSourceRecommended}"
                        HorizontalAlignment="Right"
                        VerticalAlignment="Top">
                    <TextBlock Text="{Binding Dialogs.SetupWizard.RecommendedChipText}" Classes="caption"/>
                </Border>
                <StackPanel Classes="wizard-choice-copy">
                    <TextBlock Text="{Binding Shell.I18n.DownloadSourceCafe}" Classes="value"/>
                    <TextBlock Text="{Binding Dialogs.SetupWizard.DownloadSourceCafeDescription}"
                               Classes="caption"
                               TextWrapping="Wrap"/>
                </StackPanel>
            </Grid>
        </Button>
        <Button Grid.Column="1"
                Classes="wizard-choice-card"
                Classes.selected="{Binding Dialogs.SetupWizard.IsPatchUrlGroupOfficial}"
                Command="{Binding Dialogs.SetupWizard.SelectOfficialDownloadSourceCommand}"
                AutomationProperties.Name="{Binding Shell.I18n.DownloadSourceOfficial}">
            <StackPanel Classes="wizard-choice-copy">
                <TextBlock Text="{Binding Shell.I18n.DownloadSourceOfficial}" Classes="value"/>
                <TextBlock Text="{Binding Dialogs.SetupWizard.DownloadSourceOfficialDescription}"
                           Classes="caption"
                           TextWrapping="Wrap"/>
            </StackPanel>
        </Button>
    </Grid>
</StackPanel>
```

- [ ] **步骤 2：替换代理步骤为选择卡片**

用以下结构替代代理步骤原 `RadioButton` 列表：

```xml
<StackPanel Spacing="{StaticResource LauncherSpacingMd}"
            IsVisible="{Binding Dialogs.SetupWizard.IsStep3}">
    <TextBlock Text="{Binding Shell.I18n.SetupWizardProxy}" Classes="section-title"/>
    <TextBlock Text="{Binding Shell.I18n.SetupWizardProxyHint}"
               Classes="body"
               TextWrapping="Wrap"/>
    <StackPanel Spacing="{StaticResource LauncherSpacingSm}">
        <Button Classes="wizard-choice-card"
                Classes.selected="{Binding Dialogs.SetupWizard.IsProxyAuto}"
                Command="{Binding Dialogs.SetupWizard.SelectProxyAutoCommand}"
                AutomationProperties.Name="{Binding Shell.I18n.ProxyAuto}">
            <StackPanel Classes="wizard-choice-copy">
                <TextBlock Text="{Binding Shell.I18n.ProxyAuto}" Classes="value"/>
                <TextBlock Text="{Binding Dialogs.SetupWizard.ProxyAutoDescription}"
                           Classes="caption"
                           TextWrapping="Wrap"/>
            </StackPanel>
        </Button>
        <Button Classes="wizard-choice-card"
                Classes.selected="{Binding Dialogs.SetupWizard.IsProxyDirect}"
                Command="{Binding Dialogs.SetupWizard.SelectProxyDirectCommand}"
                AutomationProperties.Name="{Binding Shell.I18n.ProxyDirect}">
            <StackPanel Classes="wizard-choice-copy">
                <TextBlock Text="{Binding Shell.I18n.ProxyDirect}" Classes="value"/>
                <TextBlock Text="{Binding Dialogs.SetupWizard.ProxyDirectDescription}"
                           Classes="caption"
                           TextWrapping="Wrap"/>
            </StackPanel>
        </Button>
        <Button Classes="wizard-choice-card"
                Classes.selected="{Binding Dialogs.SetupWizard.IsProxySystem}"
                Command="{Binding Dialogs.SetupWizard.SelectProxySystemCommand}"
                AutomationProperties.Name="{Binding Shell.I18n.ProxySystem}">
            <StackPanel Classes="wizard-choice-copy">
                <TextBlock Text="{Binding Shell.I18n.ProxySystem}" Classes="value"/>
                <TextBlock Text="{Binding Dialogs.SetupWizard.ProxySystemDescription}"
                           Classes="caption"
                           TextWrapping="Wrap"/>
            </StackPanel>
        </Button>
    </StackPanel>
</StackPanel>
```

- [ ] **步骤 3：实现选择命令**

在 `SetupWizardViewModel.cs` 中添加命令：

```csharp
[RelayCommand]
private void SelectCafeDownloadSource() => PatchUrlGroup = PatchUrlGroups.Cafe;

[RelayCommand]
private void SelectOfficialDownloadSource() => PatchUrlGroup = PatchUrlGroups.Official;

[RelayCommand]
private void SelectProxyAuto() => ProxyMode = ProxyModes.Auto;

[RelayCommand]
private void SelectProxyDirect() => ProxyMode = ProxyModes.Direct;

[RelayCommand]
private void SelectProxySystem() => ProxyMode = ProxyModes.System;
```

- [ ] **步骤 4：运行 ViewModel 和 XAML 测试**

运行：

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~SetupWizardViewModelTests|FullyQualifiedName~UiStyleContractTests"
```

预期：`SetupWizardViewModelTests` 和 `UiStyleContractTests` 通过。

- [ ] **步骤 5：Commit**

```powershell
git add .\Views\SetupWizardOverlay.axaml .\ViewModels\SetupWizardViewModel.cs .\tests\Cafe.Launcher.Avalonia.Tests\UiStyleContractTests.cs
git commit -m "feat(setup): replace wizard radios with guidance cards"
```

---

### 任务 8：为新向导类添加样式并满足 token 约束

**文件：**
- 修改：`Views/MainWindow.Styles.axaml`
- 修改：`tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs`

- [ ] **步骤 1：添加样式合约测试**

在 `UiStyleContractTests` 中追加：

```csharp
[Fact]
public void SetupWizardChoiceCards_UseSemanticBrushesAndTokenizedRadii()
{
    var document = XDocument.Load(ProjectFile("Views/MainWindow.Styles.axaml"));

    var card = GetStyleSetters(document, "Button.wizard-choice-card");
    Assert.Equal("{DynamicResource LauncherCardBackgroundBrush}", card["Background"]);
    Assert.Equal("{DynamicResource LauncherCardBorderBrush}", card["BorderBrush"]);
    Assert.Equal("{StaticResource LauncherRadiusMd}", card["CornerRadius"]);

    var selected = GetStyleSetters(document, "Button.wizard-choice-card.selected");
    Assert.Equal("{DynamicResource LauncherAccentSoftBrush}", selected["Background"]);
    Assert.Equal("{DynamicResource LauncherAccentBrush}", selected["BorderBrush"]);

    var chip = GetStyleSetters(document, "Border.wizard-recommendation-chip");
    Assert.Equal("{DynamicResource LauncherAccentSoftBrush}", chip["Background"]);
    Assert.Equal("{StaticResource LauncherRadiusSm}", chip["CornerRadius"]);
}
```

- [ ] **步骤 2：运行测试并确认失败**

运行：

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~SetupWizardChoiceCards_UseSemanticBrushesAndTokenizedRadii"
```

预期：失败，提示找不到对应 style selector。

- [ ] **步骤 3：添加样式**

在 `Views/MainWindow.Styles.axaml` 现有 dialog / settings 样式附近添加：

```xml
<Style Selector="Grid.setup-wizard-workspace">
    <Setter Property="ColumnSpacing" Value="0"/>
</Style>

<Style Selector="StackPanel.setup-wizard-navigation">
    <Setter Property="Spacing" Value="{StaticResource LauncherSpacingSm}"/>
    <Setter Property="Background" Value="{DynamicResource LauncherContentRowBrush}"/>
</Style>

<Style Selector="Button.wizard-step-link">
    <Setter Property="HorizontalAlignment" Value="Stretch"/>
    <Setter Property="Background" Value="{DynamicResource LauncherTransparentBrush}"/>
    <Setter Property="BorderBrush" Value="{DynamicResource LauncherTransparentBrush}"/>
    <Setter Property="BorderThickness" Value="0"/>
    <Setter Property="CornerRadius" Value="{StaticResource LauncherRadiusMd}"/>
    <Setter Property="Padding" Value="{StaticResource LauncherThicknessMd}"/>
</Style>

<Style Selector="Button.wizard-step-link.current">
    <Setter Property="Background" Value="{DynamicResource LauncherFlatPressedBrush}"/>
    <Setter Property="BorderBrush" Value="{DynamicResource LauncherAccentBrush}"/>
    <Setter Property="BorderThickness" Value="1"/>
</Style>

<Style Selector="Grid.wizard-step-link-content">
    <Setter Property="ColumnSpacing" Value="{StaticResource LauncherSpacingSm}"/>
</Style>

<Style Selector="StackPanel.wizard-step-copy">
    <Setter Property="Spacing" Value="{StaticResource LauncherSpacingXs}"/>
</Style>

<Style Selector="TextBlock.wizard-step-index">
    <Setter Property="FontSize" Value="{StaticResource LauncherFontSizeSm}"/>
    <Setter Property="FontWeight" Value="{StaticResource LauncherFontWeightStrong}"/>
</Style>

<Style Selector="Grid.setup-wizard-content">
    <Setter Property="RowSpacing" Value="{StaticResource LauncherSpacingLg}"/>
</Style>

<Style Selector="Button.wizard-choice-card">
    <Setter Property="HorizontalAlignment" Value="Stretch"/>
    <Setter Property="Background" Value="{DynamicResource LauncherCardBackgroundBrush}"/>
    <Setter Property="BorderBrush" Value="{DynamicResource LauncherCardBorderBrush}"/>
    <Setter Property="BorderThickness" Value="1"/>
    <Setter Property="CornerRadius" Value="{StaticResource LauncherRadiusMd}"/>
    <Setter Property="Padding" Value="{StaticResource LauncherThicknessLg}"/>
</Style>

<Style Selector="Button.wizard-choice-card.selected">
    <Setter Property="Background" Value="{DynamicResource LauncherAccentSoftBrush}"/>
    <Setter Property="BorderBrush" Value="{DynamicResource LauncherAccentBrush}"/>
</Style>

<Style Selector="StackPanel.wizard-choice-copy">
    <Setter Property="Spacing" Value="{StaticResource LauncherSpacingXs}"/>
</Style>

<Style Selector="Border.wizard-recommendation-chip">
    <Setter Property="Background" Value="{DynamicResource LauncherAccentSoftBrush}"/>
    <Setter Property="BorderBrush" Value="{DynamicResource LauncherAccentBrush}"/>
    <Setter Property="BorderThickness" Value="1"/>
    <Setter Property="CornerRadius" Value="{StaticResource LauncherRadiusSm}"/>
    <Setter Property="Padding" Value="8,2"/>
</Style>
```

如果 `Views_DoNotInlineReusableTypographyPaddingOrHeaderOffsets` 或 spacing/radius 合约因 `Padding="8,2"` 失败，将其改为新增 token 或复用现有 thickness token。优先复用 `{StaticResource LauncherThicknessSm}`。

- [ ] **步骤 4：运行 UI 样式合约测试**

运行：

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~UiStyleContractTests"
```

预期：`UiStyleContractTests` 全部通过。

- [ ] **步骤 5：Commit**

```powershell
git add .\Views\MainWindow.Styles.axaml .\tests\Cafe.Launcher.Avalonia.Tests\UiStyleContractTests.cs
git commit -m "style(setup): add wizard navigation and choice card styles"
```

---

### 任务 9：确认页添加返回修改入口

**文件：**
- 修改：`Views/SetupWizardOverlay.axaml`
- 修改：`tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs`

- [ ] **步骤 1：添加确认页结构测试**

在 `UiStyleContractTests` 中追加：

```csharp
[Fact]
public void SetupWizardReviewRows_ExposeEditCommandsForCompletedSteps()
{
    var document = XDocument.Load(ProjectFile("Views/SetupWizardOverlay.axaml"));
    var editButtons = document
        .Descendants()
        .Where(element =>
            element.Name.LocalName == "Button"
            && HasClass(element, "wizard-review-edit"))
        .ToList();

    Assert.Equal(4, editButtons.Count);
    Assert.Equal(["0", "1", "2", "3"], editButtons.Select(button => button.Attribute("CommandParameter")?.Value).ToArray());
    Assert.All(editButtons, button => Assert.Equal("{Binding Dialogs.SetupWizard.GoToStepCommand}", button.Attribute("Command")?.Value));
}
```

- [ ] **步骤 2：运行测试并确认失败**

运行：

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~SetupWizardReviewRows_ExposeEditCommandsForCompletedSteps"
```

预期：失败，找不到 `wizard-review-edit` 按钮。

- [ ] **步骤 3：为确认页每项添加修改按钮**

在确认步骤的 4 个摘要 `Grid` 中各加一个第 3 列按钮。将每行 `ColumnDefinitions` 改为：

```xml
ColumnDefinitions="Auto,*,Auto"
```

每行末尾添加对应按钮，示例（语言行）：

```xml
<Button Grid.Column="2"
        Classes="flat-action dialog-action wizard-review-edit"
        Command="{Binding Dialogs.SetupWizard.GoToStepCommand}"
        CommandParameter="0"
        AutomationProperties.Name="{Binding Shell.I18n.SetupWizardEditStep}">
    <TextBlock Text="{Binding Shell.I18n.SetupWizardEditStep}"/>
</Button>
```

下载源、游戏目录、网络行分别使用 `CommandParameter="1"`、`"2"`、`"3"`。

- [ ] **步骤 4：运行 XAML 合约测试**

运行：

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~UiStyleContractTests"
```

预期：`UiStyleContractTests` 全部通过。

- [ ] **步骤 5：Commit**

```powershell
git add .\Views\SetupWizardOverlay.axaml .\tests\Cafe.Launcher.Avalonia.Tests\UiStyleContractTests.cs
git commit -m "feat(setup): add review step edit actions"
```

---

### 任务 10：运行完整相关验证并修复集成问题

**文件：**
- 可能修改：前面任务涉及的所有文件

- [ ] **步骤 1：运行向导 ViewModel 测试**

运行：

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~SetupWizardViewModelTests"
```

预期：全部通过，输出 failed 为 0。

- [ ] **步骤 2：运行 UI 样式合约测试**

运行：

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~UiStyleContractTests"
```

预期：全部通过，输出 failed 为 0。

- [ ] **步骤 3：运行 Debug build**

运行：

```powershell
dotnet build .\Cafe.Launcher.Avalonia.csproj -c Debug --no-restore
```

预期：构建成功，0 warnings，0 errors。

- [ ] **步骤 4：如 XAML 编译失败，优先修复绑定名或 class 语法**

常见修复点：

```xml
Classes.selected="{Binding Dialogs.SetupWizard.IsPatchUrlGroupCafe}"
```

Avalonia 支持该 class 绑定模式；若编译器提示属性名不合法，改用现有项目中已使用的 class 绑定写法作为模板。

- [ ] **步骤 5：Commit 验证修复**

如果步骤 1-4 有修复，提交：

```powershell
git add .\Views\SetupWizardOverlay.axaml .\Views\MainWindow.Styles.axaml .\ViewModels\SetupWizardViewModel.cs .\Services\LocalizationService.cs .\Assets\Locales\en.json .\Assets\Locales\zh-Hans.json .\Assets\Locales\zh-Hant.json .\Assets\Locales\ja.json .\tests\Cafe.Launcher.Avalonia.Tests\SetupWizardViewModelTests.cs .\tests\Cafe.Launcher.Avalonia.Tests\UiStyleContractTests.cs
git commit -m "fix(setup): resolve wizard layout integration issues"
```

如果无修复，不创建空提交。

---

### 任务 11：启动应用手动验证首次设置向导

**文件：**
- 不需要修改文件，除非发现运行时问题。

- [ ] **步骤 1：准备首次启动状态**

不要删除用户真实 `%LOCALAPPDATA%\Cafe Launcher\settings.json`。如果需要模拟首次启动，先手动备份该文件，或使用临时用户环境运行。推荐由当前执行者在运行前明确说明将如何避免破坏本机设置。

- [ ] **步骤 2：运行应用**

运行：

```powershell
dotnet run --project .\Cafe.Launcher.Avalonia.csproj
```

预期：应用启动，首次启动状态下显示设置向导。

- [ ] **步骤 3：人工检查 UI 行为**

检查以下项目：

- 弹窗大小在步骤切换时不变化。
- 左侧步骤导航显示当前、已完成、锁定状态。
- 已完成步骤可返回，未完成步骤不能跳转。
- 下载源卡片中推荐 chip 在右上角，标题和描述对齐。
- 游戏目录为空时「继续」不可用。
- 右侧内容过长时只滚动正文区域。
- 完成后保存设置并进入正常主界面刷新流程。

- [ ] **步骤 4：记录手动验证结果**

在最终汇报中写明实际运行命令和观察结果。如果无法运行桌面应用，明确说明原因，不能声称端到端验证已完成。

---

## 自检

### 规格覆盖度

- 固定弹窗尺寸：任务 5、6、8、10、11 覆盖。
- 左侧导航、已完成可回看、未完成锁定：任务 1、2、6、10、11 覆盖。
- 选择卡片替代单选列表：任务 7、8、10、11 覆盖。
- 情境化推荐与 chip 右上角：任务 3、4、5、7、8、10、11 覆盖。
- 游戏目录校验不变：任务 10 复跑现有 `SetupWizardViewModelTests`，任务 11 人工检查。
- 不改变持久化结构：任务 1-3 只新增投影属性和命令，任务 10 验证 `CompleteCommand` 既有测试。
- 4 个 locale 文件：任务 4 覆盖。

### 占位符扫描

计划中没有「待定」「TODO」「后续实现」「适当处理」等占位指令。每个涉及代码的步骤都包含目标代码块、命令和预期结果。

### 类型一致性

- 步骤属性统一使用 `IsStep0Current` / `IsStep0Completed` / `IsStep0Accessible` / `IsStep0Locked` 命名。
- 跳转命令统一使用 `GoToStepCommand`，参数为 0-4 的整数。
- 选择命令统一使用 `SelectCafeDownloadSourceCommand`、`SelectOfficialDownloadSourceCommand`、`SelectProxyAutoCommand`、`SelectProxyDirectCommand`、`SelectProxySystemCommand`。
- 推荐状态统一使用 `IsCafeDownloadSourceRecommended`、`DownloadSourceRecommendationText`、`RecommendedChipText`。
