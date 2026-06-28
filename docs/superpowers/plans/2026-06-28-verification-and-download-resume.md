# Verification and Download Resume Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让本地完整验证实际运行两个测试项目，并让单文件下载在瞬时传输中断后从已接收字节继续。

**Architecture:** 保留 `build.ps1` 的快速 Debug 构建职责，新增 `verify.ps1` 作为完整验证入口。`FileDownloadService` 继续负责单文件 Range 与 CRC64 规则，只把异常清理策略收窄为“不可信数据删除、可验证的传输中断数据保留”。

**Tech Stack:** PowerShell、.NET 10、xUnit 2.9.3、Avalonia Headless XUnit 3.2.2、`HttpMessageHandler` 手动测试实现。

---

## 文件结构

- Create: `verify.ps1` — 本地与 CI 可复用的完整验证入口。
- Modify: `AGENTS.md` — 使用精确测试项目路径记录命令。
- Modify: `README.md` — 使用精确测试项目路径记录命令和完整验证入口。
- Modify: `tests/Cafe.Launcher.Avalonia.Tests/GameDownloadServiceTests.cs` — 增加传输中断后 Range 续传回归测试及测试 HTTP 内容。
- Modify: `Services/FileDownloadService.cs` — 按异常语义决定是否清理 `.tmp`。

### Task 1: 传输中断续传回归测试

**Files:**
- Modify: `tests/Cafe.Launcher.Avalonia.Tests/GameDownloadServiceTests.cs`
- Test: `tests/Cafe.Launcher.Avalonia.Tests/GameDownloadServiceTests.cs`

- [ ] **Step 1: 写入失败的回归测试**

在现有 Range 测试旁增加：

```csharp
[Fact]
public async Task DownloadFileAsync_WhenTransferFails_ResumesFromWrittenBytes()
{
    var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempDir);
    try
    {
        var targetPath = Path.Combine(tempDir, "file.bin.tmp");
        var expectedBytes = Encoding.UTF8.GetBytes("complete-content");
        var hashPath = Path.Combine(tempDir, "hash-source.bin");
        await File.WriteAllBytesAsync(hashPath, expectedBytes);
        var expectedHash = await new Crc64Service().ComputeFileAsync(hashPath);
        var handler = new InterruptedTransferHandler(expectedBytes, 4);
        using var client = new HttpClient(handler);
        var downloader = new FileDownloadService(
            new Crc64Service(),
            new LocalDiagnostics(),
            RemoteHttpUrlValidator.CreateForTesting());

        await downloader.DownloadAsync(
            targetPath,
            new CdnConfigResponse
            {
                PrimaryCdn = "https://primary.example.invalid",
                BackUpCdn = "https://backup.example.invalid"
            },
            "source",
            expectedBytes.Length,
            expectedHash,
            "file.bin",
            client,
            () => Task.CompletedTask,
            (_, _) => Task.CompletedTask,
            false,
            CancellationToken.None);

        Assert.Equal(2, handler.RequestCount);
        Assert.Equal(4, handler.SecondRequestRangeStart);
        Assert.Equal(expectedBytes, await File.ReadAllBytesAsync(targetPath));
    }
    finally
    {
        Directory.Delete(tempDir, recursive: true);
    }
}
```

增加测试处理器和第一次响应使用的内容实现：

```csharp
private sealed class InterruptedTransferHandler(byte[] content, int interruptionOffset)
    : HttpMessageHandler
{
    public int RequestCount { get; private set; }
    public long? SecondRequestRangeStart { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        RequestCount++;
        if (RequestCount == 1)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new InterruptedReadStream(content[..interruptionOffset]))
            });
        }

        SecondRequestRangeStart = request.Headers.Range?.Ranges.Single().From;
        var remaining = new ByteArrayContent(content[interruptionOffset..]);
        remaining.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(
            interruptionOffset,
            content.Length - 1,
            content.Length);
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.PartialContent)
        {
            Content = remaining
        });
    }
}

private sealed class InterruptedReadStream(byte[] bytes) : MemoryStream(bytes)
{
    private bool returnedData;

    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        if (returnedData)
        {
            throw new IOException("Simulated transfer interruption.");
        }

        returnedData = true;
        return base.ReadAsync(buffer, cancellationToken);
    }
}
```

- [ ] **Step 2: 运行测试并确认按预期失败**

