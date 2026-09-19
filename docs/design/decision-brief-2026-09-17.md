# 第二档候选：一页纸裁定清单（2026-09-17）

> **用途**：把候选总表 §1 里「需你裁」的第二档候选收成一页，让你一次裁完。**这不是新的候选来源**——候选仍以各所有者文档为准（评审 HTML、计划文档、`findings.json`）；本文件只是裁定会话的工作台，裁定落地后请回总表与所有者文档改状态，本文件随后归档或删除。
>
> **锚点均为 2026-09-17 重新核实的值**。登记时与今日不同的地方已在行内标出——`D13①` 的实际行号、`c09` 的零调用面、`c11` 的测试命中都不是原文所写。
>
> 供参考的现状：`findings.json` 里开放发现只剩 3 条（`SEC-001`、`SEC-002`、`ARCH-005`），全部是书面接受；**下面这批不是缺陷，是「做不做」的结构候选**。

## 建议的默认方案（若你回「全按倾向」，我就按这列执行）

| 候选 | 一句话 | 我的建议 | 规模 | 外溢 |
| --- | --- | --- | --- | --- |
| `R2-c08` | 七个模态闸口 + 11 行手工通知扫描无守卫 | **补守卫，不重开裁定** | S | 无 |
| `D17` | 三个手写 INPC 模型 | **做** | S | 公共类型基类 |
| `D7` | 卸载文件计数在两单位间往返、字面量 `2` | **做**（取「命名常量」方案，**不动文案**） | S | 无（选对方案后） |
| `D13` | 三处 XAML 重复块 | **做 ①③**；②（向导步骤帧）另议 | M | 需同步改既有契约断言 |
| `R2-c07`／`D15`／`AUD-ARCH-005` | `ShellLifecycle` 的 Wire/Unwire 手工配对 | **收敛**（Attach 记录式拆卸） | M | 退订顺序变逆序 |
| `R2-c09` | 下载模块的 `*At` 平行测试面 | **两段式**：先补暂停握手测试，再建时钟接缝，最后删零调用成员 | M | 落在下载热路径 |
| `R2-c11①` → `R2-c12` | 诊断静态入口过宽 → 注册无单一所有方 | **收窄①**，②排在①之后 | M（面广） | 机械但广 |
| `R2-c06` | 给 journey 一个构造点 | **不做** | — | — |

---

## 1. `R2-c08` 模态闸口：补守卫（建议）

- **核实**：`ModalHostViewModel.NotifyStackChanged` 是 11 行手工通知（3 个集合属性 + 8 个 `Is*Interactive`），`ModalKind` 19 个值里 7 个带闸口（另有 `IsBaseLayerInteractive`）。加一个闸口要改 6 处，漏改通知会留下「表面可见但点不动」的永久过期闸口。
- **两个选项**：**补守卫**（监听 `ModalHostViewModel.PropertyChanged`，对每个 kind 驱动一次 Open/Close，断言对应闸口确实发通知）／**重开裁定**做单一 kind 判定（会撞 `AGENTS.md` 的模态隔离裁决与 `ADR-023` 的「闸口与 AXAML 绑定不改动」）。
- **建议**：补守卫。只有在这条守卫自己也被迫依赖一份会漂移的白名单时，才值得回头重开裁定。
- **风险**：只补守卫＝低（零行为变化）。

## 2. `D17` 三个手写 INPC 模型（建议做）

- **核实**：`Models/LauncherRuntimeModels.cs` 里三处手写 INPC 与三份 `SetField`——`SelectableOption`（`:12`，抽象，`SetField(ref string…)` 在 `:38`，`SettingOption`/`LanguageOption`/`ThemeOption` 继承）、`RemoteContentItem`（`:188`，`SetField<T>` 在 `:243`）、`NewsCategory`（`:252`，`:267`）。同目录其余可观察模型**已经**派生自 `ObservableObject`（`BannerDot`、`LauncherSettings`、`ToastNotification`、`ResourcePanelItem`、`ThemeColorPaletteItem`、`GameRuntimeSettings`），基类已是承重结构。
- **注意**：`RemoteContentItem.IsImageLoading`/`IsImageLoadFailed` 是**私有 setter**，改基类时须保留；`SelectableOption` 的 `SetField` 是 `string` 专用重载，改 `[ObservableProperty]` 时属性名契约相同。
- **守卫**：补「每类一个属性的 `PropertyChanged` 名称断言」——横幅导航点不更新就是这条静默丢通知的后果。
- **规模**：S；**风险**：低（公共类型形态变化，但没有外部消费者：`RemoteContentItem` 8 处、`BannerDot` 11 处、`SelectableOption` 2 处引用，全在仓库内）。

