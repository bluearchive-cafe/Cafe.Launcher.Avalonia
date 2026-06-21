---
status: accepted
---

# 集中管理本地安装状态

本地安装状态由 `manifest.json` 和 `game-launcher-config.json` 共同组成。项目将以 `LocalInstallationStateStore` 集中管理两个文档的读取、生成、校验和提交，并将路径计算提取到独立的 `GameInstallationPath` 模块。

该方案沿用原项目的 `.tmp` 文件与顺序重命名模型，不承诺两个文档的断电原子提交。启动器通过严格识别损坏状态和基于远端最新 manifest 修复来处理中断结果。

## 模块职责

`LocalInstallationStateStore` 只负责：

- 读取 `manifest.json` 和 `game-launcher-config.json`；
- 校验两个文档的 JSON、必需字段和 `Vc`；
- 根据不可变提交数据生成两个文档；
- 写入并校验两个 `.tmp` 文档；
- 将两个 `.tmp` 文档顺序移动为正式文档；
- 删除两个正式状态文档。

它不负责：

- 创建游戏安装目录；
- 下载、移动、删除或校验游戏文件；
- 计算游戏文件 CRC64；
- 管理 `download_state.json`；
- 删除旧 manifest 指定的废弃游戏文件。

下载流程必须在提交本地安装状态前完成游戏文件安装与 CRC64 校验。卸载流程负责删除 manifest 列出的游戏文件，再调用状态删除操作。

`GameInstallationPath` 负责默认安装路径和路径规范化。读取不存在的目录返回 `NotInstalled`，删除不存在的目录直接成功，提交要求目录已经存在。

两个模块都不定义 `I...` 接口。当前不存在第二个 adapter，测试使用临时目录验证真实文件系统行为。

## 并发

同一规范化游戏安装路径的读取、提交和删除必须在进程内串行执行，不同路径互不阻塞。

`LocalInstallationStateStore` 注册为 singleton，并维护按路径建立、带引用计数的锁条目。没有持有者和等待者时立即移除条目。

不实现跨进程锁。应用已有单实例机制，状态 module 不额外扩大同步范围。

## 读取结果

读取结果分类固定为：

- `NotInstalled`：两个正式状态文档都不存在；
- `Valid`：两个文档都存在、格式有效、`Vc` 有效且版本一致；
- `Corrupted`：仅存在一个文档，任一文档无效，或两个文档的版本不一致；
- `IoFailure`：I/O 或权限错误。

`Corrupted` 不暴露任何已解析出的部分安装数据。调用者不得继续使用单个有效文档或部分 manifest 文件集合。

读取时不解释没有完整提交成功的 `.tmp` 文档。它们属于未完成的下载或提交材料，不能替代正式状态。

## 状态文档校验

反序列化必须区分属性名大小写。未知字段允许存在，但以下字段必须存在、JSON 类型正确且满足值约束。

`manifest.json`：

- `name`、`version`、`basis`、`vc`：非空字符串；
- `files`：非 null 数组；
- 每个文件的 `path`、`size`、`hash`、`vc`：非空字符串；
- `size`：能够按 `NumberStyles.None` 和 `CultureInfo.InvariantCulture` 解析为非负 `long`；
- `hash`：能够按 `NumberStyles.None` 和 `CultureInfo.InvariantCulture` 解析为 `ulong`；
- 每个文件及 manifest 信息的 `Vc` 必须通过 `OfficialHashService` 校验。

`game-launcher-config.json`：

- `tag`、`name`、`version`、`vc`：非空字符串；
- `params`：非 null 字符串数组，数组可以为空；
- `Vc` 必须通过 `OfficialHashService` 校验。

两个文档的 `version` 必须完全相同。缺失字段、显式 `null`、错误 JSON 类型或值约束失败均返回 `Corrupted`。不得依赖 DTO 默认值掩盖缺失字段。

## 提交

提交入口接收不可变数据，并立即复制受管理文件集合。调用者不得传入序列化 JSON 或预计算 `Vc`。

提交前必须验证：

- 游戏版本、manifest 来源和启动程序名称非空；
- 启动参数可以为空集合；
- 文件路径非空，并通过 `GamePathValidator.GetSafePath()`；
- 文件路径解析后不能等于游戏安装目录；
- 解析后的完整路径按 `StringComparer.OrdinalIgnoreCase` 不得重复；
- 文件大小是非负整数；
- CRC64 是 `ulong` 范围内、不带符号和空白的十进制字符串。

参数无效时抛出参数异常，且不得写入状态文件。

提交步骤固定为：

1. 根据提交数据生成两个状态文档和全部 `Vc`；
2. 写入 `manifest.json.tmp`；
3. 写入 `game-launcher-config.json.tmp`；
4. 重新读取两个 `.tmp` 文档，按正式文档规则完整校验；
5. 使用 `File.Move(..., overwrite: true)` 将 `manifest.json.tmp` 移动为 `manifest.json`；
6. 使用 `File.Move(..., overwrite: true)` 将 `game-launcher-config.json.tmp` 移动为 `game-launcher-config.json`；
7. 重新读取正式状态并返回结果。

