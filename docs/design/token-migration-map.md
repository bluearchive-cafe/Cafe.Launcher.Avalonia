# P1/M1 Token 迁移映射表（Launcher* → Launcher.<Family>.*）

> 状态：**已执行**（2026-08-25，脚本 `scripts/Rename-LauncherTokens.ps1`，共 135 键、1060+142 处替换）。
> 机器可读源：同目录 `token-migration-map.json`（LF JSON，脚本消费；本文为其人工整理版）。
> 规则来源：[design-system-spec.md](design-system-spec.md) §3.1（点分层、`Launcher.<Family>.<Role>[.<State>]`、不留别名层）与 §3.2 家族清单、§3.5 字阶表。

## 0. 总则与特例

- **一换一**：除下列 3 组"值相同合并"外，全部旧键 → 新键一一对应；**值一律不动**（含 `Radius.Md` 仍为 6、字阶仍为现状数值）。
- **保留**：`LauncherBorderButtonTemplate`（共享按钮模板，spec §4 明确保留，不进入点分层）。
- **非 token `Launcher*` 标识不迁移**（C# 类型：`LauncherSettingsService`/`LauncherConstants`/`LauncherApiClient` 等；ViewModel 成员：`Shell.LauncherVersionText`；日志源名：`"LauncherCore"`；`{x:Static constants:LauncherConstants.*}`；resx 键 `LauncherStrings.*`）。
- **3 组值相同合并**（删除重复定义，消费方引用自动合并）：
  | 目标键 | 来源 | 值 |
  |---|---|---|
  | `Launcher.Spacing.Thickness.Sm` | `LauncherSpacingSmThickness` + `LauncherThicknessSm` | Thickness 8 |
  | `Launcher.Color.Success` | `LauncherToastSuccessBrush` + `LauncherSuccessBrush` | `#FF22C55E` |
  | `Launcher.Color.Danger` | `LauncherToastErrorBrush` + `LauncherDangerBrush` | `#FFE5484D` |
- **引用规则修复合并执行**：`Views\{MainWindow.axaml,Styles\Toast.axaml,Styles\RemoteContent.axaml,Styles\SetupWizard.axaml}` 中 34 处静态 token 的 `{DynamicResource}` 误用 → `{StaticResource}`；主题变体笔刷（`Color.*`/`Text.*`）与运行时覆盖键（`Color.Primary*`/`Color.OnPrimary`/`Color.Info`/`Color.FocusRing`/`Color.Carousel.Dot.Active`——`ApplyAccentBrushes` 动态写回）保持 `{DynamicResource}`。

## 1. 家族映射（按 §3.2 十二家族）

### Launcher.Color.*（笔刷；`*` 名记号表示同源色阶）

