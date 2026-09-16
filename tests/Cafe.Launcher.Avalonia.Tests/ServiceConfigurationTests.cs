using System.Net;
using Cafe.Launcher.Avalonia.Features.Shell;
using Cafe.Launcher.Avalonia.Features.GameOperations;
using Cafe.Launcher.Avalonia.Features.Settings;
using Cafe.Launcher.Avalonia.Composition;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.Testing;
using Cafe.Launcher.Avalonia.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class ServiceConfigurationTests : IDisposable
{
    private readonly TestDirectory tempDir = TestDirectory.Create();

    [Fact]
    public async Task MainWindowViewModel_BackgroundUpdateUsesExplicitSettings()
    {
        var services = CreateServices();
        await using var provider = services.BuildServiceProvider();
        using var viewModel = provider.GetRequiredService<MainWindowViewModel>();
        var previewSettings = viewModel.Settings.Editor.GetSnapshot();
        previewSettings.BackgroundSource = BackgroundSources.Bundled;
        previewSettings.BackgroundFit = BackgroundFits.Fill;

        await viewModel.Background.UpdateBackgroundImageAsync(
            previewSettings,
            null,
            CancellationToken.None);

        Assert.Equal(global::Avalonia.Media.Stretch.Fill, viewModel.Background.BackgroundStretch);
    }

    [Fact]
    public async Task MainWindowViewModel_RequestRepairOpensItsRepairDialog()
    {
        var services = CreateServices();
        await using var provider = services.BuildServiceProvider();
        using var viewModel = provider.GetRequiredService<MainWindowViewModel>();
        viewModel.Operations.ApplySnapshot(new LauncherStatusSnapshot
        {
            RuntimeState = LauncherRuntimeState.Ready
        });

        await viewModel.Operations.RequestRepairCommand.ExecuteAsync(null);

        Assert.True(viewModel.Dialogs.RepairConfirm.IsVisible);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.Dialogs.RepairConfirm.Message));
    }

    [Fact]
    public async Task MainWindowViewModel_ConfirmRepairStartsRepairOperation()
    {
        var services = CreateServices();
        await using var provider = services.BuildServiceProvider();
        using var viewModel = provider.GetRequiredService<MainWindowViewModel>();
        viewModel.Operations.ApplySnapshot(new LauncherStatusSnapshot
        {
            Settings = new LauncherSettings
            {
                GamePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "YostarGames", "BlueArchive_JP")
            },
            RuntimeState = LauncherRuntimeState.Ready,
            Remote = new LauncherRemoteState
            {
                GameConfig = new GameConfigResponse()
            }
        });
        viewModel.Shell.IsBusy = false;
        viewModel.Dialogs.RepairConfirm.Show("repair");

        Assert.NotNull(viewModel.ModalHost.Top);
        Assert.Equal(ModalKind.RepairConfirmation, viewModel.ModalHost.Top!.Kind);
        Assert.True(viewModel.ModalHost.HasEntries);

        await viewModel.Dialogs.RepairConfirm.ConfirmCommand.ExecuteAsync(null);

        Assert.False(viewModel.Dialogs.RepairConfirm.IsVisible);
        Assert.Null(viewModel.ModalHost.Top);
    }

    [Fact]
    public async Task MainWindowViewModel_UsesSharedSingleWindowStateViewModels()
    {
        var services = CreateServices();
        await using var provider = services.BuildServiceProvider();
        using var viewModel = provider.GetRequiredService<MainWindowViewModel>();

        Assert.Same(provider.GetRequiredService<ShellViewModel>(), viewModel.Shell);
        Assert.Same(provider.GetRequiredService<RemoteContentViewModel>(), viewModel.RemoteContent);
        Assert.Same(provider.GetRequiredService<GameOperationsViewModel>(), viewModel.Operations);
    }

    [Fact]
    public void AddLauncherServices_RegistersShellLifecycleThroughOneDisposableServiceDescriptor()
    {
        var services = CreateServices();

        var runtimeDescriptor = Assert.Single(
            services,
            descriptor => descriptor.ServiceType == typeof(IShellRuntime));

        Assert.Equal(typeof(ShellLifecycle), runtimeDescriptor.ImplementationType);
        Assert.DoesNotContain(
            services,
            descriptor => descriptor.ServiceType == typeof(ShellLifecycle));
    }

    [Fact]
    public void AddLauncherServices_RegistersFilePickerThroughSharedConcreteAndInterfaceInstance()
    {
        var services = CreateServices();
        using var provider = services.BuildServiceProvider();

        var concreteService = provider.GetRequiredService<WindowFilePickerService>();
        var interfaceService = provider.GetRequiredService<IFilePickerService>();

        Assert.Same(concreteService, interfaceService);
    }

    [Fact]
    public async Task MainWindowViewModel_Dispose_LeavesContainerOwnedViewModelsForProvider()
    {
        var services = CreateServices();
        await using var provider = services.BuildServiceProvider();
        var viewModel = provider.GetRequiredService<MainWindowViewModel>();
        var notificationCount = 0;
        viewModel.Settings.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(SettingsViewModel.IsSettingsDirty))
            {
                notificationCount++;
            }
        };

        viewModel.Dispose();
        viewModel.Settings.Editor.Current.Language = LauncherLanguages.Japanese;

        Assert.Equal(1, notificationCount);
    }

    /// <summary>
    /// 状态加载不再配置任何东西：HTTP/2 偏好由容器上的一次闭包按租约读取已保存快照
    /// （见 ADR-028）。这条用例守的是「保存设置之后新建的租约立刻跟随」这条链——把组合根的
    /// 闭包写死为 true，或退回按类型注册（容器对未注册的可选参数会回退到声明默认值）
    /// 即变红。放在本文件：它断言的是组合根接线，不是某个服务的职责。
    /// </summary>
    [Fact]
    public async Task SavedHttp2Setting_IsReadByLeasesCreatedAfterwards_WithoutAnyPush()
    {
        await using var provider = CreateServices().BuildServiceProvider();
        var editor = provider.GetRequiredService<ISettingsEditor>();
        using var factory = provider.GetRequiredService<HttpClientFactory>();

        foreach (var (enabled, expected) in new[]
                 {
                     (false, HttpVersion.Version11),
                     (true, HttpVersion.Version20)
                 })
        {
            editor.ApplySnapshot(new LauncherSettings { EnableHttp2 = enabled });

            using var lease = await factory.CreateLeaseAsync(ProxyModes.Direct);

            Assert.Equal(expected, lease.Client.DefaultRequestVersion);
            Assert.Equal(HttpVersionPolicy.RequestVersionOrLower, lease.Client.DefaultVersionPolicy);
        }
    }

    /// <summary>
    /// 显式数据根必须贯穿整张对象图：登记项、闭包捕获的构造参数（日志、崩溃快照）都要落在
    /// 指定目录。只替换 DI 登记而漏掉闭包里的那个根，正是「测试写进真实用户数据目录」的成因。
    /// </summary>
    [Fact]
    public async Task AddLauncherServices_WhenDataRootIsExplicit_RoutesPersistentStateIntoIt()
    {
        var dataRoot = TestDataRoot.ForDirectory(tempDir.Sub(Guid.NewGuid().ToString("N")));
        var services = new ServiceCollection();
        services.AddLauncherServices(launcherDataRoot: dataRoot);
        await using var provider = services.BuildServiceProvider();

        Assert.Same(dataRoot, provider.GetRequiredService<LauncherDataRoot>());
        Assert.Equal(
            dataRoot.SettingsPath,
            provider.GetRequiredService<LauncherSettingsService>().SettingsPath);
        Assert.Equal(
            dataRoot.CrashReportsDirectory,
            provider.GetRequiredService<CrashReportStore>().PrimaryDirectory);
        Assert.StartsWith(
            dataRoot.Root,
            provider.GetRequiredService<UnifiedLogger>().LogFilePath,
            StringComparison.OrdinalIgnoreCase);
        // 图片缓存目录不对外暴露，改看行为：构造即创建的就是注入根下的那一个。
        provider.GetRequiredService<ImageCacheService>();
        Assert.True(Directory.Exists(dataRoot.ImageCacheDirectory));

        await provider.GetRequiredService<DownloadCheckpointStore>().SaveAsync(new DownloadTaskState
        {
            Version = "1.0.0",
            Basis = "manifest.json",
            GamePath = dataRoot.Root,
            StartedAt = "2026-09-16T00:00:00.0000000+00:00"
        });
        await provider.GetRequiredService<NoticeStateService>().SaveShownNoticeAsync("notice-hash");

        Assert.True(File.Exists(dataRoot.DownloadStatePath));
        Assert.True(File.Exists(dataRoot.NoticeStatePath));
    }

    /// <summary>
    /// 缺省仍按进程解析：生产路径的行为不因新增的可选参数而改变。
    /// </summary>
    [Fact]
    public void AddLauncherServices_WithoutAnExplicitRoot_ResolvesTheProcessRoot()
    {
        var services = new ServiceCollection();
        services.AddLauncherServices();
        using var provider = services.BuildServiceProvider();

        Assert.Equal(
            LauncherDataRoot.ForCurrentProcess().Root,
            provider.GetRequiredService<LauncherDataRoot>().Root);
    }

    public void Dispose()
    {
        tempDir.Dispose();
    }

    private ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        services.AddLauncherServices();
        services.AddSingleton(_ => new UnifiedLogger(Path.Combine(tempDir, "logs")));
        return services;
    }
}
