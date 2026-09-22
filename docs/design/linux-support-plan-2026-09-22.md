# Cafe Launcher Linux 支持推进清单（重排版）

> 状态：**推进中**——P0-C 已落地（`a5d793f`…`7f93234`），P0-B 判据前移稿已完成（接线待 P0-A 样本），P0-A 仍为外部 gate，P1-D 已落地（预检＋启动拒绝/本地化，`4f2dabf`、`e628998`） · 生成时点：**2026-09-22** · 基准提交：**29375e5**
> 来历：基于一次代码与文档评估，对原 Linux 适配方案重排优先级。**本次未修改任何代码，也未做实机验证**——因此本文的每条结论都标注了证据类型（代码事实 / 文档记录 / 待实测）。
> 相关专项：Wayland 激活与桌面通知沿用 [`desktop-notifications-integration-plan.md`](desktop-notifications-integration-plan.md)，本文不重复设计。

---

## 0. 结论摘要

原方案把多项**已完成**的能力列为 P0，却把两项真正决定 Linux 可用性的前提排在后面。重排后：

| 优先级 | 工作 | 性质 | 完成标准 |
| --- | --- | --- | --- |
| **P0-A** | 游戏与反作弊实机验证 | **外部 gate**（非代码交付物） | 至少一个有版本记录、可复现的可玩组合 |
| **P0-B** | Linux 进程识别与操作保护 | 代码，但**先采样后设计** | 外部启动、宿主退出、启动器重启后仍能正确阻止破坏性操作 |
| **P0-C** | 运行器日志与失败诊断 | 纯代码，可立即开工 | 导出包能定位失败发生在哪一层 |
| **P1-D** | 路径、文件系统与运行环境预检 | 代码 | 常见失败在启动前得到可操作提示 |
| **P1-E** | Prefix 元数据、Proton 构建发现 | 代码 | 用户能确认当前组合，切换行为明确 |
| **P1-F** | Wayland 激活及桌面通知 | 沿既有专项文档 | KDE/GNOME 验证通过 |
| **P2** | Arch 包、Flatpak、更多运行器 | 需求驱动 | 有明确用户需求后再启动 |

**关键排序约束**：P0-A 是 P0-B 的前置（其进程树样本来自 P0-A 的实机环境），P0-C 与 P0-A 可并行。把 P0-A 与另两项并列成"可提交的 P0"会让计划一开始就有个无法 assign 的条目。

**当前进度（2026-09-22 更新）**：P0-C 已全部落地——`a5d793f` 诊断导出加入运行器进程快照、`a50f2b1` 运行器 stdout/stderr 有上限捕获、`c1ebdb4` 桌面会话信息、`7f93234` GPU/Vulkan 探测。P0-B 的判据前移稿已完成并附纯函数骨架（见 §3.2），**接线与 ADR 仍等 P0-A 样本**。P0-A 尚未开始（外部 gate）。P1-D 已落地（`4f2dabf` 预检记录、`e628998` 阻断启动＋本地化提示）。

---

## 1. 已确认的基线（代码事实，已逐条核对）

| 方案原假设 | 实际现状 | 证据 |
| --- | --- | --- |
| 需要 Native/UMU/Wine 抽象 | 已有声明式 `GameRunnerDefinition`，规则集中在 `GameRuntime` | `GameRuntime/GameRunnerDefinition.cs` |
| 需重做 UMU 优先 | 注册序已是 Native → UMU → Wine；Linux 经 `IsSupportedPlatform` 短路 Native | `Composition/ServiceConfiguration.cs:141`、`GameRuntime/GameRuntime.cs:191` |
| 需新建环境探测 | 已有运行器定位、`--version` 探测、超时、Broken/Available/NotFound 状态 | `GameRuntime/GameRuntime.cs:186-232`、`RuntimeVersionProbe.cs` |
| Prefix 需规划 | 已按 gameId + runnerId 隔离在数据根之外 | `GameRuntime/GameCompatibilityPaths.cs:37` |
| 需新建诊断 | 已有 `GameRuntimeDiagnosticSnapshot` + 系统信息 + 日志 ZIP | `GameRuntime/GameRuntime.cs:325-338`、`Diagnostics/LogExportService.cs` |
| 需补 Linux CI | 已有提交时单元测试 + 每周 `linux-tests.yml` | `.github/workflows/build.yml:113`、`linux-tests.yml` |
| 需补 Linux 分发 | 已有 `.tar.gz`/AppImage/`.deb`，且发布时对 AppImage 做启动冒烟 | `.github/workflows/release.yml:54,90` |
| Wayland 未设计 | 已有专项集成计划 | `desktop-notifications-integration-plan.md` |