| 旧键 | 新键 |
|---|---|
| `LauncherAccentBrush` | `Launcher.Color.Primary` |
| `LauncherAccentHoverBrush` | `Launcher.Color.Primary.Hover` |
| `LauncherAccentPressedBrush` | `Launcher.Color.Primary.Pressed` |
| `LauncherAccentSoftBrush` | `Launcher.Color.Primary.Soft` |
| `LauncherAccentBorderBrush` | `Launcher.Color.Primary.Border` |
| `LauncherOnAccentBrush` | `Launcher.Color.OnPrimary` |
| `LauncherDangerBrush` | `Launcher.Color.Danger` |
| `LauncherDangerHoverBrush` | `Launcher.Color.Danger.Hover` |
| `LauncherDangerPressedBrush` | `Launcher.Color.Danger.Pressed` |
| `LauncherDangerSoftBrush` | `Launcher.Color.Danger.Soft` |
| `LauncherSuccessBrush` | `Launcher.Color.Success` |
| `LauncherToastWarningBrush` | `Launcher.Color.Warning` |
| `LauncherToastInfoBrush` | `Launcher.Color.Info` |
| `LauncherToastBackgroundBrush` | `Launcher.Color.Toast.Background` |
| `LauncherChromeHoverBrush` | `Launcher.Color.Chrome.Hover` |
| `LauncherChromePressedBrush` | `Launcher.Color.Chrome.Pressed` |
| `LauncherTransparentBrush` | `Launcher.Color.Transparent` |
| `LauncherOverlayBrush` | `Launcher.Color.Overlay.Scrim` |
| `LauncherFocusRingBrush` | `Launcher.Color.FocusRing` |
| `LauncherCarouselDotActiveBrush` | `Launcher.Color.Carousel.Dot.Active` |
| `LauncherCarouselDotInactiveBrush` | `Launcher.Color.Carousel.Dot.Inactive` |
| `LauncherMediaPlaceholderBrush` | `Launcher.Color.Media.Placeholder` |
| `LauncherTitleBarGradient` | `Launcher.Color.TitleBar.Gradient` |
| `LauncherPanelBackgroundBrush` | `Launcher.Color.Panel.Background` |
| `LauncherBottomPanelBackgroundBrush` | `Launcher.Color.Panel.Bottom.Background` |
| `LauncherCardBackgroundBrush` | `Launcher.Color.Card.Background` |
| `LauncherCardBorderBrush` | `Launcher.Color.Card.Border` |
| `LauncherDialogBackgroundBrush` | `Launcher.Color.Dialog.Background` |
| `LauncherDialogHeaderBrush` | `Launcher.Color.Dialog.Header` |
| `LauncherDialogFooterBrush` | `Launcher.Color.Dialog.Footer` |
| `LauncherDialogCloseHoverBrush` | `Launcher.Color.Dialog.Close.Hover` |
| `LauncherDialogClosePressedBrush` | `Launcher.Color.Dialog.Close.Pressed` |
| `LauncherFieldBackgroundBrush` | `Launcher.Color.Field.Background` |
| `LauncherFieldBorderBrush` | `Launcher.Color.Field.Border` |
| `LauncherInfoBackgroundBrush` | `Launcher.Color.Info.Background` |
| `LauncherNoticeBackgroundBrush` | `Launcher.Color.Notice.Background` |
| `LauncherWarningBackgroundBrush` | `Launcher.Color.Warning.Background` |
| `LauncherContentRowBrush` | `Launcher.Color.Content.Row` |
| `LauncherContentRowHoverBrush` | `Launcher.Color.Content.Row.Hover` |
| `LauncherButtonBorderBrush` | `Launcher.Color.Button.Border` |
| `LauncherFlatHoverBrush` | `Launcher.Color.Button.Flat.Hover` |
| `LauncherFlatPressedBrush` | `Launcher.Color.Button.Flat.Pressed` |
| `LauncherSiteButtonBackgroundBrush` | `Launcher.Color.SiteButton.Background` |
| `LauncherSiteButtonBorderBrush` | `Launcher.Color.SiteButton.Border` |

说明：业务/特殊色按 spec §3.2 归 `Color.*`；`Toast*` 业务色与既有同值业务色合并（§0）；`Overlay`→`Scrim`（spec §3.2 示例 `Color.Overlay.Scrim`，M2 扩 3 档）。

### Launcher.Text.*（应用文本专用角色）

| 旧键 | 新键 |
|---|---|
| `LauncherTextPrimaryBrush` | `Launcher.Text.Primary` |
| `LauncherTextSecondaryBrush` | `Launcher.Text.Secondary` |
| `LauncherTextBodyBrush` | `Launcher.Text.Body` |
| `LauncherTextLinkBrush` | `Launcher.Text.Link` |
| `LauncherInfoTextBrush` | `Launcher.Text.Info` |
| `LauncherOnChromeBrush` | `Launcher.Text.OnChrome` |
| `LauncherOnChromeMutedBrush` | `Launcher.Text.OnChrome.Muted` |
| `LauncherOnDarkMutedBrush` | `Launcher.Text.OnDark` |
| `LauncherMediaPlaceholderTextBrush` | `Launcher.Text.Media.Placeholder` |

### Launcher.Spacing.*（间距 + Thickness）

| 旧键 | 新键 |
|---|---|
| `LauncherSpacingXs` / `Sm` / `Md` / `Lg` / `Xl` / `Xxl` / `Section` | `Launcher.Spacing.{Xs,Sm,Md,Lg,Xl,Xxl,Section}` |
| `LauncherSpacingXsThickness` | `Launcher.Spacing.Thickness.Xs` |
| `LauncherSpacingSmThickness` + `LauncherThicknessSm` | `Launcher.Spacing.Thickness.Sm`（合并） |
| `LauncherThicknessMd` / `LauncherThicknessLg` | `Launcher.Spacing.Thickness.{Md,Lg}` |
| `LauncherThicknessNone` | `Launcher.Spacing.Thickness.None` |
| `LauncherSpacingXlThickness` | `Launcher.Spacing.Thickness.Xl` |

