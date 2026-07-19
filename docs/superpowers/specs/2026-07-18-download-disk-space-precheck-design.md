# 下载前磁盘空间预检查设计

## 背景

启动器当前同时存在两种磁盘容量口径：安装面板使用远端配置的 `decompression_size` 展示完整游戏解压容量，`GameDownloadService` 则在生成清单差异后仅汇总待下载文件的 `SizeBytes`。全新安装时，这会出现界面判断空间不足，但下载入口仍因压缩下载量较小而放行的矛盾。

实际复现场景为：完整解压容量 18.5GB，可用空间 7.15GB，清单待下载量 1.09GB。安装面板显示还差 11.35GB，但现有下载入口只检查 1.09GB，因此进入下载流程。

## 目标

- 全新安装在空间已知不足时提前禁用安装按钮。
- 即使绕过界面，下载服务也必须在创建下载请求前重新检查空间。
- 更新和修复按实际待下载临时文件量判断，不能因完整游戏容量较大而被错误阻止。
- 界面展示、按钮状态和服务校验复用同一容量规则。
- 保留下载期间 Windows 磁盘已满错误的运行时兜底。

## 非目标

- 不新增可配置的磁盘安全余量。
- 不预测下载期间其他程序可能占用的空间。
- 不改变远端 `decompression_size`、清单 `size` 或本地清单的协议语义。
- 不改变下载、暂停、重试、校验和安装文件的生命周期。
- 不重构与磁盘空间无关的设置或操作面板。

## 统一容量策略

磁盘空间判断由 `DiskSpaceService` 统一提供，并返回结构化结果：

- `RequiredBytes`：本次操作要求的可用字节数；
- `AvailableBytes`：目标磁盘当前可用字节数，查询失败时为空；
- `IsAvailableKnown`：可用空间是否成功读取；
- `HasEnoughSpace`：仅在可用空间已知且不少于所需空间时为真。

所需容量按操作类型计算：

- 全新安装：`max(完整解压容量, 待下载文件总量)`；
- 更新：待下载临时文件总量；
- 修复：待下载临时文件总量。

完整解压容量缺失或无法解析时，全新安装回退到待下载文件总量。边界采用大于等于：可用空间等于所需空间时允许继续。

更新和修复下载的 `.tmp` 文件会与旧文件同时存在，校验通过后才替换目标文件。因此待下载文件总量是这两类操作需要的额外峰值空间，不能使用完整游戏容量，也不能提前减去稍后才删除的旧文件。

## 界面提前禁用

`ShellViewModel` 应在应用 `LauncherStatusSnapshot` 时计算全新安装的磁盘空间结果。只有同时满足以下条件时才提前阻止安装命令：

1. `RuntimeState` 为 `NotInstalled`；
2. 完整解压容量可解析；
3. 可用空间已知；
4. 可用空间小于完整解压容量。

更新、修复、远端不可用和 I/O 失败等状态不使用完整容量禁用操作。它们继续沿用既有操作语义，并在生成精确下载计划后由服务检查。

`GameOperationsViewModel` 将全新安装空间结果纳入 `InstallOrUpdateCommand` 的可执行条件。安装路径、远端配置或运行时状态刷新后必须重新通知命令状态。现有 `Shell.IsBusy` 防重复操作语义保持不变。

安装按钮在空间不足时使用现有禁用视觉，不新增样式。磁盘空间行继续显示现有的“所需 / 可用 / 还差多少”文本。按钮的 `ToolTip` 和 `AutomationProperties.HelpText` 在空间不足时复用 `diskSpaceInsufficientDetail`，提供明确原因和辅助功能反馈。

当界面无法读取容量或空间时，不提前禁用按钮，允许服务入口重新读取，避免一次瞬时失败永久锁死安装操作。

## 下载入口再次校验

`GameDownloadService` 在清单差异生成后、调用任何文件下载服务前重新查询磁盘空间。该检查不能复用界面快照中的可用容量。既有下载检查点可以在此之前保存，但检查失败时必须清除。

服务使用统一策略计算所需容量：

- `NotInstalled` 使用 `max(parsed decompression_size, NeedDownload.Sum(SizeBytes))`；
- 其他安装操作使用 `NeedDownload.Sum(SizeBytes)`。

检查结果通过现有 `GameOperationStage.DiskCheck` 报告给界面，`RequiredDiskBytes` 必须是统一策略得到的最终值。全新安装的复现场景因此显示 18.5GB，而不是 1.09GB。

如果可用空间已知但不足，服务必须：

1. 不调用 `IFileDownloadService`；
2. 清除下载检查点；
3. 返回 `GameOperationErrorCode.InsufficientDiskSpace`；
4. 使用 `diskSpaceInsufficientDetail` 返回所需和可用容量；
5. 记录路径、所需容量和可用容量。

如果服务入口仍无法读取可用空间，维持当前安全失败行为：不开始下载，错误中的可用容量显示为 `--`。界面查询失败时允许点击、服务连续查询失败时拒绝操作，是有意的两阶段降级。

## 运行时兜底

预检查不能消除检查后磁盘空间被其他进程占用的竞争条件。文件写入阶段现有的 Windows `ERROR_DISK_FULL` / `IOException` 处理继续保留，并映射为 `InsufficientDiskSpace`。

运行时磁盘已满时同样清除下载检查点，但不删除已安装游戏文件。本设计不改变临时文件清理和恢复行为。

## 测试策略

实施遵循垂直切片 TDD，一次完成一个红绿循环。

### 容量策略

- 全新安装取完整解压容量与待下载量的较大值。
- 更新和修复只取待下载临时文件总量。
- 无效或缺失的完整解压容量回退到待下载量。
- 可用空间等于所需空间时允许继续。
- 可用空间未知时返回结构化未知结果，不伪造为零。

### 用户场景回归

构造以下场景：

- `RuntimeState = NotInstalled`；
- 完整解压容量为 18.5GB；
- 清单待下载量为 1.09GB；
- 可用空间为 7.15GB。

断言：

- 安装命令不可执行；
- 直接调用 `GameDownloadService` 返回 `InsufficientDiskSpace`；
- `DiskCheck.RequiredDiskBytes` 和错误消息使用 18.5GB；
- 文件下载服务调用次数为零；
- 下载检查点被清除。

增加对照场景：相同容量条件下，增量更新只需要 1.09GB 时允许进入下载。

### 状态与 UI 契约

- 全新安装空间不足时安装按钮禁用。
- 更新和修复不被完整游戏容量错误禁用。
- 更换路径或刷新快照后重新计算命令状态。
- XAML 结构测试确认按钮状态来自操作命令和统一磁盘策略，不使用硬编码容量判断。
- 现有四语种磁盘空间文案键保持完整且格式占位符一致。

## 验证

实施完成后依次运行：

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~DiskSpace|FullyQualifiedName~GameDownload|FullyQualifiedName~GameOperations"
.\dev.ps1 ui
.\scripts\Test-LocalizationContract.ps1
.\verify.ps1
```

## 验收标准

- 18.5GB 完整安装容量、7.15GB 可用空间、1.09GB 下载清单的全新安装不会启动下载。
- 上述场景的安装按钮在空闲状态下仍保持禁用，并能向用户说明原因。
- 更新或修复只要能够容纳本次待下载临时文件，就不会被完整游戏容量误拦截。
- 服务入口每次操作重新读取可用空间，并在下载调用前完成最终判断。
- 界面和服务报告相同的所需容量。
- 运行时磁盘已满仍映射为 `InsufficientDiskSpace`。
- 现有 UI、本地化、单元测试、Headless UI 测试、覆盖率和 Release 构建门禁通过。