## 3. `D7` 卸载文件计数（建议做，选「命名常量」方案）

- **核实**：`GameUninstallService.cs:193` 与 `:381` 都是 `(files.Count) + 2`，而 `GameOperationsViewModel.cs:410` 又 `Math.Max(0, validation.AffectedFileCount - 2)`——一个计数在两个单位之间往返，靠字面量 `2` 换算。
- **两个选项**：**(a) 命名常量**，`LocalInstallationFileCount = 2` 两侧共用（**零外溢**）；(b) 改计数单位报清单文件数，让 `readyToUninstall`/`uninstallConfirmText` 的 `{0}` 与之一致（**动四语 resx ＋ `confirm-dialog.png` golden**）。
- **建议**：**(a)**。这个候选的全部价值是「消掉魔数往返」，(b) 顺带改用户可见文案属于搭车，且要动 golden。若你要的是 (b)，请明说——那是另一件事，我会连带改四语与基线。
- **风险**：选 (a) 后为零；选 (b) 需要重生 golden 并复核四语文案。

## 4. `D13` 共享 XAML 重复块（建议做 ①③）

- **核实（锚点已修正）**：① 游戏管理 `MenuFlyout` 块**实际为 `MainWindow.axaml:478-515` ≡ `:544-581`**（各 38 行，去掉缩进后逐行相同；计划文档写的 `:477-516`/`:543-582` 偏移一行）；② 向导步骤帧在 `SetupWizardOverlay.axaml`（6 属性 × 5 处 + 评审行 4 处 16 行 + 单选 5 处 9 行）；③ 关于分区 `SettingsAboutSection.axaml` **只有 136 行**，五个 `about-kv-row` 在 `:51-71`，三个链接**已经**在 `ItemsControl.about-link-list` 里——计划文档 §1.4 引的 `:181-204`/`:211-239` 不存在。
- **必须同批处理**：`UiStyleContractTests.MainWindow.cs:237` 的 `Assert.Equal(2, manageButtons.Length)` **当前正是在断言这份重复**，落地时必须同提交改写成「一份控件的两处渲染」。
- **建议**：①（抽 `Controls/GameManagementActions.axaml`；注意 `MenuFlyout` 不能挂在两个所有者上，不能用共享资源）＋ ③（已基本成形，只需核对）；②另议——向导的 `<StackPanel.RenderTransform>` 由代码后置动画驱动，须保留。
- **风险**：中；**纯重构不得重生 golden**（`confirm-dialog.png` 不在本项内，但 `MainWindow` 的 golden 在）。

## 5. `R2-c07` ＝ `D15` ＝ `AUD-ARCH-005`：Wire/Unwire 收敛（建议做）

- **核实**：`ShellLifecycle.Wire()` `:433-466`（34 行、15 条 `+=`）／`Unwire()` `:574-617`（44 行、15 条 `-=`），两份手抄清单相隔约 140 行；另有 4 个只用于身份比较的委托槽（`:438-439`、`:460`、`:596` 附近）。
- **建议形态**：每条订阅包进 `Attach(attach, detach)` 并累积 `List<Action> detachAll`，`Unwire` 逆序执行后清空；顺带删掉身份比较槽与 4 个 backing 字段。**不取**「声明表」——源与事件元数异质且中间夹着模态注册块。
- **唯一行为差异**：退订顺序变为**逆序**（须在提交信息里点明）。
- **守卫**：今天没有配对守卫（唯一相关用例只覆盖 `dialogs.ConfirmUpdateAvailableRequested` 一对在 `Dispose()` 之后的行为）。落地时补「`Unwire()` 之后 `dialogs.CloseRequested`／`debug.RefreshRequested`／`operations.OpenLogViewerRequested` 三个方向都不再运行」。
- **三处登记同步**：本项裁一次，然后总表、计划文档、`findings.json`（`AUD-ARCH-005` 的处置字段）三处一起改。
- **风险**：中（`ShellLifecycle` 是 src/ 第一变更热点）。

