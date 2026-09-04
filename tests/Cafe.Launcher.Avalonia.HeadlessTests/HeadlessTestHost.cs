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
    /// 构造共享 DI 容器：真实 AddLauncherServices + 独立临时目录日志（tempDir 仅供日志
    /// 落盘使用）。configure 在注册日志之前执行，用于追加或覆盖服务（如注入测试用
    /// IGameOperationExecutor）。MainWindowHeadlessTests 的上下文构造也复用此方法。
    /// </summary>
    public static ServiceProvider CreateServiceProvider(
        string tempDir,
        Action<ServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLauncherServices();
        configure?.Invoke(services);
        services.AddSingleton(_ => new UnifiedLogger(Path.Combine(tempDir, "logs")));
        return services.BuildServiceProvider();
    }

    /// <summary>建立带真实 DI 的测试上下文；日志写入独立临时目录，不污染用户目录。</summary>
    public static LauncherHeadlessContext CreateContext()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var provider = CreateServiceProvider(tempDir);
        return new LauncherHeadlessContext(tempDir, provider);
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
    /// 有界轮询等待：每轮先泵一次 UI 线程（InvokeAsync + Task.Delay(10)）再评估条件，
    /// 条件满足即返回；超时抛 TimeoutException（failureMessage 用于说明被等待的语义）。
    /// </summary>
    public static async Task WaitUntilAsync(
        Func<bool> condition,
        TimeSpan timeout,
        string? failureMessage = null)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { });
            await Task.Delay(10);
            if (condition())
            {
                return;
            }
        }

        throw new TimeoutException(
            failureMessage ?? $"Condition was not met within {timeout.TotalSeconds:0.#} seconds.");
    }
}

internal sealed class LauncherHeadlessContext : IDisposable
{
    public LauncherHeadlessContext(string tempDir, ServiceProvider provider)
    {
        TempDir = tempDir;
        Provider = provider;
        Appearance = provider.GetRequiredService<SettingsViewModel>().Appearance;
        ViewModel = provider.GetRequiredService<BackgroundViewModel>();
    }

    public string TempDir { get; }

    public ServiceProvider Provider { get; }

    public SettingsAppearanceViewModel Appearance { get; }

    public BackgroundViewModel ViewModel { get; }

    public void Dispose()
    {
        ViewModel.Dispose();
        Provider.Dispose();
        try
        {
            // DI 容器释放后日志句柄已关闭；删除失败（如句柄延迟释放）仅残留临时目录，
            // 不让清理问题掩盖测试结果。
            Directory.Delete(TempDir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
