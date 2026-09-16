using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.Testing;

namespace Cafe.Launcher.Avalonia.Tests;

/// <summary>
/// <see cref="MainWindowTestContext"/> 的两条契约：日志活到用例结束（消费者在其整个活期内
/// 继续写入），以及两个上下文之间不共享任何持久化状态。
/// </summary>
[Collection(nameof(LocalizationServiceTestIsolation))]
public sealed class MainWindowTestContextTests
{
    static MainWindowTestContextTests()
    {
        TestLocalizationHelper.Initialize();
    }

    /// <summary>
    /// 装配返回之后日志仍可写：设置页与日志查看器持有这两个日志器，先前它们是装配方法的
    /// 局部 <c>using</c>，方法一返回句柄就被关掉，消费者此后的写入静默落进已释放的管道。
    /// </summary>
    [Fact]
    public async Task Create_AfterAssemblyReturns_KeepsItsLoggersWritable()
    {
        using var scope = new ContextScope();
        var context = scope.CreateContext();
        using var viewModel = context.ViewModel;
        Assert.NotEmpty(context.Loggers);

        // 装配方法早已返回（CreateContext 已完成），此刻写下探针。
        foreach (var logger in context.Loggers)
        {
            await logger.LogAsync(LogEntrySeverity.Info, "ContextLifetimeProbe");
        }

        context.Dispose();

        foreach (var logger in context.Loggers)
        {
            Assert.Contains(
                "[ContextLifetimeProbe]",
                File.ReadAllText(logger.LogFilePath),
                StringComparison.Ordinal);
        }
    }

    /// <summary>上下文释放后不再占着日志文件：句柄留着不放才是最容易被忽略的泄漏。</summary>
    [Fact]
    public async Task Dispose_ReleasesTheLogFilesItOwned()
    {
        using var scope = new ContextScope();
        var context = scope.CreateContext();
        var logPaths = context.Loggers.Select(logger => logger.LogFilePath).ToArray();
        foreach (var logger in context.Loggers)
        {
            await logger.LogAsync(LogEntrySeverity.Info, "DisposeProbe");
        }

        context.ViewModel.Dispose();
        context.Dispose();

        foreach (var path in logPaths)
        {
            Assert.True(File.Exists(path));
            // 写句柄没放掉时，Windows 上这一步会失败——这正是被测的那条契约。
            File.Delete(path);
            Assert.False(File.Exists(path));
        }
    }

    /// <summary>
    /// 两个上下文各写各的数据根：一个用例的设置不会出现在另一个用例的目录里。
    /// </summary>
    [Fact]
    public async Task TwoContexts_DoNotSharePersistedState()
    {
        using var firstScope = new ContextScope();
        using var secondScope = new ContextScope();
        using var firstContext = firstScope.CreateContext();
        using var firstViewModel = firstContext.ViewModel;
        using var secondContext = secondScope.CreateContext();
        using var secondViewModel = secondContext.ViewModel;

        await firstViewModel.Settings.SaveSettingsCommand.ExecuteAsync(null);

        Assert.True(File.Exists(firstScope.Directory.DataRoot.SettingsPath));
        Assert.False(File.Exists(secondScope.Directory.DataRoot.SettingsPath));
    }

    /// <summary>
    /// 一个上下文所需的全部外部夹具：目录、直连工厂、图片缓存。它们由用例创建并释放
    /// （上下文只释放自己创建的对象，见 <see cref="MainWindowTestContext"/> 的注释）。
    /// </summary>
    private sealed class ContextScope : IDisposable
    {
        private readonly HttpClientFactory httpClientFactory;
        private readonly ImageCacheService imageCacheService;

        public ContextScope()
        {
            Directory = TestDirectory.Create();
            httpClientFactory = new HttpClientFactory(new ProxySettingsService());
            imageCacheService = new ImageCacheService(
                new StubRemoteHttpTransport(),
                new Crc64Service(),
                Directory.DataRoot);
        }

        public TestDirectory Directory { get; }

        public MainWindowTestContext CreateContext() => MainWindowTestContext.Create(
            Directory,
            httpClientFactory,
            imageCacheService,
            new EmptyCoreService());

        public void Dispose()
        {
            imageCacheService.Dispose();
            httpClientFactory.Dispose();
            Directory.Dispose();
        }
    }

    private sealed class EmptyCoreService : ILauncherCoreService
    {
        public Task<LauncherStatusSnapshot> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new LauncherStatusSnapshot());
    }
}