**两项被低估的真实缺口（也已核对）：**

1. **运行器启动不重定向 stdout/stderr**。`BuildStartInfo`（`GameRuntime/GameRuntime.cs:251-295`）只设 `FileName`/`WorkingDirectory`/`ArgumentList`/`Environment`；全仓仅版本探测重定向输出（`RuntimeVersionProbe.cs:36-37`）。UMU 选 Proton 时快照记的是字面量 `"auto"`（`GameRuntime.cs:315-323`），不是实际构建。
2. **Linux 上运行中闸门实际失效**。扫描走 `Process.GetProcesses()` + `ProcessName`（`ProcessService.cs:81`），而 [ADR-032 已知限制](../../docs/design/adr/ADR-032-游戏进程按名字家族识别.md)（第 92 行）明确记录：Unix `comm` 15 字符封顶，`xldr_BlueArchiveOnline_JP_loader_x64` 与 `xldr_BlueArchiveOnline_JP` 截成同一串，等值与 `_` 分界两条规则都不成立；结论是"发布 linux-x64/osx-arm64，Wine/UMU 安装实际上没有这道闸门——它不误报（空表即放行），但也不拦"。`GameProcessTracker` 的句柄兜底（`GameProcessTracker.cs:105-120`）只在同一会话内有效，覆盖不到"运行器退出、游戏仍在跑"和"重启启动器后发现已有游戏"。

---

## 2. P0-A：游戏与反作弊实机验证（外部 gate）

**性质**：项目持有者的实机任务，不是代码交付物。README 已写明 Linux 为实验性、反作弊兼容性未验证（`README.md:42,44`）。`--version` 探测只证明运行器可执行；成功创建进程不证明能登录与持续运行。

**固定一个可复现组合**（UMU + 一个明确版本记录的 Proton），依次验证五步并留证：

1. 首次启动（含 Prefix 初始化）
2. 登录
3. 进入实际游戏
4. 正常退出
5. 再次启动

**失败分支**：若卡在反作弊，则加 Runner 类型、Prefix 元数据、打包格式都不能直接解决——此时应把结论写回 README 与本文，而不是继续扩 Runner 架构。

### 2.1 验证记录模板

```yaml
verified_at: <ISO8601>
verifier: <name>
result: pass | fail | partial
steps:
  first_launch: pass | fail
  login: pass | fail
  in_game: pass | fail
  normal_exit: pass | fail
  relaunch: pass | fail
environment:
  distro: <e.g. Arch Linux, kernel 6.x>
  session: wayland-kde | wayland-gnome | x11
  gpu: <model>
  gpu_driver: <e.g. mesa 24.x / nvidia 55x.xx>
  vulkan: <driver version, e.g. RADV / NVIDIA>
runner:
  umu_version: <umu-run --version>
  proton_build: <actual build dir / release name, NOT "auto">
  wine_version: <wine --version from inside prefix if obtainable>
  dxvk_version: <from runner log>
  game_id: <GAMEID>
anti_cheat:
  present: true | false
  version: <if discoverable from install dir>
  behavior: <e.g. loads and passes / exits / blocks>
client:
  game_version: <from game-launcher-config.json / manifest>
  launcher_version: <BuildInfo.LauncherVersion>
artifacts:
  log_export: <path to exported ZIP>
  notes: <what failed, at which layer>
```

---

## 3. P0-B：Linux 进程识别与操作保护

**为什么优先**：它决定更新、修复、卸载的运行中保护是否成立。Linux 允许删除/替换已打开的文件，误判代价更高。ADR-032 已承认这道闸门在 Linux 上实际不拦。

**硬约束（评估已指出，采纳）**：**不能简单把名字截成 15 字符来匹配**，那会引入误认。设计必须建立在真实进程树样本上。

### 3.1 前置：采集真实 Wine/UMU 进程树（在 P0-A 的环境里执行）

