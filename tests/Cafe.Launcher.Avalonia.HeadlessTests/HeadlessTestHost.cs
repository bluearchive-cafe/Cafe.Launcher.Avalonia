using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
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
    /// <summary>建立带真实 DI 的测试上下文；日志写入独立临时目录，不污染用户目录。</summary>
    public static LauncherHeadlessContext CreateContext()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var services = new ServiceCollection();
        services.AddLauncherServices();
        services.AddSingleton(_ => new UnifiedLogger(Path.Combine(tempDir, "logs")));
        var provider = services.BuildServiceProvider();
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
    }
}
