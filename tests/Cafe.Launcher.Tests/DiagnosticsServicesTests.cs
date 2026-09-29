using Cafe.Launcher.UI.Services;
using Cafe.Launcher.Core.Services.Diagnostics;
using Cafe.Launcher.Testing;
using Cafe.Launcher.Core.Services;
using Cafe.Launcher.Core.Constants;

namespace Cafe.Launcher.Tests;

public sealed class DiagnosticsServicesTests : IDisposable
{
    private readonly TestDirectory tempDir = TestDirectory.Create();

    [Fact]
    public void LocalDiagnostics_ParameterlessConstructor_UsesTemporaryDirectory()
    {
        var diagnostics = new LocalDiagnostics();

        Assert.StartsWith(
            Path.GetTempPath(),
            diagnostics.LogFilePath,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            LauncherDataRoot.ForCurrentProcess(LauncherProfiles.Cafe.ProductName).Root,
            diagnostics.LogFilePath,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EnrichedTemplate_RendersLogTitleTag()
    {
        using var logger = new UnifiedLogger(tempDir);
        await logger.LogAsync(LogEntrySeverity.Info, "TestTitle", "TestMessage");

        logger.Dispose();
        var text = File.ReadAllText(logger.LogFilePath);
        Assert.Contains("[TestTitle]", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DebugLevel_WrittenWhenMinLevelIsVerbose()
    {
        using var logger = new UnifiedLogger(tempDir);
        logger.SetMinimumLevel(Serilog.Events.LogEventLevel.Verbose);
        await logger.LogAsync(LogEntrySeverity.Debug, "DebugTest");

        logger.Dispose();
        var text = File.ReadAllText(logger.LogFilePath);
        Assert.Contains("[DebugTest]", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LogFileHeaderCodes_EverySeverity_AreRecognisedByTheLogEntryReader()
    {
        // UnifiedLogger 的输出模板（Serilog 的 {Level:u3}）与 LogEntryReader 的头部正则是同一套
        // 三字母代码的两份独立声明，彼此不引用。任一侧改动——换格式、加一级严重度、改拼写——
        // 都会让日志查看器与导出过滤器静默读不到任何条目（无法识别的头行被当成上一条的续行），
        // 而两侧各自的既有用例都仍然通过。这条往返用例是它们之间的唯一定位点。
        using var logger = new UnifiedLogger(tempDir);
        logger.SetMinimumLevel(Serilog.Events.LogEventLevel.Verbose);

        var severities = Enum.GetValues<LogEntrySeverity>();
        foreach (var severity in severities)
        {
            await logger.LogAsync(severity, TitleOf(severity));
        }

        logger.Dispose();
        var records = LogEntryReader.Read(File.ReadAllLines(logger.LogFilePath)).ToList();

        // 每级各一条且顺序一致：少一条即说明该级的代码没被识别（那条头行会被吞进上一条的正文）。
        // 断的是开头而非整串：读取器的 Title 是「头行里级别代码之后的全部内容」，其中还包含
        // 模板里的 {LogTitle} 标签（另一条用例钉住那一段）。
        Assert.Equal(severities.Length, records.Count);
        for (var index = 0; index < severities.Length; index++)
        {
            Assert.StartsWith($"[{TitleOf(severities[index])}]", records[index].Title, StringComparison.Ordinal);
        }

        Assert.All(records, record => Assert.NotNull(record.Timestamp));

        // 代码两两不同，否则「这是哪一级」在查看器里不可分。
        Assert.Equal(
            severities.Length,
            records.Select(record => record.SeverityCode).Distinct(StringComparer.Ordinal).Count());
    }

    private static string TitleOf(LogEntrySeverity severity) => $"Severity-{severity}";

    [Fact]
    public async Task DebugLevel_SuppressedWhenMinLevelIsInfo()
    {
        using var logger = new UnifiedLogger(tempDir);
        logger.SetMinimumLevel(Serilog.Events.LogEventLevel.Information);
        // Seed an Info event so the log file exists even if the Debug event is suppressed.
        await logger.LogAsync(LogEntrySeverity.Info, "SeedEvent");
        await logger.LogAsync(LogEntrySeverity.Debug, "ShouldNotAppear");

        logger.Dispose();
        var text = File.ReadAllText(logger.LogFilePath);
        Assert.Contains("[SeedEvent]", text, StringComparison.Ordinal);
        Assert.DoesNotContain("[ShouldNotAppear]", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FatalLevel_PassesFatalSwitch()
    {
        using var logger = new UnifiedLogger(tempDir);
        logger.SetMinimumLevel(Serilog.Events.LogEventLevel.Fatal);
        await logger.LogAsync(
            LogEntrySeverity.Fatal,
            "FatalTest",
            exception: new InvalidOperationException("boom"));

        logger.Dispose();
        var text = File.ReadAllText(logger.LogFilePath);
        Assert.Contains("[FatalTest]", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LocalDiagnostics_NewFacades_WriteExpectedLevels()
    {
        using var logger = new UnifiedLogger(tempDir);
        logger.SetMinimumLevel(Serilog.Events.LogEventLevel.Verbose);
        var diagnostics = new LocalDiagnostics(logger);

        await diagnostics.DebugAsync("DebugFacade", "debug msg");
        await diagnostics.VerboseAsync("VerboseFacade", "verbose msg");
        await diagnostics.MessageAsync("MessageFacade", "message msg");
        await diagnostics.WarningAsync("WarningFacade", "warning msg");
        await diagnostics.ErrorAsync("ErrorFacade", "error msg", new InvalidOperationException("boom"));
        await diagnostics.FatalAsync("FatalFacade", new InvalidOperationException("fatal"));

        logger.Dispose();
        var lines = File.ReadAllLines(logger.LogFilePath);

        // 断言「标题出现在正确的严重级代码那一行」，而不只是标题出现过：六个门面收敛到同一个
        // 核心之后（B5），映射写错——例如 Error 落到 Info——只会改变级别代码，标题标签照样在，
        // 只看标题的断言对此完全无感。
        foreach (var (code, title) in new[]
                 {
                     ("DBG", "DebugFacade"),
                     ("VRB", "VerboseFacade"),
                     ("INF", "MessageFacade"),
                     ("WRN", "WarningFacade"),
                     ("ERR", "ErrorFacade"),
                     ("FTL", "FatalFacade")
                 })
        {
            var line = Assert.Single(
                lines,
                candidate => candidate.Contains($"[{title}]", StringComparison.Ordinal));
            Assert.Contains($"[{code}]", line, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void RunSession_WhenActionReturns_WritesSessionStartAndEnd()
    {
        using var logger = new UnifiedLogger(tempDir);
        var ran = false;

        Program.RunSession(logger, () => ran = true);

        Assert.True(ran);
        logger.Dispose();
        var text = File.ReadAllText(logger.LogFilePath);
        Assert.Contains("Session started", text, StringComparison.Ordinal);
        Assert.Contains("Session ended", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RunSession_WhenActionThrows_LogsCrashAndRethrows()
    {
        using var logger = new UnifiedLogger(tempDir);

        var exception = Assert.Throws<InvalidOperationException>(
            () => Program.RunSession(logger, () => throw new InvalidOperationException("fatal")));

        Assert.Equal("fatal", exception.Message);
        logger.Dispose();
        var text = File.ReadAllText(logger.LogFilePath);
        Assert.Contains("Session started", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Session ended", text, StringComparison.Ordinal);
        Assert.Contains("[Main]", text, StringComparison.Ordinal);
        Assert.Contains("fatal", text, StringComparison.Ordinal);
    }

    [Fact]
    public void LogSyncSeverityOverload_DoesNotThrow()
    {
        // LogSync uses a Volatile-read static reference. This at minimum verifies the
        // Debug.WriteLine fallback path does not throw. The integrated path (writing
        // through the DI-resolved UnifiedLogger) is exercised by the instance-level facade tests.
        LocalDiagnostics.LogSync(LogEntrySeverity.Debug, "SyncDebug", "sync msg");
        LocalDiagnostics.LogSync("SyncInfo", "info msg");
    }

    /// <summary>
    /// 日志按大小轮转后，Serilog 把后续内容写进 <c>unified_001.log</c> 一类带序号的名字，基名
    /// <c>unified.log</c> 从此不再被创建。谁把 <c>LogFilePath</c> 当成「现在正在写的那个文件」
    /// 读，谁就在 5 MB 之后永久拿到空内容——日志查看器全空、导出抛 <c>FileNotFoundException</c>。
    /// </summary>
    [Fact]
    public void ResolveActiveLogFile_AfterRotation_ReturnsTheRotatedFileInsteadOfTheAbsentStem()
    {
        var stem = Path.Combine(tempDir, "unified.log");
        var rotated = Path.Combine(tempDir, "unified_001.log");
        File.WriteAllText(rotated, "rotated content");

        Assert.False(File.Exists(stem));
        Assert.Equal(rotated, UnifiedLogger.ResolveActiveLogFile(stem));
    }

    [Fact]
    public void ResolveActiveLogFile_WithBothStemAndRotatedFile_PrefersTheStem()
    {
        var stem = Path.Combine(tempDir, "unified.log");
        var rotated = Path.Combine(tempDir, "unified_001.log");
        File.WriteAllText(stem, "current content");
        File.WriteAllText(rotated, "rotated content");
        // 基名最后一次写入早于轮转文件：解析必须按「基名优先」而不是「最新写入」，否则正在写的
        // 那份会被旧的轮转文件盖过去。
        File.SetLastWriteTimeUtc(stem, DateTime.UtcNow.AddHours(-2));

        Assert.Equal(stem, UnifiedLogger.ResolveActiveLogFile(stem));
    }

    [Fact]
    public void ResolveActiveLogFile_WithNoLogFiles_ReturnsNull()
    {
        Assert.Null(UnifiedLogger.ResolveActiveLogFile(Path.Combine(tempDir, "unified.log")));
        Assert.Null(UnifiedLogger.ResolveActiveLogFile(""));
    }

    [Fact]
    public void ExistingLogFiles_IgnoresSiblingsThatAreNotRotationOutput()
    {
        var stem = Path.Combine(tempDir, "unified.log");
        var rotated = Path.Combine(tempDir, "unified_001.log");
        File.WriteAllText(stem, "current");
        File.WriteAllText(rotated, "rotated");
        // 同词干但不是轮转产物：导出面板不能把它们当成日志读进来，也不能因为它们把
        // 「当前日志不存在」的判定搅乱（例如 unified.log.bak 会让 EndsWith 通过）。
        File.WriteAllText(Path.Combine(tempDir, "unified.log.bak"), "backup");
        File.WriteAllText(Path.Combine(tempDir, "unified_notes.log"), "notes");

        var files = UnifiedLogger.ExistingLogFiles(stem);

        Assert.Equal(2, files.Count);
        Assert.Contains(
            files,
            path => string.Equals(Path.GetFileName(path), "unified.log", StringComparison.Ordinal));
        Assert.Contains(
            files,
            path => string.Equals(Path.GetFileName(path), "unified_001.log", StringComparison.Ordinal));
    }

    /// <summary>
    /// 兜底日志器的所有权：Core-only 容器（没有登记 <c>UnifiedLogger</c>）里由门面自建的那一个
    /// 必须由门面负责释放——否则容器释放门面之后，那条 Serilog 异步 sink 永远不会关。注入进来
    /// 的那一个则相反：它的所有者是进程（<c>Program.RunSession</c> 最后显式释放），门面不得替
    /// 它做主。日志是 <c>shared: true</c> 打开的，句柄在释放后依然可读，所以判据用
    /// <c>Dispose</c> 上的测试缝而不是文件锁。
    /// </summary>
    [Fact]
    public void LocalDiagnostics_DisposesOnlyTheLoggerItOwns()
    {
        var ownedDisposed = false;
        var owned = new UnifiedLogger(
            Path.Combine(tempDir, "fallback-owned"),
            buildIdentity: null,
            onDisposed: () => ownedDisposed = true);

        LocalDiagnostics.Owning(owned).Dispose();

        Assert.True(ownedDisposed, "兜底日志器没有随门面一起释放，这条 Serilog 管道会永久泄漏。");

        var injectedDisposed = false;
        using var injected = new UnifiedLogger(
            Path.Combine(tempDir, "fallback-injected"),
            buildIdentity: null,
            onDisposed: () => injectedDisposed = true);

        new LocalDiagnostics(injected).Dispose();

        Assert.False(
            injectedDisposed,
            "门面释放了注入进来的日志器：它的所有者是进程，不是门面。");
    }

    /// <summary>
    /// 「谁可以创建自有日志器的门面」只允许一处：Core-only 容器的兜底分支。任何其它生产调用点
    /// 都可能把一个由别处持有的日志器交给门面去释放。
    /// </summary>
    [Fact]
    public void LocalDiagnostics_OwningFacadeIsCreatedOnlyByTheCoreFallbackBranch()
    {
        var callSites = Directory
            .EnumerateFiles(TestRepository.CorePath, "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(TestRepository.HostPath, "*.cs", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(TestRepository.PresentationPath, "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => File.ReadAllText(path).Contains("LocalDiagnostics.Owning(", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(TestRepository.Root, path))
            .ToArray();

        Assert.Equal(
            [Path.Combine("src", "Cafe.Launcher.Core", "Composition", "LauncherCoreServiceCollectionExtensions.cs")],
            callSites);
    }

    public void Dispose()
    {
        tempDir.Dispose();
    }
}