对一次由启动器发起的运行，记录：

```bash
# 1. 运行器家族与树形
ps -e -o pid,ppid,comm,args | rg -i 'umu|wine|proton|pressure|steam|pv-|Xign|BlueArchive|xldr'
pstree -alp $(pgrep -f umu-run | head -1)   # 树形，含 pid

# 2. 逐个候选节点（对上面每个 PID）
for p in <PIDs>; do
  echo "== $p comm=$(cat /proc/$p/comm)"; cat /proc/$p/cmdline | tr '\0' '\n'; readlink /proc/$p/exe
done

# 3. 环境与归属
tr '\0' '\n' < /proc/<pid>/environ | rg 'WINEPREFIX|GAMEID|PROTONPATH|STEAM_COMPAT|WINEDLLOVERRIDES'

# 4. 关注点：游戏 PE 是否作为独立 /proc 节点出现，还是只映射在某个 wine 宿主进程里
cat /proc/<pid>/maps | rg -i 'BlueArchive|xldr|\.exe'
```

**要回答的问题**（决定后续判据）：游戏身份落在哪个字段——`comm`、`cmdline` 还是 `/proc/<pid>/exe`；运行器退出后游戏以什么节点存活；Prefix（`WINEPREFIX`）与进程的关联是否稳定。

### 3.2 设计要点

> 判据设计的「前移稿」（内核 ABI 层与强信号先定稿，弱信号与具体字段序仍待样本）见 [`linux-process-identification-design-2026-09-22.md`](linux-process-identification-design-2026-09-22.md)；本文仍是排序的所有者。

- 判据来源应优先用 `/proc/<pid>/cmdline`（不截断）与 `/proc/<pid>/exe`，而不是仅 `comm`。
- 可用的关联维度：`WINEPREFIX` 环境变量、`cmdline` 中的 Windows 路径、进程树父子关系、会话内已注册的运行器句柄。
- **失败方向必须保持"宁可放行"**：当前空表即放行（不误报），新实现不得引入误报。无法确定时返回"没在跑"，由调用方的其余检查兜底。
- 影响面：`GameProcessNames`、`ProcessService`、`GameProcessTracker`、`RunningGameGate.ResolveKnownProcessNames`（闸门判据的唯一出处，见 [ADR-035](../../docs/design/adr/ADR-035-游戏会话状态可见-启动后看护与状态行.md)）。
- **需要一份新 ADR**，因为它改动 ADR-032 的前提（"判据只有名字、Unix 被截断削弱"），必须把新判据、误报边界与被否决方案记下来。

### 3.3 完成标准

- 外部启动（启动器重启后）能识别到游戏在跑；
- 运行器宿主退出、游戏仍在跑时能识别；
- 更新/修复/卸载在以上两种状态下都拒绝破坏性写入；
- 不误报（无关的长进程名不被认领）有测试覆盖。

---

## 4. P0-C：运行器日志与失败诊断（已落地）

**现状**：启动不重定向运行器输出；`ProtonPath` 记为 `"auto"`。

**纯增量，接现有导出服务，不新建日志根**：

- 有**大小上限**的运行器 stdout/stderr 捕获（避免无界增长），作为可选条目加入 `LogExportService`（它已有 manifest 跳过记录模型）。
- 解析并记录**实际 Proton 构建**（替代/补充 `"auto"`），可行路径：读取 UMU 选定构建目录名，或调整 UMU 调用以显式传入构建。
- 补充系统信息：桌面会话类型（Wayland/X11）、GPU 与 Vulkan 信息（可从 gfxinfo/vulkaninfo 取，注意不在无工具时失败）。
- 首次运行的 Prefix 初始化失败原因（把运行器 stderr 的关键行与失败阶段关联）。

**注意**：捕获需要处理进程生命周期与缓冲（异步读取、退出时 flush），并保留仓库"启动边界返回诊断结果而不抛穿 UI 管线"的约定（`GameRuntime.cs:70-83`）。

**完成标准**：导出包能解释失败发生在哪一层（运行器不存在 / 版本探测失败 / Prefix 初始化失败 / 反作弊拦截 / 游戏崩溃），且新增日志有上限、失败不影响启动。

