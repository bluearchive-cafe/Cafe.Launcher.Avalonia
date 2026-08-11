# 原生 `.resx` 本地化迁移设计

## 决策与约束

启动器迁移到 `.resx`、`ResourceManager` 和 `CultureInfo`。迁移必须保留四种已支持语言、运行时 UI 刷新，以及设置中已有的 `auto`、`en`、`zh-Hans`、`zh-Hant`、`ja` 值。

本次不新增语言、不支持在线或可安装语言包，也不批量重写现有 `T/F` 调用或 Avalonia 绑定。

## 资源与 culture

```text
Resources/LauncherStrings.resx          English neutral fallback
Resources/LauncherStrings.zh-Hans.resx  Simplified Chinese
Resources/LauncherStrings.zh-Hant.resx  Traditional Chinese
Resources/LauncherStrings.ja.resx       Japanese
```

中性资源包含英文及全部 456 个键；其他语言文件的键集合完全一致，只改变值。项目显式生成强类型资源类，`ResourceManager` 同时提供按键查询的兼容入口。资源由 SDK 编译为 embedded/satellite resources，不再从 Avalonia asset 或 JSON 加载。

| 设置值 | 手动选择的 culture | 回退链 |
| --- | --- | --- |
| `en` | `en-US` | `en-US → en → neutral` |
| `zh-Hans` | `zh-CN` | `zh-CN → zh-Hans → zh → neutral` |
| `zh-Hant` | `zh-TW` | `zh-TW → zh-Hant → zh → neutral` |
| `ja` | `ja-JP` | `ja-JP → ja → neutral` |

“自动”不映射到固定区域。服务在应用初始化时快照系统 `CurrentCulture` 和 `CurrentUICulture`，用户切回“自动”时恢复该快照；系统 UI 语言仍按既有规则映射为四种有效启动器语言，供字体与语言选择器使用。

## 运行时模型

```text
保存的语言代码
       ↓
LocalizationService.SetLanguage
       ├─ 解析有效启动器语言
       ├─ 应用或恢复 CurrentCulture / CurrentUICulture
       ├─ ResourceManager 查找资源
       └─ 发布 LanguageChanged
                    ↓
        现有 LocalizedStrings.Apply 与 ViewModel 刷新
                    ↓
             已创建的 Avalonia UI 立即更新
```

手动选择同步当前线程和默认线程的两种 culture，让文本、日期、数字、货币与复合格式化一致。“自动”恢复系统 culture，不能遗留上次手动选择。

保留 `LocalizationService.T/F` 作为迁移外观：`T` 通过资源管理器查找，`F` 用当前 culture 格式化。已有 `LanguageChanged`、`LocalizedStrings.Apply`、各 ViewModel 的 `ApplyLanguage` 与字体映射保持不变。新增文本优先使用强类型资源属性；历史动态键由契约测试保护。

资源键在完整回退链中不存在、资源清单缺失或格式串非法时，测试必须失败；生产环境必须写入现有诊断和应用级错误处理，不能把键名、模板或静默英语降级展示给用户。

## 分阶段实施

1. **扩展兼容边界**：建立 culture 解析、系统 culture 快照与资源提供者边界，现有 JSON 行为不变。
2. **宽机械资源迁移**：将四个 456 键语言包转为 `.resx`，逐键和逐格式参数验证，并验证 Release 产物加载所有资源；本阶段不切换用户行为。
3. **切换运行时行为**：`T/F` 改用 `ResourceManager`，实现手动/自动 culture 策略，并复用现有事件刷新 UI。这是完整、可演示的用户可见切换。
4. **收缩与加固**：删除 JSON、Avalonia asset 加载与静默回退路径，将资源完整性、格式化和 UI 即时刷新固定为回归契约。

前 3 阶段保留兼容入口，可回退至前一可验证实现；仅在第 3 阶段完整验证通过后删除 JSON。

## 验收

- 所有 `.resx` 键集合和格式参数与中性资源一致，并可在各自 culture 下实际格式化。
- 所有生产代码的字面量 `T/F` 键存在于中性资源，遗漏键在测试中失败。
- 每种手动语言都验证两种 `CultureInfo` 与数字或日期格式；“自动”恢复启动时系统 culture。
- Headless UI 测试验证在设置或首次启动向导中切换语言后，已显示的标签、选项和对话框立即刷新。
- Debug 与 Release 均可加载四种资源，Release 输出包含必要的 satellite resources。
- 完成资源契约、相关单元测试、`dev.ps1 ui` 与 `verify.ps1`。