Run:

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --no-restore --filter "FullyQualifiedName~DownloadFileAsync_WhenTransferFails_ResumesFromWrittenBytes"
```

Expected: FAIL；第二次请求没有从偏移 `4` 续传，原因是当前 catch 删除了 `targetTempPath`。

- [ ] **Step 3: 提交测试**

```powershell
git add -- tests/Cafe.Launcher.Avalonia.Tests/GameDownloadServiceTests.cs
git commit -m "test(download): 覆盖传输中断后的 Range 续传"
```

### Task 2: 按异常语义保留临时文件

**Files:**
- Modify: `Services/FileDownloadService.cs:192`
- Test: `tests/Cafe.Launcher.Avalonia.Tests/GameDownloadServiceTests.cs`

- [ ] **Step 1: 写入最小实现**

把通用 catch 的无条件删除改为：

```csharp
catch (Exception ex) when (ex is not OperationCanceledException)
{
    if (ex is not HttpRequestException and not IOException)
    {
        try { File.Delete(targetTempPath); } catch { /* best-effort */ }
    }

    lastError = ex;
    if (retryIndex >= RetryDomainOrder.Length - 1) throw;
}
```

CRC64 不匹配路径在进入 catch 前已经删除文件。`InvalidDataException` 继承自 `IOException`，因此必须在该 catch 前增加专用清理：

```csharp
catch (InvalidDataException ex)
{
    try { File.Delete(targetTempPath); } catch { /* best-effort */ }
    lastError = ex;
    if (retryIndex >= RetryDomainOrder.Length - 1) throw;
}
```

- [ ] **Step 2: 运行新回归测试并确认通过**

Run:

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --no-restore --filter "FullyQualifiedName~DownloadFileAsync_WhenTransferFails_ResumesFromWrittenBytes"
```

Expected: PASS，1 个测试通过。

- [ ] **Step 3: 运行单文件下载相关测试**

Run:

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --no-restore --filter "FullyQualifiedName~DownloadFileAsync"
```

Expected: 全部通过；CRC64 不匹配和无效 `Content-Range` 测试继续验证删除行为。

- [ ] **Step 4: 提交实现**

```powershell
git add -- Services/FileDownloadService.cs
git commit -m "fix(download): 保留传输中断后的续传文件"
```

### Task 3: 统一完整验证入口

**Files:**
- Create: `verify.ps1`
- Modify: `AGENTS.md`
- Modify: `README.md`

- [ ] **Step 1: 新增完整验证脚本**

创建 `verify.ps1`：

```powershell
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:AVALONIA_TELEMETRY_OPTOUT = '1'

dotnet build .\Cafe.Launcher.Avalonia.csproj -c Debug --no-restore
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj -c Debug --no-restore
dotnet test .\tests\Cafe.Launcher.Avalonia.HeadlessTests\Cafe.Launcher.Avalonia.HeadlessTests.csproj -c Debug --no-restore
dotnet build .\Cafe.Launcher.Avalonia.csproj -c Release --no-restore
```

- [ ] **Step 2: 修正文档测试命令**

在 `AGENTS.md` 和 `README.md` 中使用：

```powershell
.\verify.ps1
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj
dotnet test .\tests\Cafe.Launcher.Avalonia.HeadlessTests\Cafe.Launcher.Avalonia.HeadlessTests.csproj
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~VersionComparerTests"
```

不得继续将仓库根目录的 `dotnet test` 描述为全量测试。

- [ ] **Step 3: 运行文档契约检查**

Run:

```powershell
rg -n "^dotnet test\\s+#|dotnet test --filter" AGENTS.md README.md
```

Expected: 无匹配。

- [ ] **Step 4: 提交验证入口和文档**

```powershell
git add -- verify.ps1 AGENTS.md README.md
git commit -m "fix(build): 统一完整验证入口"
```

### Task 4: 完整验证

**Files:**
- Verify: `verify.ps1`
- Verify: all modified files

- [ ] **Step 1: 执行完整验证**

Run:

```powershell
.\verify.ps1
```

Expected:

- Debug 构建 0 警告、0 错误；
- 逻辑测试 0 失败；
- 无头界面测试 0 失败；
- Release 构建 0 警告、0 错误。

- [ ] **Step 2: 检查差异和工作区**

Run:

```powershell
git diff --check
git status --short
git log -4 --oneline
```

Expected: `git diff --check` 无输出；工作区无未提交文件；最近提交仅包含设计、测试、下载修复、验证入口。
