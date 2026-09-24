# ADR-036：Linux 的游戏进程按启动器所有权标记识别，名字家族退为回退

- 状态：✅ 已接受
- 日期：2026-09-22
- 来源：计划 [`linux-support-plan-2026-09-22.md`](../linux-support-plan-2026-09-22.md) §3（P0-B）——「更新/修复/卸载的运行中保护在 Linux 上实际失效」
- 相关：ADR-032（游戏进程按名字家族识别）、ADR-035（启动后看护）、判据设计稿 [`linux-process-identification-design-2026-09-22.md`](../linux-process-identification-design-2026-09-22.md)

## 背景

ADR-032 的「已知限制」写明：Unix 上 `Process.GetProcesses()` 的 `ProcessName` 取内核 `comm`（15 字符封顶），`xldr_BlueArchiveOnline_JP_loader_x64` 与 `xldr_BlueArchiveOnline_JP` 截成同一串，等值与「以 `_` 为界续写」两条规则都不成立——发布 linux-x64 后，Wine/UMU 安装实际上没有这道闸门（不误报，但也不拦）。ADR-032 同时留下门：若某个运行环境不保护进程，镜像路径那条路可以重开。

2026-09-22 的实机样本（Arch Linux，UMU 1.4.4 + UMU-Proton-10.0-4，判据设计稿 §6）给出了 Linux 上真正可用的信号：

- 启动器写入的环境变量经 pressure-vessel/Proton **存活到整族进程**：`WINEPREFIX`/`GAMEID`/`PROTONPATH`/`UMU_ID`/`STEAM_COMPAT_DATA_PATH` 都在 wine 侧进程上，`GAMEID=blue-archive-jp` 一路保留。
- 游戏 PE（`BlueArchive.exe`）是**独立的 `/proc` 节点**，`cmdline` 保留 `S:\...\BlueArchive.exe`（pressure-vessel 未重写 argv），`maps` 含游戏目录内的 PE/DLL（Unix 路径）。
- 配置声明的宿主 loader 已退出；反作弊 XignCode（`xxd-0.xem`）是独立节点——与 ADR-032「宿主提前退出」一致。

## 决策

1. **启动器写入一个私有的所有权标记**：`BuildStartInfo` 给运行器进程环境加 `CAFE_LAUNCHER_GAME_ID=<gameId>`。标记随 UMU/Proton 传给整族，因此「这次启动的游戏会话」可由所有权回答，而不是由名字相似度回答。
2. **Linux 扫描 `/proc`**（`LinuxProcessScanner`）：先按 `comm`/`cmdline` 找候选（家族名或 PE 参数），只对候选读 `environ` 认标记。判定由 `UnixGameProcessMatcher` 给出；名字家族（`comm`）退为回退。
3. **判定与展示分离**：matcher 决定「在不在跑」（标记 / 前缀 / `maps` / `comm`）；展示名优先家族名（`argv`/`comm` 里能对上家族的 basename），标记命中但名字对不上的辅助进程只在没有家族名时兜底。
4. **失败方向不变**：读不到 `environ`（不同 uid）按没有标记；枚举失败按「没在跑」放行；只有扫描超时仍按「无法确认」拒绝（ADR-032 决策 11）。
5. **平台有意分叉**：Windows 保持 ADR-032 的名字家族判据——那里的反作弊保护镜像路径、读不到镜像；Linux 的 `/proc` 不保护这些字段，于是用所有权。两者不是同一套规则的退化，而是各自环境下的最优解。
6. **`maps` 归属已接入，前缀仍待接**：`IGameProcessTracker` 的查询扩为 `RunningGameQuery`（家族名 + 安装目录）；Linux 扫描对候选读 `maps`，命中「映射着安装目录内文件」即认。这覆盖了标记不在场的外部启动（官方启动器 / 桌面脚本）。前缀（`STEAM_COMPAT_DATA_PATH` 等值 / `WINEPREFIX` 根匹配）尚未接——标记已覆盖启动器发起的会话，前缀的边际收益小。
7. **标记按「存在且非空」判定归属**：标记名是启动器私有的，别的软件不会写；`query.GameId` 给定值时才要求等值（多游戏场景的收窄）。

## 被否决的替代方案

- **只修 `comm` 截断，或改用 `cmdline` 名字匹配。** `comm` 截断由内核决定、无法回避；改用 `cmdline` 会引入 Windows 名字判据没有的误报类别（某个编辑器恰好打开了游戏 exe 的路径），且回答的仍是「名字像不像」而不是「归不归这次会话」。
- **只按 `WINEPREFIX` 等值匹配。** 实测内层 `WINEPREFIX` 是 `<prefix>/pfx/`，等值比较会失败；且 prefix 依赖调用方上下文。`STEAM_COMPAT_DATA_PATH` 是精确前缀，但它由 UMU 派生、不是启动器控制的信号。
- **只按 `maps` 归属安装目录。** 需要安装目录上下文，且未映射时全瞎；作为后续增强，不作为本片的判据。
- **把标记值写死成常量或按 key 存在匹配整族。** 前者把单游戏假设固化进扫描层；后者已采纳为「存在即归属」，但保留 `query.GameId` 的等值收窄以备用。

## 后果

- Linux 的破坏性闸门（卸载、下载/安装/修复）与会话看护现在能认出：启动器重启后仍在跑的游戏、运行器宿主退出后仍在跑的游戏。
- 依赖「标记经运行器透传」。样本只覆盖 UMU + Proton 单组合；若某运行器清洗未知环境变量，回退到名字家族仍可用（对 `BlueArchive.exe` 这类 ≤15 字符名有效）。
- 仍未覆盖：官方启动器/桌面脚本直接启动、且名字被截断的长名游戏；前缀/`maps` 信号；macOS。
- 守卫：`LinuxProcessScannerTests`（标记优先、家族名回退、无关进程不认领）、`UnixProcessRecordsTests`（标记存在/等值/不符）、`GameRuntimeTests`（启动写入标记）。

## 已知限制

- 标记只对「由本启动器发起」的会话有效。官方启动器或桌面脚本拉起、且 `comm` 被截断到对不上的游戏，Linux 上仍认不出——这与 ADR-032 的放行语义一致（不误报，可能漏拦），由调用方其余检查兜底。
- 标记是普通环境变量，游戏/反作弊理论上可读；它不含机密（只是 gameId），但把它当安全边界是不对的——它只是识别信号。
- 前缀归属（`STEAM_COMPAT_DATA_PATH` 等值 / `WINEPREFIX` 根匹配）尚未接；`maps` 归属已接（见决策 6）。
