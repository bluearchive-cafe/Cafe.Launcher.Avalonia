# 验证入口与下载续传修复设计

## 目标

修复以下两个已经通过仓库代码和实际命令确认的问题：

1. 仓库根目录执行 `dotnet test` 不会运行两个测试项目，但文档将其描述为全量测试命令。
2. `FileDownloadService` 在瞬时传输失败后删除已下载的 `.tmp` 文件，导致同一下载任务无法从已接收字节继续。

## 范围

本次修改包括：

- 新增一个完整验证脚本；
- 修正 `AGENTS.md` 和 `README.md` 中的测试命令；
- 调整单文件下载失败后的临时文件保留规则；
- 增加覆盖传输中断和 Range 续传的自动化测试。

本次不修改 ViewModel 协调结构，不增加界面设置，不改变本地安装状态文档格式。

## 验证入口

新增一个 PowerShell 验证脚本，固定执行：

1. Debug 构建；
2. `Cafe.Launcher.Avalonia.Tests`；
3. `Cafe.Launcher.Avalonia.HeadlessTests`；
4. Release 构建。

脚本设置：

- `DOTNET_CLI_TELEMETRY_OPTOUT=1`
- `AVALONIA_TELEMETRY_OPTOUT=1`

`build.ps1` 继续作为快速 Debug 构建入口。`AGENTS.md` 和 `README.md` 使用两个测试项目的精确路径，并说明完整验证脚本。

## 下载临时文件规则

`FileDownloadService.DownloadAsync()` 按异常语义处理 `.tmp` 文件：

- CRC64 不匹配：删除；
- 无效 `Content-Range`：删除；
- 其他 `InvalidDataException`：删除；
- `HttpRequestException`：保留；
- 传输过程中的 `IOException`：保留；
- `OperationCanceledException`：维持当前行为，保留；
- 其他异常：删除。

保留的文件仍不被直接信任。下一次请求必须携带从现有文件长度开始的 `Range`；服务端返回的 `Content-Range` 必须与该长度及预期总长度完全一致。最终文件仍必须通过 CRC64 校验。

## 测试

先增加一个失败的回归测试：

1. 第一次响应返回部分正文后抛出传输异常；
2. 第二次请求必须携带精确的 Range 起点；
3. 第二次响应返回剩余内容和有效 `Content-Range`；
4. 最终 `.tmp` 内容与预期文件一致；
5. 最终 CRC64 校验通过。

保留现有无效 `Content-Range` 和 CRC64 不匹配测试，确保不可信临时文件仍被删除。

## 完成条件

- 回归测试经历失败到通过；
- 两个测试项目全部通过；
- Debug 和 Release 构建均为 0 警告、0 错误；
- 文档不再将根目录 `dotnet test` 描述为全量测试命令；
- `git diff` 仅包含本设计范围内的文件。
