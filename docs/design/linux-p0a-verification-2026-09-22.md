# Cafe Launcher Linux P0-A 实机验证记录（2026-09-22）

> 生成时点：**2026-09-22** · 基准提交：**f4b57fb**（实机运行的 Debug 构建，`Version 1.1.0-beta.10`）
> 证据来源：`~/.local/share/Cafe Launcher/unified.log`、`prefix_metadata.json`、`compatibility_environment.json`，以及验证时点仍在运行的进程 `/proc` 快照。
> 模板见 [`linux-support-plan-2026-09-22.md`](linux-support-plan-2026-09-22.md) §2.2。

```yaml
verified_at: 2026-09-22T23:45:26+08:00
verifier: 项目持有者（本记录由日志与进程快照整理）
result: pass
steps:
  first_launch: pass   # 23:42:46 GameLaunch（含 Prefix 初始化）
  login: pass
  in_game: pass
  normal_exit: pass    # 由 23:44:29 的第二次启动推得（无显式退出日志）
  relaunch: pass       # 23:44:29 GameLaunch
environment:
  distro: Arch Linux
  session: unknown     # 本次未导出 system-info；可用日志导出补
  gpu: unknown
  gpu_driver: unknown
  vulkan: unknown
runner:
  umu_version: 1.4.4
  proton_build: /home/gytxtx/.local/share/Steam/compatibilitytools.d/UMU-Proton-10.0-4
  wine_version: unknown
  dxvk_version: unknown
  game_id: blue-archive-jp
anti_cheat:
  present: true
  version: unknown
  behavior: loads and passes   # XignCode 节点 xxd-0.xem 在跑，游戏进入游戏内
client:
  game_version: 1.72.0
  launcher_version: 1.1.0-beta.10 (f4b57fb, Debug)
artifacts:
  log_export: unified.log / prefix_metadata.json / compatibility_environment.json
  notes: 单组合（UMU + UMU-Proton-10.0-4，游戏在 NTFS3）；Debug 构建
```

## 证据

- **两次启动**：`unified.log` 23:42:46 与 23:44:29 各一条 `[GameLaunch] 游戏进程已启动`，均为 `Runner: umu / RunnerVersion: 1.4.4 / Proton: auto`。
- **前缀元数据**：`prefix_metadata.json` `launchCount: 2`、`createdAt 23:42:46`、`lastLaunchedAt 23:44:29` —— 同一前缀的创建信息被保留、次数累加。
- **环境预检**：`compatibility_environment.json` `distribution: Arch Linux`、`caseSensitive: true`、`findings: []`。
- **反作弊**：`xxd-0.xem`（XignCode）作为独立进程在跑；游戏进入游戏内，未被拦截。
- **P0-B 所有权标记（关键）**：启动器已退出（23:45:26 `Session ended`），游戏仍在跑；对存活进程读 `/proc/<pid>/environ`，`CAFE_LAUNCHER_GAME_ID=blue-archive-jp` 出现在 `umu.exe`、`BlueArchive.exe`、`xxd-0.xem` 上，`PROTONPATH` 指向 `UMU-Proton-10.0-4`。这验证了 ADR-036 的「标记经 UMU/pressure-vessel 透传到整族」与「启动器不在场即可认出」。

## 结论与边界

- 该组合 **可玩**：首启（含 Prefix 初始化）、登录、进入游戏、退出、再次启动全部通过，XignCode 未拦截。
- 结果只覆盖 **单一组合**（Arch + UMU 1.4.4 + UMU-Proton-10.0-4 + NTFS3 游戏目录 + Debug 构建）；换发行版/Proton 构建/GPU 需重新取证。
- `environment.session/gpu` 等字段本次未采集，可用新构建的日志导出（`system-info.json` 已含 `session`/`graphics`/`protonBuilds`/`runningProtonBuild`）补齐。
- 实机运行的是 `f4b57fb`（含所有权标记），不含 `8486b80` 的 `maps` 归属与 `runningProtonBuild` 读取；后者为单元测试覆盖，未实机复验。
