# ADR 0004：游戏操作并发与强类型状态

状态：已接受

游戏启动、安装/修复、卸载分别通过 `IGameLaunchWorkflow`、`IGameInstallationWorkflow`、`IGameUninstallWorkflow` 暴露给展示层。`GameOperationKind`、`GameOperationStage`、`GameOperationErrorCode` 和 `GameOperationPanelMode` 是唯一合法状态集合，展示层不得依赖裸字符串。

同一时刻只允许一个安装类操作。暂停通过协作等待实现，停止取消活动令牌；断点状态由 `DownloadCheckpointStore` 原子保存和清理。异步多订阅者必须显式按注册顺序等待。
