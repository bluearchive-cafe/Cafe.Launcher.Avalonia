using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Cafe.Launcher.Avalonia.Composition;
using Cafe.Launcher.Avalonia.Features.Settings;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.Testing;
using Cafe.Launcher.Avalonia.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Cafe.Launcher.Avalonia.HeadlessTests;

/// <summary>
/// 无头测试共享支撑：真实 DI 上下文构造与纯色 PNG 造图，供背景解码/主题取色等
/// 需要可控源图的测试复用。
/// </summary>
internal static class HeadlessTestHost
{
    /// <summary>
    /// 构造共享 DI 容器：真实 AddLauncherServices，数据根与日志都落在
    /// <paramref name="directory"/> 里。configure 在日志注册之前执行，用于追加或覆盖服务
    /// （如注入测试用 IGameOperationExecutor）。MainWindowHeadlessTests 的上下文构造也复用此方法。
    /// </summary>
    /// <remarks>
    /// 数据根必须显式传入而不是让它按进程解析：进程根是按程序集隔离的共享目录，
    /// 用它会让同一程序集里的两个上下文互相看见对方的设置、下载检查点与崩溃快照——
    /// 那正是「每个上下文一个独立数据根」要消掉的耦合。
    /// </remarks>
    public static ServiceProvider CreateServiceProvider(
        TestDirectory directory,
        Action<ServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLauncherServices(launcherDataRoot: directory.DataRoot);
        configure?.Invoke(services);
        services.AddSingleton(_ => new UnifiedLogger(directory.Sub("logs")));
        return services.BuildServiceProvider();
    }

    /// <summary>建立带真实 DI 的测试上下文；数据与日志都写在专属目录，不污染其他上下文。</summary>
    public static LauncherHeadlessContext CreateContext()
    {
        // 无头拆卸用尽力清理：窗口关闭与句柄释放是异步的，删除失败只该留下一个临时目录。
        var directory = TestDirectory.Create(TestDirectoryCleanup.BestEffort);
        var provider = CreateServiceProvider(directory);
        return new LauncherHeadlessContext(directory, provider);
    }

    /// <summary>渲染纯色 Border 并保存为 PNG，造出尺寸可控的源图。</summary>
    public static void WriteSolidPng(string path, IBrush brush, int width, int height)
    {
        var border = new Border { Width = width, Height = height, Background = brush };
        border.Measure(new Size(width, height));
        border.Arrange(new Rect(0, 0, width, height));
        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height));
        bitmap.Render(border);
        using var stream = File.Create(path);
        bitmap.Save(stream, new PngBitmapEncoderOptions());
    }

    public static Bitmap WriteSolidPngBitmap(
        string tempDir,
        string fileName,
        IBrush brush,
        int width,
        int height)
    {
        var path = Path.Combine(tempDir, fileName);
        WriteSolidPng(path, brush, width, height);
        return new Bitmap(path);
    }

    /// <summary>
    /// 有界轮询等待：每轮先泵一次 UI 线程（InvokeAsync + 轮询间隔）再评估条件，
    /// 条件满足即返回；超时抛 TimeoutException（failureMessage 用于说明被等待的语义）。
    /// 计时与超时语义在 <see cref="TestWait"/>，这里只补 UI 调度那一半。
    /// </summary>
    /// <remarks>
    /// 首次评估之前必须先泵一轮：条件常常读的是动效落定的产物（自适尺寸 ＋ 全不透明），
    /// 而未驱动的 UI 线程上那两个锚点的初始值恰好也满足条件——不泵就评估会把
    /// 「还没开始过渡」当成「已经落定」。
    /// </remarks>
    public static async Task WaitUntilAsync(
        Func<bool> condition,
        TimeSpan timeout,
        string? failureMessage = null)
    {
        await PumpUiThreadAsync();
        await TestWait.UntilAsync(
            condition,
            timeout,
            failureMessage,
            tickAsync: PumpUiThreadAsync);
    }

    private static async ValueTask PumpUiThreadAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(static () => { });
        await Task.Delay(TestWait.PollInterval);
    }
}

internal sealed class LauncherHeadlessContext : IDisposable
{
    public LauncherHeadlessContext(TestDirectory directory, ServiceProvider provider)
    {
        Directory = directory;
        Provider = provider;
        Appearance = provider.GetRequiredService<SettingsViewModel>().Appearance;
        ViewModel = provider.GetRequiredService<BackgroundViewModel>();
    }

    public TestDirectory Directory { get; }

    public ServiceProvider Provider { get; }

    public SettingsAppearanceViewModel Appearance { get; }

    public BackgroundViewModel ViewModel { get; }

    /// <summary>拆卸顺序即所有权顺序：先放掉消费者，再放容器，最后删目录。</summary>
    public void Dispose()
    {
        ViewModel.Dispose();
        Provider.Dispose();
        Directory.Dispose();
    }
}
