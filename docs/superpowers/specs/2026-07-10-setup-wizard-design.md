# Setup Wizard — First-Launch Configuration Guide

## Problem

Chinese users who want the Cafe-localised game version (Chinese text, Cafe CDN) must manually:
1. Open Settings
2. Find "Download Source"
3. Switch from Official to Cafe

Many users never find this step and report "the Chinese translation doesn't work" in community channels. More broadly, key configuration (language, proxy, game path) is scattered across settings sections and first-time users have no guided path through them.

## Solution

A multi-step overlay dialog shown once when `settings.json` does not exist (i.e. first launch). The wizard guides the user through 5 steps, persists choices via the existing `ISettingsEditor`/`LauncherSettingsService` pipeline, then starts the normal app flow.

### Architecture

```
App.axaml.cs
  │ settings.json 不存在
  ▼
Dialogs.ShowSetupWizard()
  │
  ▼
MainWindow renders as usual (wallpaper, background, shell)
  + SetupWizardOverlay (Z-index between dialog overlay and toast)
    │ step = 0..4
    ▼
  Step 0: Welcome + language selection
  Step 1: Download source (Official / Cafe) + explanation
  Step 2: Game path (text input + browse button)
  Step 3: Proxy mode (direct / auto / system)
  Step 4: Review + Finish
    │
    └── Apply & Save ──► RefreshAsync()
                          SetupWizardVisible = false
```

### Files

**New:**
- `ViewModels/SetupWizardViewModel.cs` — Wizard state machine
- `Views/SetupWizardOverlay.axaml` + `.axaml.cs` — UX layout

**Modified:**
- `ViewModels/DialogsViewModel.cs` — Add `IsSetupWizardVisible`, hold `SetupWizardViewModel`
- `ViewModels/MainWindowViewModel.cs` — Wire wizard-complete → save + refresh
- `Views/MainWindowDialogsOverlay.axaml` — Include `<views:SetupWizardOverlay/>`
- `App.axaml.cs` — Check first-launch condition, show wizard

### SetupWizardViewModel

```
Properties:
  Step               → int (0-4)
  IsFirstStep        → Step == 0
  IsLastStep         → Step == 4
  CanGoNext          → validation passes for current step
  CanGoPrevious      → Step > 0
  Language           → string (default: auto)
  PatchUrlGroup      → string (default: cafe on zh-system, official otherwise)
  GamePath           → string
  ProxyMode          → string (default: auto)

Commands:
  NextCommand        → Step++
  PreviousCommand    → Step--
  CompleteCommand    → emit SettingsApplied event

Events:
  event Func<LauncherSettings, Task>? SettingsApplied;
```

### Steps detail

| Step | Title | Control | Hint text |
|---|---|---|---|
| 0 | 欢迎与语言 | Welcome text + Language dropdown | "欢迎使用 Cafe Launcher！" + "选择启动器的显示语言" |
| 1 | 下载源 | Dropdown: Official / Cafe | "Cafe 源使用汉化版本……" |
| 2 | 游戏路径 | TextBox + Browse button | "游戏安装目录……" |
| 3 | 代理设置 | Dropdown: direct / auto / system | "如遇到下载速度慢……" |
| 4 | 确认 | Summary of all 4 choices | "确认这些设置吗？后续可在设置中修改。" |

### Integration with existing code

- `DialogsViewModel` already has `Is*Visible` + `Show*()` pattern (e.g. `ShowCrashRecovery`). `ShowSetupWizard()` follows the same pattern.
- `App.axaml.cs` checks `Program.FirstLaunch` (set in `Program.cs` when `settings.json` missing). If true, calls `viewModel.Dialogs.ShowSetupWizard()` instead of (or before) normal init.
- `LauncherApiClient` / `HttpClient` / `GameDownloadService` are not touched — the wizard only writes settings.

### Test coverage

- `SetupWizardViewModel` — step navigation, validation per step, completion emission
- `DialogsViewModel` — `ShowSetupWizard` visibility toggle
- `MainWindowViewModel` — wizard completion triggers save + refresh
- Headless — visual smoke test: overlay shows at step 0, advance to step 4, confirm summary renders
