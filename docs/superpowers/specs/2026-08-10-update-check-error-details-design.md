# 更新检查错误详情设计

## 目标

让手动检查启动器更新失败时的 Toast 使用统一错误摘要，显示本地化操作说明、异常类型、消息及内部异常链。

## 方案

`LauncherUpdateCheckResult` 增加可选失败异常或失败说明。`LauncherUpdateService` 在网络请求、响应读取和 JSON/版本解析的异常路径保留捕获的异常；非成功 HTTP 响应保留状态码与原因短语。`SettingsViewModel.CheckForUpdatesAsync` 在失败时通过 `ErrorHandlingService.FormatToastMessage` 显示 `launcherUpdateCheckFailed` 与该失败信息。

无更新和发现更新的既有流程不变。没有异常的协议失败使用说明文字，不伪造异常；完整诊断仍保留在现有日志中。

## 测试

为更新服务结果覆盖异常和 HTTP 失败原因，并为设置页失败 Toast 断言摘要包含操作说明与错误详情。