**落地记录（2026-09-22）**：Wine/UMU 启动重定向 stdout/stderr 到数据根 `runner_output.log`（`RunnerOutputCapture`，256 KB 上限、每次启动覆盖、失败不影响启动，`a50f2b1`）；导出包带上该文件，并新增 `linux-process-snapshot.txt`（`a5d793f`）；`system-info.json` 增 `session`（`c1ebdb4`）与 `graphics`（`vulkaninfo`/`glxinfo`，短超时、缺失即省略，`7f93234`）。实际 Proton 构建可从运行器输出读出，"auto" 仅剩诊断快照里的字面量。

---

## 5. P1

- **P1-D 路径、文件系统与运行环境预检**：检查大小写冲突、读写权限、符号链接能力、Prefix 所在位置。**不要把"大小写敏感"本身判为不兼容**，也不要一刀切禁止 NTFS 上的游戏目录。发行版名称可记录，但决定启动的是运行器、图形能力、目录权限与会话条件。
  - **已落地（2026-09-22）**：`CompatibilityEnvironmentPrecheck`（`4f2dabf`）在选中 Wine/UMU 运行器后、启动前对有效前缀预检——不可创建/写入、文件系统不支持符号链接、挂载点 `noexec` 记为阻断级发现；发行版与大小写敏感性只记录。报告写入数据根 `compatibility_environment.json` 并由诊断导出携带。`e628998` 把阻断级发现接入启动：命中即 `EnvironmentPrecheckFailed` 拒绝启动，`GameLaunchService` 按类别给出可操作本地化文案（前缀不可写 / 不支持符号链接 / noexec）；预检自身出错仍按「无发现」放行，不因诊断失败误拒。
- **P1-E Prefix 元数据与 Proton 构建发现**：记录创建信息、最近运行器版本、最近成功组合（诊断价值）。**先记录，不做自动迁移**；版本变化不等于必须迁移，元数据也不保证 Prefix 可回滚，自定义 Prefix 的所有权要写明。发现 GE-Proton 安装目录可作为设置体验增强。
- **P1-F Wayland 激活与桌面通知**：按 [`desktop-notifications-integration-plan.md`](desktop-notifications-integration-plan.md) 推进，验证 KDE/GNOME，避免重复设计。

---

## 6. P2 与边界

**暂不做**：

- **不新增统一的 `HostPath/WinePath` 双路径模型**：当前启动请求已传宿主路径与独立参数，UMU 官方示例也直接接收 Unix 下的 EXE 路径；只在游戏参数确实要求 Windows 路径时于运行器边界转换，不能假设所有 Prefix 有相同 `Z:` 映射。
- **不增加"裸 Proton → Wine-GE"自动回退链**：Proton 构建选择与启动器协议是两回事；`ProtonPath` 已能表达用户选定的构建。不同组合的兼容性须针对本游戏验证，不能按通用排行榜判断。
- **不马上外置 JSON**：现有声明式定义足以服务三个运行器；Bottles/CrossOver 涉及不同命令与生命周期，不能仅凭新增配置项就声称支持。
- **不做 Prefix 自动迁移**（见 P1-E）。
- 针对特定显卡的固定驱动安装命令与性能比例**不进入项目指导**；DXVK 要求随版本变化，须按选定构建核验。

**保留专用游戏启动器边界**：现阶段投入应落在把已有 UMU/Wine 路径做成可验证、可诊断、不会误操作游戏文件的完整流程；扩展成小型 Lutris 管理器会增加大量维护面，未必提高日服实际可玩性。这也与 `AGENTS.md` 的 feature 隔离、窄抽象一致。

**P2**：Arch 包、Flatpak、更多运行器——有明确用户需求后再启动。

---

## 7. 门禁与记录

- 每次代码落地后跑 `.\verify.ps1`（Debug 构建 + 覆盖率 + Release 构建），XAML/样式改动加 `UiStyleContractTests`，资源改动跑 `.\scripts\Test-LocalizationContract.ps1`。
- P0-B 落地需新 ADR；P0-A 的验证记录模板（§2.1）填毕后，同步更新 `README.md` 的平台支持描述。
- 本文是**规划视图**；候选与裁定的唯一查询入口仍是 [`candidates-ledger-2026-09.md`](candidates-ledger-2026-09.md)。若这些条目要进入裁定流程，需按该表 §5 在所有者文档立案后加行。
