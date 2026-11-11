using System.Reflection;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.UI.Controls.VideoBackground;

namespace Cafe.Launcher.HeadlessTests;

public sealed partial class MainWindowHeadlessTests
{
    [AvaloniaFact]
    public async Task VideoBackground_WhenSourceAndWindowStateChange_ReusesSurfaceAndRestoresFallback()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Uses the bundled Windows runtime path.");
        using var context = CreateContext();
        var video = context.Directory.Sub("first.mp4");
        var second = context.Directory.Sub("second.webm");
        await File.WriteAllBytesAsync(video, []);
        await File.WriteAllBytesAsync(second, []);
        var vm = context.ViewModel;
        var settings = new LauncherSettings { BackgroundSource = BackgroundSources.Custom, CustomBackgroundPath = video };
        await vm.Background.UpdateBackgroundImageAsync(settings, null, CancellationToken.None);
        // No GPU is initialized in headless mode. This checks the real window's
        // ownership and binding path; native rendering is a separate desktop check.
        VideoBackgroundControl? Current() => context.Window.FindControl<Grid>("ShellRoot")!.Children.OfType<VideoBackgroundControl>().SingleOrDefault();
        var original = Assert.IsType<VideoBackgroundControl>(Current());
        Assert.Equal(1, original.Opacity); // Zero opacity suppresses GL initialization.
        Assert.True(original.PlaybackPaused);
        Assert.False(original.ShowStillFrame);
        vm.IsMotionReduced = false;
        context.Window.Show();
        // Headless posts activation asynchronously; unfocused playback stays paused.
        Dispatcher.UIThread.RunJobs();
        Assert.True(context.Window.IsActive);
        Assert.False(original.PlaybackPaused);
        Assert.False(original.ShowStillFrame);
        vm.Background.ApplyBackgroundPresentation(new LauncherSettings
        {
            BackgroundVideoVolume = 35, BackgroundFit = BackgroundFits.Uniform, BackgroundFillColor = "#FF112233"
        });
        Assert.Equal(35, original.Volume);
        Assert.Equal(Stretch.Uniform, original.Stretch);
        Assert.Equal(Color.Parse("#FF112233"), original.FillColor);

        // Simulate the native first-frame notification; Headless has no GL renderer.
        typeof(Cafe.Launcher.UI.Views.MainWindow).GetMethod("OnVideoBackgroundFirstFrame", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(context.Window, null);
        Assert.All(context.Window.FindControl<Grid>("ShellRoot")!.Children.OfType<Image>(), image => Assert.False(image.IsVisible));
        var shellCache = context.Window.FindControl<Grid>("MainContentLayer")!.CacheMode;
        Assert.NotNull(shellCache);

        settings.CustomBackgroundPath = second;
        await vm.Background.UpdateBackgroundImageAsync(settings, null, CancellationToken.None);
        Assert.Same(original, Current());
        Assert.Equal(second, original.Source);
        Assert.All(context.Window.FindControl<Grid>("ShellRoot")!.Children.OfType<Image>(), image => Assert.True(image.IsVisible));
        context.Window.Hide();
        Dispatcher.UIThread.RunJobs();
        Assert.False(context.Window.IsActive);
        Assert.True(original.PlaybackPaused);
        Assert.False(original.ShowStillFrame);
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.False(original.PlaybackPaused);
        context.Window.WindowState = WindowState.Minimized;
        Assert.True(original.PlaybackPaused);
        context.Window.WindowState = WindowState.Normal;
        vm.IsMotionReduced = true;
        Assert.True(original.PlaybackPaused);
        Assert.True(original.ShowStillFrame);

        vm.Background.ReportVideoFailure(video, new InvalidOperationException("Stale failure"));
        Assert.Same(original, Current());
        vm.Background.ReportVideoFailure(second, new InvalidOperationException("Invalid media"));
        Assert.Same(original, Current());
        Assert.Null(original.Source);
        Assert.True(original.PlaybackPaused);
        Assert.All(context.Window.FindControl<Grid>("ShellRoot")!.Children.OfType<Image>(), image => Assert.True(image.IsVisible));
        // 缓存随视频控件存活；切回静态背景不重建全窗口位图缓存。
        Assert.Same(shellCache, context.Window.FindControl<Grid>("MainContentLayer")!.CacheMode);

        settings.CustomBackgroundPath = video;
        await vm.Background.UpdateBackgroundImageAsync(settings, null, CancellationToken.None);
        Assert.Same(original, Current());
        Assert.Equal(video, original.Source);
        await vm.Background.UpdateBackgroundImageAsync(new LauncherSettings(), null, CancellationToken.None);
        Assert.Same(original, Current());
        Assert.Null(original.Source);
        context.Window.Close();
        Assert.Null(Current());
        Assert.Null(context.Window.FindControl<Grid>("MainContentLayer")!.CacheMode);
        Dispatcher.UIThread.RunJobs();
    }
}
