---
status: accepted
---

# 使用事务化模块管理本地安装状态

本地安装状态由 `manifest.json` 和 `game-launcher-config.json` 共同组成，两个文档必须始终描述同一次安装结果。项目将以 `LocalInstallationStateStore` 统一管理它们的读取、生成、提交、删除和崩溃恢复，并将路径计算提取到独立的 `GameInstallationPath` 模块。

## 模块职责

`LocalInstallationStateStore` 只管理两个状态文档，不负责：

- 创建游戏安装目录；
- 移动、删除或校验游戏文件；
- 计算游戏文件 CRC64；
- 管理现有 `download_state.json`；
- 解释没有事务记录引用的旧 `.tmp` 文件。

下载流程必须在提交状态前完成游戏文件安装和 CRC64 校验。卸载流程负责删除 manifest 列出的游戏文件，再调用状态删除操作。

`GameInstallationPath` 负责默认安装路径和路径规范化。调用 `LocalInstallationStateStore` 前必须提供已经规范化且存在的游戏安装目录。读取不存在的目录返回 `NotInstalled`，删除不存在的目录直接成功，提交不存在的目录返回 `IoFailure`。

两个模块都不定义 `I...` 接口。当前不存在第二个 adapter，测试使用临时目录验证真实文件系统行为。

## 一致性与恢复

两个状态文档禁止出现新旧版本混合。该要求覆盖可捕获异常、进程崩溃和断电。

每次读取、提交或删除前都自动恢复未完成事务：

- 提交事务优先前滚到新状态；
- 新状态无效且两个备份组成有效状态时，回滚旧状态；
- 删除事务始终优先前滚删除；
- 事务记录损坏、格式版本未知、恢复材料缺失，或新旧状态均无效时，停止自动恢复并返回 `Corrupted`；
- 无法确定动作时不得猜测、删除恢复材料或暴露部分状态。

恢复只校验两个状态文档的严格 JSON 结构和现有 `Vc`，不重新校验所有游戏文件。成功恢复只写入本地 diagnostics；失败时由 UI 显示修复操作。

## 事务材料

事务材料位于：

```text
<游戏安装路径>\.cafe-installation-state\
```

精确文件名为：

```text
transaction.json
transaction.previous.json
manifest.new.json
game-launcher-config.new.json
manifest.backup.json
game-launcher-config.backup.json
```

事务记录使用严格、区分大小写且拒绝未知字段的 JSON，包含格式版本、操作类型、阶段及 SHA-256 校验值。校验覆盖除校验字段外的规范化 JSON 内容，用于检测截断和意外损坏，不作为安全签名。

事务记录通过“写入新文件、同步落盘、原子替换”更新，并保留上一阶段记录。恢复时只接受通过校验且阶段序号最高的记录。

事务阶段固定为：

```text
Preparing
NewStateWritten
BackupWritten
Applying
Applied
Deleting
Deleted
```

提交使用前五个阶段；删除使用 `Preparing`、`BackupWritten`、`Deleting`、`Deleted`。未知阶段返回 `Corrupted`。

完成提交、删除或恢复后立即清理事务材料并删除空的事务目录。清理失败不改变主操作结果，只记录 diagnostics。下次读取遇到已完成事务的残留材料时只执行清理，不重复应用操作。

## 持久化语义

状态写入和事务阶段更新必须同步落盘。正式状态替换使用支持写穿透的 Windows 文件操作。项目已限定为 Windows，因此该约束不引入新的平台限制。

状态文档继续使用现有官方 `Vc` 算法，不修改文档格式。状态文档 JSON 区分大小写，但继续允许未知字段。

首次安装没有旧状态时，事务记录明确标记提交前为 `NotInstalled`，不创建备份。新状态无法恢复时保留全部材料并返回 `Corrupted`。

修复流程允许以新的有效状态替换损坏状态。现有文档先完整备份；提交失败时保留恢复材料，不把旧的损坏内容恢复为可用状态。

## 并发控制

同一规范化游戏安装路径的读取、提交、删除和恢复必须串行执行，不同路径互不阻塞。

