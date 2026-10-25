# .NET 10 原生 MTP 测试迁移调查

调查日期：2026-10-19。本文记录迁移依据与命令接口。迁移实现与完整验证已落地：单元套件 2393 项（2378 通过 / 15 平台跳过）、Headless 238 项全通过，覆盖率行约 86.3%、分支 92.29%。

## 推荐组合

两套测试可统一使用 MTP 2，而 Headless 仍保留 xUnit 3.2.2 扩展 API。显式把包名改为 `xunit.v3.mtp-v2`：普通单元测试用 4.0.2，Headless 用 3.2.2。xUnit 从 3.2.0 起就允许选择 MTP 主版本；3.x 的 `xunit.v3` 默认 MTP 1 不代表只能运行 MTP 1。[xUnit 官方配置文档](https://xunit.net/docs/getting-started/v3/microsoft-testing-platform)

`xunit.v3.core.mtp-v2 3.2.2` 依赖同版 `xunit.v3.extensibility.core` 与 `runner.inproc.console`，平台依赖为 MTP >= 2.0.2，因此无需通过升级到 xUnit 4 来选择 MTP 2。[官方包依赖](https://www.nuget.org/packages/xunit.v3.core.mtp-v2/3.2.2)

本地验证已确认 Headless 3.2.2 + MTP v2 + TRX 可以编译，且两个 ReleaseNotes 测试运行成功。这证明基本组合可用；完整套件、覆盖率和 CI 仍需迁移后验证。

| 组件 | 建议版本与处理 |
| --- | --- |
| 测试引擎 | `Microsoft.Testing.Platform` 2.5.1；两套项目统一 |
| xUnit | 显式 `xunit.v3.mtp-v2`；普通 4.0.2，Headless 3.2.2 |
| 覆盖率 | `Microsoft.Testing.Extensions.CodeCoverage` 18.12.0（初选 `coverlet.MTP`，见下节修正） |
| 报告 | `Microsoft.Testing.Extensions.TrxReport` 2.5.1 |
| 挂起保护 | `Microsoft.Testing.Extensions.HangDump` 2.5.1 |
| VSTest 包 | 全部开发与 CI 入口迁移后，可移除 `Microsoft.NET.Test.Sdk` 和 `xunit.runner.visualstudio` |

TRX 和 HangDump 2.5.1 的包约束均为 MTP >= 2.5.1 且 < 3.0.0。[TRX 包依赖](https://www.nuget.org/packages/Microsoft.Testing.Extensions.TrxReport/2.5.1)、[HangDump 包依赖](https://www.nuget.org/packages/Microsoft.Testing.Extensions.HangDump/2.5.1)

## SDK 与项目入口

在现有 `global.json` 中增加以下顶级成员，保留已有 `sdk` 设置：

```json
"test": {
  "runner": "Microsoft.Testing.Platform"
}
```

原生入口要求 .NET 10 SDK 和 MTP >= 1.7，当前 SDK 范围满足，不需要为了 MTP 迁移升至 .NET 11。移除旧兼容入口的 `TestingPlatformDotnetTestSupport` 等设置；也移除项目显式关闭 MTP 的 `IsTestingPlatformApplication=false`。测试应用保持 `OutputType=Exe`，用 `UseMicrosoftTestingPlatformRunner=true` 统一直接执行时的 CLI。[原生入口与迁移](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-integration-dotnet-test)、[xUnit 入口设置](https://xunit.net/docs/getting-started/v3/microsoft-testing-platform)

## 命令接口

建议用显式项目选择；构建参数位于 `--` 前，测试应用参数位于其后，避免解析歧义：

```powershell
dotnet test --project tests/Cafe.Launcher.Tests/Cafe.Launcher.Tests.csproj --no-build --results-directory artifacts/test-results/unit -- --filter-class '*VersionComparerTests' --report-trx --report-trx-filename unit.trx --hangdump --hangdump-timeout 5m --hangdump-type None
```

[SDK 参数与转发规则](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-test-mtp)

| 原接口 | MTP 替代 |
| --- | --- |
| `--filter FullyQualifiedName~VersionComparerTests` | `--filter-class '*VersionComparerTests'` |
| 按方法过滤 | `--filter-method '*VersionComparerTests.MethodName'` |
| 复杂过滤表达式 | xUnit `--filter-query`；不能与 simple filters 混用 |
| `--logger trx;LogFileName=unit.trx` | `--report-trx --report-trx-filename unit.trx` |
| `--blame-hang --blame-hang-timeout 5m` | `--hangdump --hangdump-timeout 5m --hangdump-type None` |

class/method filter 接收完整限定名，`*` 支持在开头或结尾。本地两版本 `--help` 均已确认该接口；脚本可公开 `FilterClass`/`FilterMethod`，不应假装继续支持任意 VSTest 表达式。[xUnit 参数映射](https://xunit.net/docs/getting-started/v3/microsoft-testing-platform)、[查询过滤语言](https://xunit.net/docs/query-filter-language)

TRX 的 filename 只传文件名，目录用 `--results-directory`，并为两套报告使用不同目录/名称。[官方 TRX 文档](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-test-reports)

HangDump 默认 30 分钟，必须显式给 `5m`。其计时是无活动时长，非整个套件时长；`None` 不写内存 dump，但仍终止挂起进程树并使测试失败。官方 2.5.1 对应 tag 的集成测试覆盖 None 无 dmp、失败退出，以及总时长超过 timeout 但持续有测试活动时成功。[挂起参数文档](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-crash-hang-dumps)、[官方集成测试](https://github.com/microsoft/testfx/blob/v4.5.1/test/IntegrationTests/Microsoft.Testing.Platform.Acceptance.IntegrationTests/HangDumpTests.cs)

## 覆盖率及边界

实施时先按 `coverlet.MTP 10.1.0` 接入（其依赖要求 MTP >= 2.4.1，基础命令为 `--coverlet --coverlet-output-format cobertura`）。本地完整运行发现报告只有 `Cafe.Launcher.Core`、`Cafe.Launcher` 与 `Cafe.Launcher.Updater.Core`；整个 `Cafe.Launcher.UI` 缺失。Coverlet 的诊断日志给出原因：MTP 控制器在插桩前已经加载 `Cafe.Launcher.UI.dll`（测试发现会解析引用表现层的测试类型），静态插桩无法重写该文件，于是 `Skipping module ... Locked`。这不是配置能绕开的：任何被控制器提前加载的生产程序集都会消失，无法满足「四个程序集必须实际计数」的仓库契约。

最终改用 `Microsoft.Testing.Extensions.CodeCoverage 18.12.0`：它通过运行时分析器收集，不重写磁盘程序集，因此能覆盖控制器已加载的 UI。CLI 为 `--coverage --coverage-output-format cobertura --coverage-output <name>`；报告文件名固定、按套件放在独立目录。18.12.0 的包依赖要求 MTP >= 2.5.0，由仓库的 2.5.1 满足。[官方包依赖](https://www.nuget.org/packages/Microsoft.Testing.Extensions.CodeCoverage/18.12.0)

两个套件均改用 MTP 扩展后，保留仓库现有聚合 Cobertura、最低 line/branch 门槛、基线防回退和生产程序集存在性检查；包升级本身不能替代这些仓库契约。覆盖率文件名须从实际输出确认，不能继续假定旧 MSBuild 驱动的名称。控制器已加载的程序集不能再用静态插桩覆盖，这是选择运行时收集器的直接原因；不应同时装两种收集器。官方兼容表列出 CodeCoverage 18.1.x 对应 MTP 2.0.x、18.0.x 对应 MTP 1.8.x，不能随意混配不同代际。[官方覆盖率兼容表](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-code-coverage)

报告解析随之适配：Microsoft 的 Cobertura 用绝对 `filename` 且 `<sources>` 为空，`coverage.ps1` 同时接受绝对路径与旧的相对根解析；`obj/` 下的生成文件与设计器文件继续排除。

SDK 10 的项目参数、报告和过滤不需要使用文档中标明仅 .NET 11/12 可用的新选项；本次不采用全局 `--timeout` 等更高 SDK 入口能力。RID 特定的发行构建与测试执行分开验证；不要从「VSTest 可把测试项目当 Library 加载」推断「MTP 也能这样执行」。
