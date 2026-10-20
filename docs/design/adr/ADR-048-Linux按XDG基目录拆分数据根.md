# ADR-048：Linux 按 XDG 基目录拆分启动器数据根

- 状态：**已撤销**（2026-10-20：发布前复核认为四类根的复杂度高于收益，由 [ADR-049](ADR-049-Linux保留单一数据根并统一小写目录名.md) 收回拆分；小写目录名与注入式 `GameCompatibilityPaths` 保留）
- 日期：2026-10-20
- 来源：用户要求核对并修复 XDG 规范符合性
- 相关：[ADR-025](ADR-025-数据根由组合根解析并注入.md)（数据根解析一次并注入）、[ADR-034](ADR-034-Unix单实例所有权用数据根内锁套接字判定.md)（Unix 单实例套接字）

## 背景

启动器原先在所有平台都只有一个数据根：Windows/macOS 为 `%LOCALAPPDATA%\Cafe Launcher`，Linux 由 .NET 的 `LocalApplicationData` 映射到 `$XDG_DATA_HOME/Cafe Launcher`（未设置时为 `~/.local/share/Cafe Launcher`）。设置、下载检查点、公告已读状态、日志、崩溃快照、日志导出、图片缓存与兼容前缀全在同一目录里。

这带来三个问题：

1. **目录名不符合 Linux 习惯**：大写与空格来自 Windows 产品目录名；Linux 的 XDG 应用通常使用小写连字符名。旧版兼容前缀助手已经硬编码使用 `$XDG_DATA_HOME/cafe-launcher`，于是同一款启动器在 Linux 上占两个拼写不同的目录。
2. **没有按 XDG 分类**：可重建的图片缓存应放在 `XDG_CACHE_HOME`，设置应放在 `XDG_CONFIG_HOME`，日志与运行状态应放在 `XDG_STATE_HOME`；把它们都塞进 `XDG_DATA_HOME` 会让备份、清理和只读配置目录的策略失效。
3. **兼容前缀仍绕过注入的数据根**：`GameCompatibilityPaths` 是静态助手，Windows 分支自行解析进程根、Unix 分支自行读 XDG 数据主目录，是 ADR-025 声明表里的最后一处例外。

Windows 与 macOS 的现有布局没有同样的问题，本决定不改变它们的行为。

## 决策

1. **Linux 使用四个 XDG 根**，目录名统一为产品名的小写连字符形式（`Cafe Launcher` → `cafe-launcher`）：
   - `$XDG_DATA_HOME/cafe-launcher`：受管兼容环境（`compatibility/<gameId>/<runnerId>/prefix`）等不可重建的数据。
   - `$XDG_CONFIG_HOME/cafe-launcher`：`settings.json`。
   - `$XDG_STATE_HOME/cafe-launcher`：`unified.log` 及轮转文件、`download_state.json`、`shown_notices.json`、`runner_output.log`、`compatibility_environment.json`、`prefix_metadata.json`、`CrashReports/`、日志导出。
   - `$XDG_CACHE_HOME/cafe-launcher`：`image-cache/`。
2. **回退值遵循 XDG 规范**：空值或相对路径不被采用；未设置时分别回退到 `~/.local/share`、`~/.config`、`~/.local/state`、`~/.cache`。`XDG_DATA_HOME` / `XDG_CONFIG_HOME` 继续由 .NET 的 `LocalApplicationData` / `ApplicationData` 映射并校验；`XDG_STATE_HOME` / `XDG_CACHE_HOME` 在 `LauncherDataRoot` 内按同一规则解析。
3. **`LauncherDataRoot` 仍是布局的唯一出处**：新增 `ConfigRoot`、`StateRoot`、`CacheRoot` 与 `LogDirectory`，知名路径按类别派生。单参构造器让四类根相同，测试与 Windows/macOS 的既有语义不变；四参构造器只供进程解析与测试使用。
4. **`GameCompatibilityPaths` 改为实例类**，构造时接收注入的 `LauncherDataRoot`，在 `AddLauncherCore` 中登记为单例；`GameRuntime`、`GameUninstallService` 与 `ResourcePanelUidService` 通过构造器接收它。ADR-025 的进程根声明表因此只剩 `LauncherDataRoot.cs`、组合根、`Program.cs` 与 `CrashReportApp.axaml.cs`。
5. **一次性迁移旧 Linux 布局**：进程首次解析数据根时，若旧根 `$XDG_DATA_HOME/Cafe Launcher` 存在，则把 `settings.json`（含其 `.tmp` 变体）移到配置根、`image-cache` 移到缓存根、`compatibility`/`update` 保留在数据根、其余条目移到状态根。迁移逐项进行、不覆盖新位置已有同名内容；失败条目留在旧根，旧根只在清空后删除。运行期套接字（`cl-signal-*.sock`）不迁移。迁移只在 Unix 生产路径上尝试一次，测试覆盖目录不触发。
6. **运行期套接字保留在数据根**，不迁到 `$XDG_RUNTIME_DIR`。这是 ADR-034 已记录的取舍：套接字与数据根所有权绑定，跨 POSIX 会话的去重语义已由内核原子锁保证；改到 runtime 目录需要同时重做单实例与转发协议，超出本次布局修复的范围。

## 被否决的替代方案

**只在 Linux 改目录名、不拆分类**：改动最小，能解决大小写重复，但设置、缓存与状态仍混在一起，XDG 的核心收益（配置可备份、缓存可删除、状态可诊断）没有拿到。

**把设置留在数据根、只拆缓存与日志**：可以减少 `Open Data Directory` 按钮的困惑，但 `settings.json` 正是 `XDG_CONFIG_HOME` 最典型的成员；保留例外会让“数据根”继续承担三种语义。

**迁移时用符号链接兼容旧路径**：Linux 上旧版本进程仍可能运行，链接会让两个版本同时写同一份设置；一次性移动并在新位置继续更可预测。

**把 Unix 套接字迁到 `$XDG_RUNTIME_DIR`**：规范上更纯，但会与 ADR-034 的锁语义、跨进程转发信号和现有测试同时冲突；记录为已知偏差，而不是顺手改掉。

## 后果

- Linux 用户升级后首次启动会自动迁移旧目录；若新旧位置存在同名项，新位置优先，旧副本保留在旧根，不静默覆盖。
- 兼容前缀位置不变（旧版助手本来就用 `cafe-launcher`），因此不需要移动 Wine/Proton 前缀。
- `LauncherDataRootTests` 钉住四类根的落点、Unix 目录名与 XDG 回退规则；`LauncherDataRootMigrationTests` 钉住迁移的分类、不覆盖、幂等与套接字保留。
- `TestUserDataIsolationTests` 的进程根声明表缩减为四处，测试覆盖目录仍让四类根全部落在同一个隔离目录。
- `LogExportService`、`CrashReportWindowViewModel` 与两个日志器构造点统一消费 `LogDirectory`/`StateRoot`，日志与崩溃快照不再从数据根旁路读取。