## 6. `R2-c09` 下载节奏的时钟接缝（建议两段式）

- **核实**：`DownloadTransferThrottle` 与 `DownloadProgressAccumulator` 各自有「第二构造器 `(initialTimestamp, timestampFrequency)`」＋ 一组 `*At(long timestamp)` 平行表面（`RecordBytesAt`/`PauseAt`/`ResumeAt`/`TryRecordAt`/`ResumeAt`），生产构造器只是转发；测试对这些 `*At` 有 11 处引用，**生产侧零调用**。
- **建议顺序**（卡片自己写明的）：先补那条缺失的**暂停握手聚焦测试**（引用相等 + 共享锁，保证暂停时长不计入速度；今天只有一条真实节流下限断言），再建注入式时钟接缝，最后删掉零调用成员。
- **风险**：中（落在下载热路径上）。
- **依赖**：无（不影响 `ADR-022` 的会话内分段）。

## 7. `R2-c11①` → `R2-c12` 诊断接缝收窄（建议分两批，②后置）

- **核实（有更正）**：`ISettingsEditor` 的 `tests/` 命中**不是 0**——有 5 处，但全是 `provider.GetRequiredService<ISettingsEditor>()`（从容器取真实现），**没有任何手写替身**；`IShellRuntime` 同理（2 处，均为从容器解析/断言注册），实现者只有 `ShellLifecycle`。按 `ADR-021` 自己的标准（手写替身就是第二个适配器），两者**仍是单适配器接口**，判断不变。
- **① `LocalDiagnostics` 静态入口**：现静态调用点 **22 处**（`LogAsync`/`LogSync`）。建议分两批——先改有注入能力的约 12 个文件，静态入口只留 pre-DI（`ADR-019` 的 tier-2 决定这条静态缝只能收窄、不能删）。
- **② `R2-c12` 注册单一所有方**：`ServiceConfiguration.cs:56-62` 的 DI 工厂里 `LocalDiagnostics.RegisterSharedLogger(logger)`（每次解析都注册，多容器测试会把静态日志绑到最后构造的 provider 上）。**必须排在①之后**：今天改成「按进程先注册者胜」会直接打断测试对静态 logger 的依赖。
- **建议**：先做①（机械但面广），②在①落地后与「谁可以碰这条静态缝」的断言一起做。
- **风险**：中（面广，但每处改动机械）。

## 8. `R2-c06` 给 journey 一个构造点（建议不做）

- 卡片的首要理由（「测试要驱动规则只能把 534 行展示模块立起来」）与现状不符：`GameOperationJourneyTests` 早已直接 `new` 具体类驱动它；这个形态**落地过又删过**（`IGameOperationJourney` 于 `05e188e` 引入、`1a37370` 作为伪接口删除，理由正是「无测试替身、测试直接 new 具体类」）。
- **做的前提**：先重开 `ADR-021`（其原文把「journey 注入接口化」列为必须重新裁决的触发事件），且真正的改动是破 host 环（journey 现在拿 VM 当宿主）。
- **建议**：不做。

---

## 裁完之后我会做什么

- 逐项独立提交，每项先跑聚焦门禁、收口跑 `verify.ps1`；结构类改动做变异验证。
- 每项落地后同步三处：候选总表对应行、所有者文档（计划文档／评审结论登记／`findings.json`）、`CODEBASE_AUDIT.md` 的相关节。
- 若某项你判「不做」：写进所有者文档的否决记录（含理由），总表行改「判定不做」，不留悬空。
