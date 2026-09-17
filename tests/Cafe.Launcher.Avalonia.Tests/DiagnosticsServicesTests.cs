using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.Testing;

namespace Cafe.Launcher.Avalonia.Tests;

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
            LauncherDataRoot.ForCurrentProcess().Root,
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

    public void Dispose()
    {
        tempDir.Dispose();
    }
}
