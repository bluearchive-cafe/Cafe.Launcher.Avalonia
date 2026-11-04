# ADR-053：Shell 跨功能接线收拢为呈现接线模块

- 状态：✅ 已接受
- 日期：2026-11-04
- 相关：ADR-023（模态注册声明式收敛）、ADR-042（Core 与 UI 程序集分层）、ADR-043（程序集与命名空间同名）

## 背景

`ShellLifecycle` 同时承担生命周期编排与全部跨功能订阅：21 组 `Attach` 配对、启动模块接线、订阅可见性旗标与模态注册混在同一段 `Wire`/`Unwire` 里。订阅的录入点与退订点仍由同一个类维护，但「哪些协作者互相监听」这一结构知识被埋在生命周期方法中间；测试只能靠 `Unwire` 的行为结果间接验证这张表，无法把它当作一个独立单元替换或检查。ADR-023 已把模态注册表移出，但 attach 表仍在原处。

## 决策

1. 新增 `src/Cafe.Launcher.UI/Features/Shell/ShellPresentationWiring.cs`（`internal sealed`，`IDisposable`）：唯一持有 attach 表、退订记录、`ModalRegistrar` 生命周期与 `ShellStartup` 接线，暴露 `Wire()`/`Unwire()`/`TryDispatchEscape()`。
2. 接线模块只依赖 `ShellPresentationFamily`、`ISavedSettingsFollowThrough`、`ShellStartup`、`LocalizationService` 与 `ShellWiringActions`；**不反向依赖 `ShellLifecycle` 的具体类型**。需要壳状态、写入方或停机编排的动作经 `ShellWiringActions` 的委托聚合传入。
3. `ShellLifecycle` 保留生命周期动词（刷新、设置落盘、恢复默认、源切换、自更新编排、外观预览）与 `StatusDetailModeChanged` 事件；`Wire`/`Unwire`/`TryHandleEscape` 退化为对接线模块的委托。
4. **装卸顺序逐字不变**：登记顺序即逆序退订顺序；`Unwire` 先释放模态注册，再逆序执行记录表，最后退订启动模块。模态注册表仍由 `ShellModalCatalog` 按 ADR-023 的栈序产出。
5. 只移动接线，不改处理器语义：日志查看、外部链接、资源面板确认等纯呈现动作随表移入接线模块；设置落盘与刷新等仍由壳协调者执行。

## 备选方案

- **保留在 `ShellLifecycle`**：订阅结构继续与生命周期动词混排，新增协作者时仍要在 800 行文件里找插入点。否决。
- **把处理器实现整体搬进接线模块**：会把设置写入、刷新与自更新编排一并迁走，接线模块变成第二个壳协调者。否决。
- **接线模块持有 `ShellLifecycle` 引用**：方向反转成双向依赖，接线表无法脱离壳协调者测试。否决。
- **同时用真实注册重建测试 fixture**：属于测试组合根的独立议题；本次只搬家，避免把行为验证和测试设施改造混在一个改动里。暂缓。

## 后果

- 跨功能订阅只有一张表、一个装卸点；新增协作者只改 `ShellPresentationWiring`（及其动作聚合），不再扩散生命周期方法。
- `ShellLifecycle` 的公开表面不变（`Wire`/`Unwire`/`TryHandleEscape` 保留为委托），现有调用方与测试无需迁移。
- 现有 `Unwire_AfterWired_StopsCrossFeatureEventFlow` 继续作为退订行为的回归守卫；不需要新的行为基线。
- 测试 fixture 仍手工搭建约 15 个协作者，与组合根的并行维护问题未在本决策内解决。
