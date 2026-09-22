# Linux 进程识别判据设计（P0-B 前移稿）

> 状态：**设计稿 + 实机样本已取得**（2026-09-22 采到 UMU/Proton 运行中的真实进程树；强信号全部验证，字段细节按样本修正，见 §6）· 生成时点：**2026-09-22** · 基准提交：**29375e5**
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
| 1 | 所有权标记 `CAFE_LAUNCHER_GAME_ID == gameId` | `BuildStartInfo` 写入的 env | 强 | 跨启动器重启、宿主退出后仍认；反作弊兄弟继承 env（实测 `GAMEID`/`WINEPREFIX` 已透传，自有标记同理） |
| 2 | `STEAM_COMPAT_DATA_PATH == prefix`，或 `WINEPREFIX` 以该 prefix 为根 | env | 强（可能跨游戏误认，见 §4） | env 透传但标记被清洗时。**注意内层进程的 `WINEPREFIX` 是 `<prefix>/pfx/`，等值比较会失败**（§6） |
| 3 | 映射了安装目录下的文件 | `maps` | 强 | 直接对应「占着待删目录」；ADR-032 留的门（实测游戏进程 maps 含游戏目录内的 PE/DLL，Unix 路径） |
| 4 | `comm` 落在名字家族 | 现状 | 弱（截断 + 命名约定） | 与 Windows 兼容的回退：仅 ≤15 字符的名字可用（`BlueArchive.exe` 恰好 15 字符故幸存，长宿主名会截断） |

`cmdline` 的 basename 家族匹配**故意不参与判定**（只用于显示名，见 §5）：它会把「某个编辑器恰好打开了游戏 exe 的路径」也认成游戏，是 Windows 名字判据没有的新误报类别。实测 pressure-vessel **未重写**游戏进程的 argv（保留 `S:\...\BlueArchive.exe` 的 Windows 形态），所以它作为显示名可靠；若将来要参与判定，须先加安装目录/prefix 佐证。

## 4. 失败方向与误报边界

- 无法确定 = 没在跑（`ProcessService.cs` 的放行语义不变）；唯一例外仍是扫描超时（ADR-032 决策 11）。
- 每条信号都是正向识别，因此「没命中」不报错、不拒绝。
- 误报必须为 0：无关长进程名、其它 prefix 下的同族进程、`argv` 里提到游戏路径的无关进程，都不得被认领（守卫见 `UnixProcessRecordsTests`）。
- 信号 2 的已知边界：用户手配 `PrefixPath` 时两个游戏可能共用一个 prefix，此时 prefix 命中不等于本游戏。落地时要么要求 prefix 路径里带 gameId（默认路径本就带），要么只在信号 1 缺失时降级使用。

## 5. 显示名

判定依据不总是给出名字（标记 / prefix 只回答「归属」）。展示优先用不截断来源：`argv` 里的游戏可执行文件 basename → `maps` 里安装目录下的文件名 → `comm`。最终仍经 `GameProcessNames.DescribeForDisplay` 补 `.exe`。

## 6. 实机样本结论（2026-09-22）

**环境**：Arch Linux；游戏在 NTFS3（`/dev/sdb1`，`rw,nosuid,nodev,...,uid=1000`，非 noexec）；前缀在 home 盘的 XDG 数据家；UMU 1.4.4，实际 Proton 为 `UMU-Proton-10.0-4`；启动器 1.1.0-beta.10。预检报告 `distribution=Arch Linux`、`caseSensitive=true`、0 findings。游戏数据根设定为 `gamePath=/run/media/gytxtx/Games/YostarGames/BlueArchive_JP`。

**进程树**（UMU 启动后、游戏在跑）：

| 节点 | comm | 关键事实 |
| --- | --- | --- |
| `umu-run` | `umu-run` | 宿主；env 里 `WINEPREFIX` = **精确前缀**、`GAMEID=blue-archive-jp` |
| `srt-bwrap` / `pv-adverb` | 全名（>15） | pressure-vessel；env 增 `PROTONPATH=<实际构建>`、`STEAM_COMPAT_DATA_PATH=<精确前缀>` |
| `proton` | `python3` | Proton 入口 |
| `umu.exe` | `umu.exe` | wine 侧 shim；`WINEPREFIX=<前缀>/pfx/` |
| `BlueArchive.exe` | `BlueArchive.exe` | **游戏 PE 是独立节点**；cmdline `S:\YostarGames\BlueArchive_JP\BlueArchive.exe`；maps 含游戏目录内的 PE/DLL |
| `xxd-0.xem` | `xxd-0.xem` | XignCode 模块，独立节点 |
| 其它 | `winedevice.exe` / `xalia.exe` / `UnityCrashHandl`（截断）/ `CrBrowserMain` 等 | 辅助进程 |

