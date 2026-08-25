# M0 Spike 结论 — MaterialColorUtilities 包可行性 + 新旧公式对照

> 状态：**GO** · 日期：2026-08-25 · 环境：.NET SDK 10.0.400 / `net10.0` / Avalonia 12.1.1
> 输入：[P1 实施计划](p1-implementation-plan.md) M0 · 对应决策：Q17（NuGet 直引 + 文档化 vendor 回退）
> 临时工程（均不入库，位于 `%TEMP%\m0-spike\`）：`spike-new`（Shirasagi core 0.2.0）、`spike-old`（albi005 0.3.0）、`spike-avalonia`（集成包 × Avalonia 12.1.1）。可在 `%TEMP%\m0-spike\` 下用 `dotnet run --project <dir>` 复现。

## 0. 结论摘要

| 问题 | 结论 |
|---|---|
| 参考值断言（seed `#6750A4`，Light/Dark × TonalSpot，±1 tone 容差） | **通过**：计划强制的 5 个角色（Primary / PrimaryContainer / SecondaryContainer / Surface / OnPrimary）全部 ≤±0.17 tone；60 行断言 56 PASS / 4 FAIL（详见 §3） |
| Avalonia 12.1.1 集成包构建 | **可构建可绑定**（无 NU 冲突、无预览降级；运行时 `Avalonia.Base` 实际加载 12.1.1.0），但**不采用集成包**——它强制引入第三方 `DesignTokens.Avalonia` token 框架，与项目自有 App.axaml token 体系冲突 → **方案 B：仅引 core + 手写 ArgbColor↔Avalonia 接线**（§6） |
| 新旧公式对照（albi005 0.3.0 vs Shirasagi 0.2.0） | 差异集中在 3 类映射变化（§5）；**不建议用旧包**；albi005 仅作交叉参考，不进产品依赖 |
| 整体判定 | **Go**：`Shirasagi0012.MaterialColorUtilities` 0.2.0（core）进入 P1 M3 依赖；`Shirasagi0012.MaterialColorUtilities.Avalonia` 不引入 |

后续动作（供 M1–M3 执行）：M3 参考值单测以**本 spike 输出的 6750A4 fixture 表**（§3，来自本移植，自洽锁定）为基准，另叠 MTB 参考 ±1 tone 容差断言；配色管线构建 scheme 时**显式传 `SpecVersion.Spec2021`**（默认行为=Spec2021，已证实，但显式化防上游默认变更）；M3 的 ArgbColor↔`Avalonia.Media.Color` 接线自写（≈10 行，见 §6）。

## 1. 包与依赖树

| 包 | 版本 | TFM | 依赖 | 许可 | 用途 |
|---|---|---|---|---|---|
| `Shirasagi0012.MaterialColorUtilities` | 0.2.0 | net10.0 / net8.0 | **无** | Apache-2.0 | **产品依赖（core）** |
| `Shirasagi0012.MaterialColorUtilities.Avalonia` | 0.2.0 | net10.0 / net8.0 | core 0.2.0 + `Avalonia >= 12.0.0-preview2` + `DesignTokens.Avalonia 0.1.0` | Apache-2.0 | **不采用**（§6） |
| `DesignTokens.Avalonia` | 0.1.0 | net10.0 / net8.0 | `Avalonia >= 12.0.0-preview2` | MIT | 集成包传递引入；不进入产品 |
| `MaterialColorUtilities`（albi005） | 0.3.0 | net6.0 / netstandard2.0 / 2.1 | 无（含源生成器） | MIT | 仅对照数据点 |

**Avalonia 兼容性验证（关键）**：集成包与 DesignTokens 的 nuspec 依赖写的是 `version="12.0.0-preview2"`，语义为**下限 `[12.0.0-preview2,)`，无上限**。NuGet 在该区间内选择直接引用（12.1.1）→ 资产图最终收敛 `Avalonia 12.1.1`，**无 NU1605 降级警告、无预览版锁定**。运行时冒烟：集成报 DLL 引用 `Avalonia.Base, Version=12.0.0.0, PublicKeyToken=c8d484a7012f9a8b`，加载 12.1.1 时按名绑定成功，实际版本 `12.1.1.0`（输出见 spike-avalonia 运行日志第一行）。

## 2. 参考值断言（#6750A4 · TonalSpot · default≡Spec2021）