提交成功返回不可变的 `Valid` 状态快照。I/O 或权限错误返回结构化失败，由调用 module 转换为用户消息。

该提交步骤不保证两个正式文档在进程崩溃或断电时原子更新。若中断导致文档缺失、无效或版本不一致，下次读取返回 `Corrupted`。

## 删除

删除具备幂等性：

- 游戏安装目录不存在时直接成功；
- 两个状态文档都不存在时直接成功；
- 仅存在一个状态文档时仍删除现有文档；
- 删除完成后的结果为 `NotInstalled`。

状态 module 只删除两个状态文档，不删除游戏文件或目录。

## 损坏状态修复

`Corrupted` 状态禁止启动和卸载，只允许修复。

修复不读取或信任现有 manifest 中的文件集合，必须：

1. 取得远端游戏配置；
2. 取得该配置指向的最新远端 manifest；
3. 以远端 manifest 作为完整且唯一的受管理文件集合；
4. 对远端 manifest 中的每个文件执行本地存在性和 CRC64 校验；
5. 下载缺失或 CRC64 不匹配的文件；
6. 不删除远端 manifest 未列出的本地文件，因为没有可信旧 manifest 可以证明这些文件受启动器管理；
7. 所有远端文件验证通过后，提交一组新的有效本地安装状态。

远端游戏配置或最新 manifest 无法取得时，不修改正式状态，运行状态保持 `Corrupted`，UI 提供重试。

## 启动器运行状态

`LauncherStatusSnapshot` 不再以 `IsInstalled`、`NeedsUpdate` 和 `BelowLowestVersion` 三个布尔值表达运行状态，改为单一分类：

- `NotInstalled`
- `Corrupted`
- `IoFailure`
- `RemoteUnavailable`
- `BelowLowestVersion`
- `UpdateAvailable`
- `Ready`

状态计算优先级为：

1. 本地安装状态不是 `Valid` 时直接映射本地分类；
2. 本地有效但远端游戏配置不可用时为 `RemoteUnavailable`；
3. 本地版本低于最低版本时为 `BelowLowestVersion`；
4. 本地版本低于最新版本时为 `UpdateAvailable`；
5. 其余情况为 `Ready`。

允许的主要操作为：

- `NotInstalled`：安装；
- `Corrupted`：修复；
- `IoFailure`：重试状态加载；
- `RemoteUnavailable`：重试远端状态加载；
- `BelowLowestVersion`：强制更新；
- `UpdateAvailable`：更新；
- `Ready`：启动或可选修复。

`RemoteUnavailable` 时禁止离线启动，因为当前启动流程依赖远端最低版本和 manifest 校验。

## 远端失败隔离

`LauncherCoreService.LoadAsync()` 必须独立收集本地安装状态与各远端读取结果：

- `OperationCanceledException` 在取消令牌已取消时继续传播；
- 游戏配置请求失败时保留已读取的本地状态；本地状态为 `Valid` 时映射为 `RemoteUnavailable`；
- base config、CDN config、operations、social media 和 installation config 的失败分别保留为空值及对应 diagnostics；
- 只有游戏配置参与 `BelowLowestVersion`、`UpdateAvailable` 和 `Ready` 的计算；
- `LoadAsync()` 返回包含本地状态和各远端结果的快照。

## diagnostics

diagnostics 只记录游戏安装路径、操作类型、失败类别和异常。不得记录完整状态 JSON、启动参数或受管理文件清单。

## 与原项目的关系

保留原项目的核心提交模型：

- 游戏文件和两个状态文档先写入 `.tmp`；
- 文件验证成功后顺序重命名；
- 修复以远端最新 manifest 为依据；
- 卸载根据本地 manifest 删除文件，再删除两个状态文档。

当前项目继续保留已有增强：

- `GamePathValidator` 路径安全；
- 下载和安装阶段的 CRC64 校验；
- `download_state.json` 重启恢复；
- 异步暂停；
- 磁盘空间预检；
- 卸载系统目录保护；
- 本地 diagnostics。

不引入事务日志、状态机、备份、Win32 文件 API、跨进程锁或断电前滚/回滚。

## 实施与验证

按以下 Conventional Commits 顺序实施：

1. `refactor(path): 提取游戏安装路径模块`
2. `refactor(state): 集中本地安装状态读写`
3. `refactor(state): 接入统一运行状态`
4. `test(state): 覆盖损坏识别与修复`

验证必须覆盖：

- 路径规范化；
- `NotInstalled`、`Valid`、`Corrupted` 和 `IoFailure`；
- 单文档缺失、JSON 无效、`Vc` 无效和版本不一致；
- `.tmp` 文档校验失败时不替换正式状态；
- 两次移动之间模拟中断后读取为 `Corrupted`；
- 修复完全依据远端最新 manifest；
- 删除幂等性；
- 下载、卸载、启动、核心快照和 ViewModel 行为；
- 全量测试；
- Debug 与 Release 构建均为 0 warnings、0 errors。