配置声明的宿主 `xldr_BlueArchiveOnline_JP_loader_x64` **未作为存活节点观察到**（loader 已退出），与 ADR-032「宿主提前退出」一致。

**逐题回答**：

| 问题 | 结论 |
| --- | --- |
| 我们的 env 是否经 UMU / pressure-vessel 透传 | **是**。`WINEPREFIX`/`GAMEID`/`PROTONPATH`/`UMU_ID`/`STEAM_COMPAT_DATA_PATH` 都在 wine 侧进程上；`GAMEID` 一路保留为 `blue-archive-jp` |
| 游戏 PE 是否独立节点 | **是**，`BlueArchive.exe` 是独立 `/proc` 节点，且有 maps |
| 宿主退出后剩什么 | loader 已退出；UMU 宿主仍在。PE 独立成节点意味着判据不依赖宿主存活 |
| `maps` 路径形态 | **Unix 形态**（`/run/media/.../BlueArchive.exe`），可做安装目录归属 |
| `cmdline` 是否被重写 | **否**，保留 Windows 形态（`S:\...\BlueArchive.exe`） |
| `WINEPREFIX` 能否等值匹配 | **不能**：内层是 `<前缀>/pfx/`；须用 `STEAM_COMPAT_DATA_PATH` 或按前缀根匹配 |
| `comm` 够不够用 | 仅 ≤15 字符名（`BlueArchive.exe` 恰好 15）；长名（宿主 / Chromium helper）截断 |

**对 P0-C 的修正**：`runner_output.log` 实测为 **0 字节**——UMU 不往 stdout/stderr 写东西。实际 Proton 构建应从进程 env 的 `PROTONPATH` 读（或 UMU 自己的日志），不能指望运行器输出。

**仍未验证**：正常退出 + 再次启动（P0-A 第 4、5 步）；「运行器宿主退出、游戏仍在跑」这一档的进程表。

## 7. 已否决方案

- **只修 `comm` 截断**：内核截断无法回避；换 `cmdline` 又引入误报（§3），留作回退。
- **纯名字相似度**：与「游戏在跑」相关性弱，Linux 上不必迁就。
- **进程树追踪**：Wine 与 Windows 一样会 reparent，断链。
- **只用 `maps`**：安装目录未知或未映射时全瞎，且读 maps 有开销。

## 8. 产物、落地与后续

**已落地（2026-09-22）**：

- `BuildStartInfo` 写入私有所有权标记 `CAFE_LAUNCHER_GAME_ID`；`LinuxProcessScanner` 扫 `/proc` 认标记，`ProcessService` 在 Linux 走它；`comm` 家族名退为回退。判定与展示分离（标记 / 前缀 / maps / comm 决定在不在跑，家族名决定怎么报）。
- ADR-036 记录本判据与「Windows 名字家族 / Linux 所有权标记」的有意分叉。
- 纯函数骨架（`UnixProcessRecord`、`UnixGameProcessMatcher`）已接入扫描器，不再是脱离调用方的骨架；`tests/.../UnixProcessRecordsTests.cs`、`LinuxProcessScannerTests` 覆盖。

**后续**：

1. **前缀归属**：`maps`（安装目录）已接入 `RunningGameQuery` 并由扫描器读取；`STEAM_COMPAT_DATA_PATH` 等值 / `WINEPREFIX` 根匹配尚未接（标记已覆盖启动器发起的会话，边际收益小）。
2. **诊断侧已落地**：`LinuxProcessScanner.TryReadRunningProtonBuild` 从带标记进程的 env 读 `PROTONPATH`，写入 `system-info.json` 的 `runningProtonBuild`（`runner_output.log` 实测为空）。
3. macOS 未纳入。

## 9. 门禁

`.\verify.ps1`；改 XAML / 资源时另加 `UiStyleContractTests` / `Test-LocalizationContract.ps1`（本步都不涉及）。
