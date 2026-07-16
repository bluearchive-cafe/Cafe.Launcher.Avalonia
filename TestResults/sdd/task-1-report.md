# Task 1 实施报告：四语术语基线与本地化契约

## 状态

DONE

## 改动

- 扩充 `UBIQUITOUS_LANGUAGE.md`：补齐英语、简体中文、繁体中文、日语标准译法；新增三种代理模式的语义边界、自然译名与必要原词规则、保留词和禁用别名。
- 在四份 locale 中新增并保持同序的正式键：`languageAuto`、`localizedResources`、`manifest`。
- 将中文 `banner`/`banners` 统一为“横幅/橫幅”，并移除前后横幅文案中的“活动/活動”。
- 将简繁中文 `logFilterFatal` 与 `logLevelFatal` 统一为“致命”。
- 将 `launchCheck`、`resourcePanel`、Manifest、Localized resources 对齐四语标准值；同步 Resource Panel 相关自有文案，未改动态服务端内容。
- 重写三种代理模式名称、说明和提示，使自动检测、无代理直连、显式系统代理彼此可区分并符合真实网络行为。
- 自动语言显示统一读取 `languageAuto`；覆盖设置选择器、首次设置语言选择器及向导摘要，保留原有无参 `GetLanguageOptions()` 公共入口。
- 新增 `LocalizationTerminologyTests`，覆盖四语标准值、横幅、Fatal、Manifest 多键一致性、代理语义及自动语言显示；更新旧 Resource Panel 契约期望。
- 未删除 locale 键，未新增依赖，未修改业务、网络、持久化或外部 API 行为。

## TDD 证据

### RED 1：术语基线

命令：

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~LocalizationTerminologyTests" --no-restore
```

结果：退出码 1；19 项中 15 项失败、4 项通过。失败原因符合预期：缺少 `manifest`/`localizedResources`，中文横幅与 Fatal 冲突，代理旧文案不准确，Manifest 状态标签不一致，向导摘要实际为 `语言 (Auto)`。

### RED 2：两个语言选择器

命令：

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~LocalizationTerminologyTests.LanguageSelectors_AutomaticOption_UsesLocalizedLocaleKey" --no-restore
```

结果：退出码 1；1 项失败。期望“自动（跟随系统语言）”，实际为固定英文 `Auto`。

### GREEN

命令：

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~LocalizationTerminologyTests|FullyQualifiedName~LocalizationServiceTests" --no-restore
.\scripts\Test-LocalizationContract.ps1
```

结果：退出码 0；聚焦测试 53/53 通过；本地化契约脚本退出码 0。键集合、Ordinal 键顺序和复合格式占位符保持一致。

## 自审

- `git diff --check` 与 `git diff --cached --check` 均为退出码 0。
- 四份 JSON 的三个新增键位置一致；现有 `LocaleFiles_KeepKeysSortedOrdinal`、键集合及占位符测试通过。
- 引用检索确认生产代码不再拼接固定 `(Auto)`，旧 Resource Panel 别名和中文活动横幅只保留在术语表的禁用别名说明中。
- 未执行全量 `verify.ps1`；任务简报要求的聚焦测试和本地化契约均已执行。

## 提交

- 实现提交：`c9feafb`（`fix(localization): 统一界面术语与四语文案`）
- 本报告将在后续独立文档提交中加入，避免在同一提交内容中形成无法解析的自引用哈希。

## 遗留疑虑

- 未发现功能性遗留疑虑。
- 本次未运行全量 `verify.ps1`，因此报告仅声明任务要求范围内的验证结果。
