# Toast 错误详情设计

## 目标

在应用发生可恢复错误时，让 Toast 在现有的操作说明之外，显示异常类型、异常消息及完整内部异常链，帮助用户了解失败原因；完整堆栈和诊断详情继续仅写入日志。

## 方案

错误 Toast 的内容由 `ErrorHandlingService` 统一构造。调用方继续传入本地化的操作说明（通过 `ToastMessage` 或现有的本地化格式化文本），不再在各个 `catch` 块中手动追加异常详情。

统一摘要格式为：

```
操作说明（ExceptionType）：Exception.Message → InnerExceptionType：InnerException.Message
```

异常链从外层到内层依次追加。没有内部异常时只显示外层异常；空白消息不产生空白分隔项。摘要不包括堆栈、源代码位置或完整 `Exception.ToString()` 输出。

## 覆盖范围

所有使用 `HandleErrorAsync` 的可恢复错误路径自动获得统一摘要。当前绕过该服务、直接调用 `ToastService.ShowError` 的错误路径将改为经过同一摘要构造逻辑，确保启动初始化和其他直接 Toast 错误也使用相同格式。

严重错误仍通过现有模态错误对话框处理，不新增 Toast；诊断日志仍保留完整异常对象。

## 测试

为摘要构造逻辑添加单元测试，覆盖：

- 操作说明、外层异常类型和消息的组合；
- 多层 `InnerException` 的顺序与分隔；
- 空白消息的处理；
- `HandleErrorAsync` 展示的 Toast 使用统一摘要。

现有 UI 样式契约不需要变更，因为 Toast 仍通过既有 `Message` 属性渲染。