### Launcher.Radius.*（形状）

| 旧键 | 新键 | 备注 |
|---|---|---|
| `LauncherRadiusSm`(4) | `Launcher.Radius.Sm` | 值暂不变 |
| `LauncherRadiusMd`(6) | `Launcher.Radius.Md` | 特例：值暂不变，P2 统一字阶时迁移 6→8 并记录 |
| `LauncherRadiusLg`(8) | `Launcher.Radius.Lg` | 值暂不变 |

### Launcher.Typography.*（M3 角色字阶，按 spec §3.5 归组）

| 旧键 | 新键 | M3 角色 |
|---|---|---|
| `LauncherFontSizeDisplay`(22) | `Launcher.Typography.FontSize.Display` | Display |
| `LauncherFontSizeHeadingLg`(19) | `Launcher.Typography.FontSize.Headline.Lg` | Headline |
| `LauncherFontSizeHeadingMd`(18) | `Launcher.Typography.FontSize.Headline.Md` | Headline |
| `LauncherFontSizeHeadingSm`(17) | `Launcher.Typography.FontSize.Title.Lg` | Title |
| `LauncherFontSizeXxl`(16) | `Launcher.Typography.FontSize.Title.Md` | Title |
| `LauncherFontSizeXl`(15) | `Launcher.Typography.FontSize.Body.Lg` | Body |
| `LauncherFontSizeLg`(14) | `Launcher.Typography.FontSize.Body.Md` | Body |
| `LauncherFontSizeMd`(13) | `Launcher.Typography.FontSize.Body.Sm` | Body |
| `LauncherFontSizeSm`(12) | `Launcher.Typography.FontSize.Label.Md` | Label |
| `LauncherFontSizeXs`(11) | `Launcher.Typography.FontSize.Label.Sm` | Label |
| `LauncherFontWeightNormal` / `Strong` | `Launcher.Typography.FontWeight.{Normal,Strong}` | |
| `LauncherFontFamilyMonospace` | `Launcher.Typography.FontFamily.Monospace` | |

### Launcher.Icon.*（图标尺寸）

| 旧键 | 新键 |
|---|---|
| `LauncherIconSm/Md/Lg/Xl/Xxl`(16/18/20/22/24) | `Launcher.Icon.{Sm,Md,Lg,Xl,Xxl}` |

### Launcher.Control.*（控件尺寸）

| 旧键 | 新键 |
|---|---|
| `LauncherControlHeightSetting`(36) | `Launcher.Control.Height.Setting` |
| `LauncherControlHeightDialog`(42) | `Launcher.Control.Height.Dialog` |
| `LauncherControlHeightBottom`(48) | `Launcher.Control.Height.Bottom` |
| `LauncherControlHeightLaunch`(58) | `Launcher.Control.Height.Launch` |
| `LauncherChipHeight`(32) | `Launcher.Control.Height.Chip` |
| `LauncherFieldHeight`(40) | `Launcher.Control.Height.Field` |
| `LauncherSwatchSize`(28) | `Launcher.Control.Size.Swatch` |
| `LauncherControlMinWidthNone`(0) | `Launcher.Control.MinWidth.None` |

### Launcher.Layout.*（视口级常量）

| 旧键 | 新键 |
|---|---|
| `LauncherNewsViewportHeight`(184) | `Launcher.Layout.News.Viewport.Height` |
| `LauncherDebugDialogWidth`(720) / `Height`(540) | `Launcher.Layout.Debug.{Width,Height}` |
| `LauncherDebugLogPathMaxWidth`(300) | `Launcher.Layout.Debug.Log.Path.MaxWidth` |
| `LauncherDebugLogLevelMinWidth`(110) | `Launcher.Layout.Debug.Log.Level.MinWidth` |
| `LauncherDebugSettingsPreviewMaxHeight`(160) | `Launcher.Layout.Debug.SettingsPreview.MaxHeight` |
| `LauncherLogViewerWidth`(720) / `Height`(592) | `Launcher.Layout.LogViewer.{Width,Height}` |

