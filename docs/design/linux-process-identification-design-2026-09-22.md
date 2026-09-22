# Linux 进程识别判据设计（P0-B 前移稿）

> 状态：**设计稿**（判据的 ABI 层与强信号已定，弱信号与具体字段序待实机样本收口）· 生成时点：**2026-09-22** · 基准提交：**29375e5**
> 来历：`linux-support-plan-2026-09-22.md` §3（P0-B）选定「所有权标记为主」的方向后，把「能先于样本定稿的一半」抽出来：内核 ABI 决定的原始字段读取与纯判定骨架先落地，具体命中策略等 §3.1 的进程树样本回来再定。
> 本文**不替代** `linux-support-plan-2026-09-22.md` 的排序：P0-A 仍是 P0-B 的实机前置；本文只把 P0-B 的「设计」部分前移。

## 1. 范围

- 只做 Linux。macOS 没有 `/proc`，维持现状（fail-open，不误报也不拦），在落地 ADR 里写明。
- 只写「纯判定 + 原始字段解析」，**不接平台读取层、不改 `IGameProcessTracker` 契约**（接线见 §8）。
- 不做：Prefix 自动迁移、运行器架构扩展、Windows 侧判据改动。

## 2. 为什么 Linux 可以换一种判据

ADR-032 的判据落在「名字家族」，前提是 **Windows 反作弊保护了镜像路径**（`ExecutablePath` 全为 null），只剩名字可用。该 ADR 的「被否决的替代方案」留了门：Wine/UMU 若不保护进程，镜像路径这条路可以重开。Linux 上 `/proc` 不保护这些字段，于是可用信号比 Windows 多：

| 字段 | 内容 | 是否截断 |
| --- | --- | --- |
| `/proc/<pid>/comm` | 进程名（内核 15 字符） | 是 |
| `/proc/<pid>/cmdline` | 完整 argv（NUL 分隔） | 否 |
| `/proc/<pid>/environ` | 启动时的完整环境（NUL 分隔，仅同 uid 可读） | 否 |
| `/proc/<pid>/maps` | 映射文件路径 | 否 |

判据因此从「名字像不像」升级为「归不归这次游戏会话」。

## 3. 信号阶梯

按「正向识别」排序，任一命中即认；全部不中即「没在跑」（fail-open）。

| 序 | 信号 | 来源 | 强度 | 覆盖 |
| --- | --- | --- | --- | --- |
| 1 | 所有权标记 `CAFE_LAUNCHER_GAME_ID == gameId` | `BuildStartInfo` 写入的 env | 强 | 跨启动器重启、宿主退出后仍认；反作弊兄弟继承 env |
| 2 | `WINEPREFIX == 启动器为该游戏选的 prefix` | env | 强（可能跨游戏误认，见 §4） | env 透传但标记被清洗时 |
| 3 | 映射了安装目录下的文件 | `maps` | 强 | 直接对应「占着待删目录」；ADR-032 留的门 |
| 4 | `comm` 落在名字家族 | 现状 | 弱（截断 + 命名约定） | 与 Windows 兼容的回退 |

`cmdline` 的 basename 家族匹配**故意不参与判定**（只用于显示名，见 §5）：它会把「某个编辑器恰好打开了游戏 exe 的路径」也认成游戏，是 Windows 名字判据没有的新误报类别。是否启用、如何加约束（如必须与 runner / `WINEPREFIX` 佐证）留待样本，见 §6。

## 4. 失败方向与误报边界

- 无法确定 = 没在跑（`ProcessService.cs` 的放行语义不变）；唯一例外仍是扫描超时（ADR-032 决策 11）。
- 每条信号都是正向识别，因此「没命中」不报错、不拒绝。
- 误报必须为 0：无关长进程名、其它 prefix 下的同族进程、`argv` 里提到游戏路径的无关进程，都不得被认领（守卫见 `UnixProcessRecordsTests`）。
- 信号 2 的已知边界：用户手配 `PrefixPath` 时两个游戏可能共用一个 prefix，此时 prefix 命中不等于本游戏。落地时要么要求 prefix 路径里带 gameId（默认路径本就带），要么只在信号 1 缺失时降级使用。

## 5. 显示名

判定依据不总是给出名字（标记 / prefix 只回答「归属」）。展示优先用不截断来源：`argv` 里的游戏可执行文件 basename → `maps` 里安装目录下的文件名 → `comm`。最终仍经 `GameProcessNames.DescribeForDisplay` 补 `.exe`。

## 6. 待样本收口的问题（映射到信号）

| 问题 | 影响的信号 |
| --- | --- |
| 我们写入的 env 是否经 UMU / pressure-vessel / Proton 存活到游戏与反作弊进程 | 1、2 |
| 游戏 PE 是独立 `/proc` 节点，还是只映射在 wine 宿主里 | 3、4 |
| 宿主退出后剩下的那个节点长什么样 | 1、3、4 |
| `maps` 里路径是 Unix 形态还是保留 Windows 形态 | 3 |
| `cmdline` 是否被 pressure-vessel 重写 | 决定 §3 的 `cmdline` 是否可启用 |

## 7. 已否决方案

- **只修 `comm` 截断**：内核截断无法回避；换 `cmdline` 又引入误报（§3），留作回退。
- **纯名字相似度**：与「游戏在跑」相关性弱，Linux 上不必迁就。
- **进程树追踪**：Wine 与 Windows 一样会 reparent，断链。
- **只用 `maps`**：安装目录未知或未映射时全瞎，且读 maps 有开销。

## 8. 本步产物与后续

本步（纯函数，**无生产调用方，刻意如此**）：

- `Services/GameRuntime/UnixProcessRecord.cs`：`/proc` 原始字节 → 字段的纯解析（ABI 层，样本无关）。
- `Services/GameRuntime/UnixGameProcessMatcher.cs`：信号 1–4 的纯判定与显示名。
- `tests/.../UnixProcessRecordsTests.cs`：合成记录表驱动，含误报反例。

后续（等 P0-A 样本）：

1. 补平台读取层（`OperatingSystem.IsLinux()` 门控，先廉价过滤再读 `environ` / `maps`）。
2. `BuildStartInfo` 写入 `CAFE_LAUNCHER_GAME_ID`；`IGameProcessTracker` 的查询从「名字」扩为「名字 + gameId + prefix + 安装目录」。
3. 按样本确定信号 2/3/4 的去留与约束，写落地 ADR（暂定 ADR-036），把本文 §3、§7 收进去。

## 9. 门禁

`.\verify.ps1`；改 XAML / 资源时另加 `UiStyleContractTests` / `Test-LocalizationContract.ps1`（本步都不涉及）。