进程内使用按路径建立、带引用计数的锁条目；没有持有者和等待者时立即移除条目。`LocalInstallationStateStore` 注册为 singleton，确保所有调用者共享该锁表。

跨进程使用：

```text
<游戏安装路径>\.cafe-installation-state.lock
```

锁文件以排他方式打开，并使用句柄关闭时删除的语义。不得先释放锁再手动删除。获取锁每 100 毫秒重试一次，总超时为 5 秒；等待期间响应 `CancellationToken`。超时返回 `Busy`，取消抛出 `OperationCanceledException`。

读取也使用排他锁，因为读取前可能执行恢复和清理。

## 读取结果

读取不再通过可空字段和自由文本错误让调用者推断状态。结果分类固定为：

- `NotInstalled`：两个状态文档都不存在；
- `Valid`：两个文档完整且全部通过校验；
- `Corrupted`：仅存在一个文档，任一文档 JSON 或 `Vc` 无效，或事务无法安全恢复；
- `IoFailure`：I/O 或权限错误；
- `Busy`：5 秒内未取得排他锁。

`Corrupted` 不暴露任何已解析出的部分安装数据。诊断信息可以记录失败类别，但调用者不得继续使用部分状态。

提交和删除使用同一个结构化结果类型，分类为 `Succeeded`、`Busy`、`IoFailure`、`Corrupted`。取消抛出 `OperationCanceledException`，其他未分类异常继续传播。成功提交返回不可变的有效状态快照，成功删除返回明确的 `NotInstalled` 快照。

成功恢复附带结构化恢复信息，例如前滚提交、回滚提交或前滚删除，但不增加新的安装状态分类。

## 提交数据验证

提交入口接收不可变领域快照，并立即复制受管理文件集合。调用者不得传入序列化 JSON 或预计算 `Vc`。

提交前必须验证：

- 游戏版本、manifest 来源和启动程序名称非空；
- 启动参数可以为空集合；
- 文件路径非空，并通过 `GamePathValidator.GetSafePath()`；
- 文件路径解析后不能等于游戏安装目录；
- 解析后的完整路径按 `StringComparer.OrdinalIgnoreCase` 不得重复；
- 文件大小是非负整数；
- CRC64 是 `ulong` 范围内、不带符号和空白的十进制字符串。

参数无效时抛出参数异常，且不得创建事务材料。

## 启动器运行状态

`LauncherStatusSnapshot` 不再以 `IsInstalled`、`NeedsUpdate` 和 `BelowLowestVersion` 三个布尔值表达运行状态，改为单一分类：

- `NotInstalled`
- `Corrupted`
- `Busy`
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
- `Corrupted`：修复，禁止启动和卸载；
- `BelowLowestVersion`：强制更新；
- `UpdateAvailable`：更新；
- `Ready`：启动或可选修复；
- `Busy`、`IoFailure`、`RemoteUnavailable`：仅重试状态加载。

`RemoteUnavailable` 时禁止离线启动，因为当前启动流程依赖远端最低版本和 manifest 校验。

## diagnostics

diagnostics 只记录游戏安装路径、操作类型、事务阶段、恢复动作、失败类别和异常。不得记录完整状态 JSON、启动参数或受管理文件清单。

## 迁移与验证

一次性完成迁移，不保留旧 interface 的兼容转发：

1. 以 `GameInstallationPath` 替代 `LocalGameStateService` 中的路径职责；
2. 以 `LocalInstallationStateStore` 替代状态读取、写入与删除职责；
3. 下载与卸载不再直接读写两个状态文档；
4. 启动、核心快照和 ViewModel 使用新的运行状态分类；
5. 移除 `IsInstalled`、`NeedsUpdate` 和 `BelowLowestVersion`。

验证必须覆盖：

- 读取、提交、删除与自动恢复；
- 每个持久化步骤后的故障注入；
- 少量子进程强制终止场景；
- 跨进程锁超时与取消；
- 路径规范化及提交数据验证；
- 下载、卸载、启动、核心快照和 ViewModel 行为；
- 全量测试；
- Debug 与 Release 构建均为 0 warnings、0 errors。