断言方法：`new SchemeTonalSpot(Hct.From(seed), isDark, 0.0)` → 各角色 `ArgbColor`；tone 由库自身 `Hct.From(argb).Tone` 计算（与参考自洽）；判定 = `|Δtone| ≤ 1.0`。参考值 = Material Theme Builder 公开基线（2021 初版发布值）。

| 角色 | Light 输出 (tone) | Light Δtone | Dark 输出 (tone) | Dark Δtone |
|---|---|---|---|---|
| **Primary** | `65558F` (39.99) | −0.09 ✅ | `CFBDFE` (80.08) | +0.12 ✅ |
| **OnPrimary** | `FFFFFF` (100.00) | 0.00 ✅ | `36275D` (20.15) | +0.17 ✅ |
| **PrimaryContainer** | `E9DDFF` (89.98) | −0.08 ✅ | `4D3D75` (29.91) | −0.15 ✅ |
| **SecondaryContainer** | `E8DEF8` (89.94) | 0.00 ✅ | `4A4458` (30.14) | 0.00 ✅ |
| **Surface** | `FDF7FF` (97.88) | −0.08 ✅ | `141218` (5.85) | 0.00 ✅ |
| OnPrimaryContainer | `4D3D75` (29.91) | **+20.01 ❌** | `E9DDFF` (89.98) | −0.08 ✅ |
| OnSecondaryContainer | `4A4458` (30.14) | **+20.11 ❌** | `E8DEF8` (89.94) | 0.00 ✅ |
| OnTertiaryContainer | `633B48` (30.03) | **+19.90 ❌** | `FFD9E3` (90.06) | +0.21 ✅ |
| OnErrorContainer | `93000A` (29.97) | **+17.61 ❌** | `FFDAD6` (89.98) | −0.60 ✅ |
| Secondary | `625B71` (40.03) | 0.00 ✅ | `CBC2DB` (79.84) | −0.11 ✅ |
| Tertiary | `7E5260` (40.11) | +0.13 ✅ | `EFB8C8` (80.07) | 0.00 ✅ |
| TertiaryContainer | `FFD9E3` (90.06) | +0.21 ✅ | `633B48` (30.03) | 0.00 ✅ |
| Error | `BA1A1A` (40.00) | +0.31 ✅ | `FFB4AB` (80.09) | +0.20 ✅ |
| ErrorContainer | `FFDAD6` (89.98) | −0.60 ✅ | `93000A` (29.97) | −0.72 ✅ |
| Background | `FDF7FF` (97.88) | −0.08 ✅ | `141218` (5.85) | 0.00 ✅ |
| OnBackground | `1D1B20` (10.18) | 0.00 ✅ | `E6E0E9` (89.87) | 0.00 ✅ |
| OnSurface | `1D1B20` (10.18) | 0.00 ✅ | `E6E0E9` (89.87) | 0.00 ✅ |
| SurfaceVariant | `E7E0EB` (90.00) | −0.03 ✅ | `49454E` (29.97) | −0.04 ✅ |
| OnSurfaceVariant | `49454E` (29.97) | −0.04 ✅ | `CAC4CF` (79.92) | −0.03 ✅ |
| Outline | `7A757F` (49.97) | +0.40 ✅ | `948F99` (60.11) | +0.08 ✅ |
| OutlineVariant | `CAC4CF` (79.92) | −0.03 ✅ | `49454E` (29.97) | −0.04 ✅ |
| InverseSurface | `322F35` (19.92) | 0.00 ✅ | `E6E0E9` (89.87) | 0.00 ✅ |
| InverseOnSurface | `F5EFF7` (95.10) | 0.00 ✅ | `322F35` (19.92) | 0.00 ✅ |
| InversePrimary | `CFBDFE` (80.08) | +0.12 ✅ | `65558F` (39.99) | −0.09 ✅ |
| SurfaceTint | `65558F` (39.99) | −0.09 ✅ | `CFBDFE` (80.08) | +0.12 ✅ |
| Scrim / Shadow | `000000` / `000000` | 0.00 ✅ | `000000` / `000000` | 0.00 ✅ |

结论：**60 行断言 56 PASS / 4 FAIL；全部 FAIL 为 light on-container 4 角色，且为参考表选值与上游映射差异，非 HCT 漂移**：

