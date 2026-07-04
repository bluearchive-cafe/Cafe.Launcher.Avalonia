# 覆盖率优化设计

## 目标

建立可重复执行的项目级覆盖率流程，并使手写 C# 的行覆盖率与分支覆盖率均不低于 70%。

## 当前状态

- `Cafe.Launcher.Avalonia.Tests` 引用了 `coverlet.collector`，现有 Cobertura 报告只包含该测试项目。
- `Cafe.Launcher.Avalonia.HeadlessTests` 未引用 `coverlet.collector`，其中的窗口、托盘、转换器和焦点行为测试未进入现有覆盖率报告。
- Avalonia 编译生成的 `.axaml` 执行代码和 `obj` 生成代码进入现有报告，显著降低总行覆盖率。
- 现有代码已经通过 `ISystemTrayPlatform` 隔离托盘平台实现，并在 Headless 测试中使用测试实现。

## 覆盖率执行与合并

新增统一的覆盖率配置和 PowerShell 入口脚本。脚本依次运行以下两个测试项目，并为每个项目生成 Cobertura 报告：

- `tests/Cafe.Launcher.Avalonia.Tests/Cafe.Launcher.Avalonia.Tests.csproj`
- `tests/Cafe.Launcher.Avalonia.HeadlessTests/Cafe.Launcher.Avalonia.HeadlessTests.csproj`

脚本读取两个报告，以源文件路径和行号合并行命中数据；同一源代码行只计算一次，只要任一测试项目命中即视为已覆盖。分支数据按 Cobertura 报告提供的分支总数和命中数合并，避免把两个测试程序集对同一生产程序集的统计简单相加。

输出必须包含：

- 两个测试项目各自的测试结果；
- 手写 C# 行覆盖率、已覆盖行数和有效行数；
- 手写 C# 分支覆盖率、已覆盖分支数和有效分支数；
- Cobertura 报告的确定路径；
- 阈值检查结果。

任一测试失败、报告缺失、报告结构不符合预期，或任一覆盖率指标低于 70% 时，脚本返回非零退出码。

## 覆盖范围

覆盖率统计只包含仓库中的手写 `.cs` 文件。以下内容不参与阈值计算：

- `.axaml` 编译生成代码；
- `obj` 下的生成代码；
- 编译器生成且没有独立手写源文件语义的代码。

排除规则只用于覆盖率统计，不跳过对应测试，也不排除 `Views/*.axaml.cs`、Windows 平台服务或其他手写 C#。

## 测试补强顺序

在统一报告建立后，以合并报告中的精确未覆盖分支为依据补测，顺序如下：

1. `GameDownloadService` 的失败、重试、暂停、取消和清理分支；
2. `RemoteHttpUrlValidator` 的协议、端口、用户信息、本地主机、字面量地址、DNS 与代理分支；
3. `GameLaunchService` 和 `GameUninstallService` 的验证失败、进程状态、文件状态和异常分支；
4. `ThemeColorExtractionService` 的颜色归并与边界输入分支；
5. `SettingsAppearanceViewModel`、`SettingsViewModel`、`MainWindowViewModel`、`ResourcePanelViewModel` 等 ViewModel 的命令和状态转换分支。

每个新增或变更的生产行为都采用测试先行：先增加会因缺少目标行为而失败的测试，再实施最小生产代码。纯覆盖缺口且现有行为正确时，只增加能够验证真实行为的测试，不为了覆盖率修改生产语义。

## 平台交互边界

沿用现有 `ISystemTrayPlatform`，不重复创建托盘抽象。Headless 测试继续验证 `SystemTrayService` 与窗口、菜单文本和平台实现之间的协作。

对注册表、原生窗口和应用生命周期代码，先使用现有可注入入口或 Headless 环境测试。只有测试证明当前静态或原生依赖无法隔离时，才增加职责单一的最小接口，并通过依赖注入接入生产实现。不得为了提高数字加入测试专用生产方法。

## 验证

实现完成后执行：

1. 新增的项目级覆盖率脚本；
2. `.\verify.ps1`；
3. 再次执行项目级覆盖率脚本，确认最终报告中的手写 C# 行覆盖率与分支覆盖率均不低于 70%。

覆盖率报告和 `TestResults` 保持为本地构建产物，不提交到仓库。

## 非目标

- 不要求 XAML 生成代码达到覆盖率阈值；
- 不以删除或排除手写业务代码的方式提高覆盖率；
- 不改变现有启动器业务行为；
- 不引入仅用于生成 HTML 报告的额外依赖；
- 不修改 CI，除非现有仓库配置中存在明确的覆盖率执行入口需要同步。