### Launcher.Motion.*（动效，spec §3.7）

| 旧键 | 新键 |
|---|---|
| `LauncherMotionFasterDuration`(50ms) / `Fast`(167ms) / `Content`(200ms) / `Normal`(250ms) | `Launcher.Motion.Duration.{Faster,Fast,Content,Normal}` |
| `LauncherMotionToastOffset`(6) / `Content`(6) / `Surface`(8) / `Bottom`(12) | `Launcher.Motion.Offset.{Toast,Content,Surface,Bottom}` |
| `LauncherMotionEnterEasing` / `ExitEasing` / `LinearEasing` | `Launcher.Motion.Easing.{Enter,Exit,Linear}` |

### Launcher.Component.*（组件专属，spec §3.2）

| 旧键 | 新键 |
|---|---|
| `LauncherToastWidth`(340) | `Launcher.Component.Toast.Width` |
| `LauncherToastActionMinHeight`(30) | `Launcher.Component.Toast.Action.MinHeight` |
| `LauncherToastActionPadding`(12,8) | `Launcher.Component.Toast.Action.Padding` |
| `LauncherToastContentPadding`(12) | `Launcher.Component.Toast.Content.Padding` |
| `LauncherToastMessageMargin`(0,4,0,0) | `Launcher.Component.Toast.Message.Margin` |
| `LauncherToastActionRowMargin`(0,8,0,0) | `Launcher.Component.Toast.Action.RowMargin` |
| `LauncherToastPrimaryActionMargin`(0,0,8,0) | `Launcher.Component.Toast.Action.Primary.Margin` |
| `LauncherToastActionProgressMargin`(8,0,0,0) | `Launcher.Component.Toast.Action.Progress.Margin` |
| `LauncherToastAutoDismissProgressHeight`(3) | `Launcher.Component.Toast.Action.Progress.Height`（自动消失进度条已按 ADR-014 删除；键由「操作执行中」indeterminate 进度条复用） |
| `LauncherConfirmDialogMaxHeight`(480) | `Launcher.Component.Dialog.Confirm.MaxHeight` |
| `LauncherDialogTitleHeight`(56) | `Launcher.Component.Dialog.Title.Height` |
| `LauncherSettingRowContentMinWidth`(240) | `Launcher.Component.Settings.Row.Content.MinWidth` |
| `LauncherSettingRowActionMaxWidth`(440) | `Launcher.Component.Settings.Row.Action.MaxWidth` |
| `LauncherLoadingBarWidth`(120) / `Height`(4) | `Launcher.Component.Loading.Bar.{Width,Height}` |
| `LauncherTabHeaderMargin`(0,0,0,4) | `Launcher.Component.Tabs.Header.Margin` |
| `LauncherTabIndicatorMargin`(0,4,0,2) | `Launcher.Component.Tabs.Indicator.Margin` |
| `LauncherPathFieldPadding`(16,0,4,0) | `Launcher.Component.PathField.Padding` |
| （新增，2026-08-26 确认对话框批次） | `Launcher.Component.Dialog.BasicAction.Padding`(12,0) |
| （新增，同上） | `Launcher.Component.Dialog.BasicAction.MinWidth`(64) |
| （新增，同上） | `Launcher.Component.Dialog.Confirm.MinWidth`(392) |

## 2. 变更范围与门禁记录

- 脚本首轮：24 文件、1060 处（含 App.axaml 定义与 3 组合并去重）；二轮（补 `.cs` XAML 字面量）：2 文件、142 处；修复引用规则：4 文件、34 处 `{DynamicResource}`→`{StaticResource}`。
- 旧键残留巡检：**0**（.axaml 词边界 / .cs 全词，映射表键级审计）。
- `UiStyleContractTests`：键断言全部随脚本更新；2 处前缀断言（FontSize/Space）与新前缀对齐；4 处静态 token 的 Dynamic→Static 断言同步。
- **门禁（✅ 全过，2026-08-25）**：Debug 构建 0 警告 0 错误；单测 1070 通过（2 既有 skip）；headless 99 通过；旧键残留 0。