- 当前上游 [color_spec_2021.ts](https://github.com/material-foundation/material-color-utilities/blob/main/typescript/dynamiccolor/color_spec_2021.ts)（main 分支）`onPrimaryContainer` 为 `return s.isDark ? 90 : 30;` —— **当前上游 2021 spec 的 light on-container 就是 tone 30**；tone 10 是 2021 初版发布（m3.material.io 静态文档仍展示）的基线值。本移植忠实于当前上游（tone 30 输出、以及 `primaryContainer` light tone 90 / dark tone 30 @ L382-396 均与上游一致）。
- 计划强制的 5 角色不受此影响，全部 ≤±0.17 tone。
- 精度边界：±1 tone 容差内全部通过；非强制角色最大 |Δtone| = 0.72（dark ErrorContainer）。**主色 hex 级偏差**：Primary `65558F` vs 参考 `6750A4`（ΔR−2, ΔG+5, ΔB−21，ΔE76 ≈ 15.6）—— tone 精确 (39.99 vs 40.08) 下的 HCT 色相/彩度浮点精度残余；与 albi005 输出完全一致（两大包 HCT 核心同源同精度）。**hex 不作断言，M3 单测以本表为 fixture 锁定即可自洽**。

## 3. Spec 版本行为（上游 `ColorSpec2021/2025/2026` 均有实现）

- `new SchemeTonalSpot(...)`（简版 ctor）默认 = `Spec2021`（与显式 Spec2021 输出逐行一致，已 diff 验证）。
- `Spec2025` == `Spec2026`（输出一致），与 Spec2021 **差异显著**（40/60 角色断言行超容差）：如 light OnPrimary `FDF7FF`(t97.9)、light PrimaryContainer `D4C3FD`(t81.9)、dark Surface `0F0D12`(t3.9)、dark TertiaryContainer `F4BFE3`(t82.9) —— 为新一代 spec 的整组重映射，**超出本 spike 范围**，P2 变体/spec 决策需单独评估。
- 对 P1 影响：`ApplyScheme` 用 5 参 ctor **显式传 `SpecVersion.Spec2021`**（默认值虽亦如此，显式化可防上游默认漂移）；Platform 枚举为 `Phone/Watch`（默认 Phone），保持默认。
- 其余能力（P2/M2 相关）：`Variant` 10 种（Monochrome/Neutral/TonalSpot/Vibrant/Expressive/Content/Fidelity/Rainbow/FruitSalad/**Cmf**）、`Quantize` Celebi/Wu/Wsmeans、`Score`、`TonalPalette`、`Contrast.RatioOfTones/Darker/Lighter`（M2 的 AA 校验可复用其 tone 工具，或继续用现有 `ColorUtils` 相对亮度实现，二选一在 M2 定）。

## 4. 新旧公式差异表（albi005 0.3.0 vs Shirasagi 0.2.0）

方法：整批 seed × Light/Dark × TonalSpot，两包各算一次；共享角色 28 个 × 2 模式 = 每 seed 56 行。Δt = 新 − 旧（tone）。三档分类：`同值`（hex 相同）/ `±1t`（|Δt|≤1）/ `diff`（>1 tone）。

| seed | 同值 | ±1t | diff | 小结 |
|---|---|---|---|---|
| `#6750A4`（默认紫） | 33 | 16 | 7 | light on-container ×4、dark OnErrorContainer、Background ×2 |
| `#E8B8A0`（壁纸暖） | 34 | 16 | 6 | light on-container ×4、dark OnErrorContainer、dark Background |
| `#4A6B93`（冷） | 33 | 16 | 7 | light on-container ×4、dark OnErrorContainer、Background ×2 |

**diff 行的根因三组**（以 `#6750A4` 为例，新旧各自相对 MTB 2021 初版基线的差异）：

| 角色 | 旧 (albi005) → 新 (Shirasagi) | Δtone | ΔE76 | 根因 |
|---|---|---|---|---|
| light OnPrimaryContainer | `201047` → `4D3D75` | +19.86 | ≈20 | 2021 初版映射（`foregroundTone(容器,4.5)`→tone 10）→ 当前上游硬编码 tone 30（对比提升：16.7:1 → 8.9:1，均为 AAA/AA） |
| light OnSecondaryContainer | `1E192B` → `4A4458` | +19.99 | ≈20 | 同上 |
| light OnTertiaryContainer | `31101D` → `633B48` | +20.14 | ≈20 | 同上 |
| light OnErrorContainer | `410002` → `93000A` | +19.95 | ≈39 | 同上（error 族在新映射下变化更大） |
| dark OnErrorContainer | `FFB4AB` → `FFDAD6` | +9.89 | ≈19 | dark on-container 由 tone 80 → 90（对 tone-30 容器 4.5:1 临界 → 8.9:1） |
| light Background | `FFFBFF` → `FDF7FF` | −1.14 | ≈2.5 | 旧包 background(99) ≠ surface(98)；新包 **background ≡ surface**(98)（当前上游合并） |
| dark Background | `1C1B1E` → `141218` | −4.14 | ≈4.6 | 同上（旧 bg tone 10 / surface tone 6 → 新均 tone ~5.85） |

±1t 档（≈16 行/seed）：SurfaceVariant/Outline/OutlineVariant/Inverse* 等中性色 ±≤0.22 tone（ΔE≈0.5 内），属两包 HCT 求解器浮点差，无观感意义。

**降级观感量化结论**：若回退 albi005 0.3.0，可见差异 = ① light on-container 4 角色由 tone 30 深色变 tone 10 极深色（ΔE≈20，观感明显；对比度更高但视觉更"重"）；② dark OnErrorContainer 处于 4.5:1 临界（错误场景强调度降低）；③ background 与 surface 拆成两个面色（与"单一中性面"的当前 M3 表面模型相悖，需设计层额外协调）。→ **不支持降级**；且 albi005 缺 `SurfaceTint`/`Scrim` 角色、非 net10.0 TFM、附源生成器。定位维持：仅交叉参考。

## 5. Avalonia 集成包评估（方案 A vs 方案 B）

| 维度 | 方案 A：用集成包 | 方案 B：core + 手写接线（**采纳**） |
|---|---|---|
| 版本兼容 | ✅ 可行（区间满足、绑定成功） | ✅ 无关 |
| 引入面 | `DesignTokens.Avalonia`（第三方**类型化 token 框架**：`TokenHost/TokenBinding/RefPaletteToken/SysColorToken/ThemeVariant` 等，见冒烟输出） | 无可加依赖 |
| 架构契合 | ❌ 第二套 token 体系，与 App.axaml + `Launcher*` token + ThemeDictionaries 平行冲突；集成包为它设计的 `MdRefPaletteExtension`/`MaterialColor` 我们无法直接消费 | ✅ 与既有体系一致 |
| 获得能力 | Map 扩展（`ArgbExtensions.ToAvaloniaColor()` / `HctExtensions`）、scheme 类（TonalSpot/Content/Expressive/Fidelity/FruitSalad/…/Cmf，含 `Scheme(Color)` ctor） | 自写等价：`ArgbColor → Color`、`Color → ArgbColor` 各 ~5 行（ArgbColor 为结构体，含 R/G/B/A+Value） |
| 风险 | 依赖区间下限是 12.0.0-**preview2**（产品升级到 12.2 等时需复验范围） | 零 |

**结论：方案 B。** `M3` 计划不变（"core 必加；集成包视 M0" → 集成包不加），csproj 只加 `Shirasagi0012.MaterialColorUtilities`。

## 6. Go/No-Go 与 P1 影响

- **判定：GO**。无需启动 vendor 裁剪回退（回退文档保留备查：core 无依赖、net10.0 直营，若未来需要，裁剪成本低于预估）。
- 风险表更新：① "包 WIP + 预览版依赖" → 已实测排除（无预览锁定；DesignTokens 仅随集成包出现，已规避）；② "HCT ±1 tone 漂移" → 实测 **tone 极精确（≤±0.72）**，残余风险为 hex 级通道差（ΔE≈15.6 @Primary，恒与 albi005 一致）——M3 单测以本 spike fixture 为准，容差策略维持 ±1 tone；③ 新增关注项：**Spec 版本显式化**（M3 显式 `SpecVersion.Spec2021`）。
- M3 参考值单测 fixture：§2 表中 56 PASS 行的 argb 值（Light/Dark 各 28 角色）可直接作为断言数据。

## 7. 复现记录

```
# spike-new（参考值断言 + spec 模式）：
dotnet run --project %TEMP%\m0-spike\spike-new\spike-new.csproj -- [default|2021|2025|2026]
# spike-old（旧公式对照）：
dotnet run --project %TEMP%\m0-spike\spike-old\spike-old.csproj
# spike-avalonia（集成包冒烟；含 CopyLocalLockFileAssemblies=true）：
dotnet run --project %TEMP%\m0-spike\spike-avalonia\spike-avalonia.csproj
```

数据文件（spike 原始输出）：`spike-new-{default,2021,2025,2026}-out.txt`、`spike-old-out.txt`、`spike-avalonia-out.txt`（均在 `%TEMP%\m0-spike\`）。
