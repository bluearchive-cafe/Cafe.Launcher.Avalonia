using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.UI.Services;
using Cafe.Launcher.Core.Models;
using Cafe.Launcher.Core.Constants;

namespace Cafe.Launcher.HeadlessTests;

public sealed class SystemTrayServiceTests
{
    [AvaloniaFact]
    public void Initialize_ShowHideLanguageChangeAndExit_UsePlatformAdapter()
    {
        var window = new Window();
        window.Show();
        var localizer = new LocalizationService();
        localizer.SetLanguage(LauncherLanguages.English);
        var platform = new TestTrayPlatform();
        using var service = new SystemTrayService(LauncherProfiles.Cafe, window, localizer, platform);

        Assert.True(service.Initialize());
        Assert.True(service.Initialize());
        Assert.Equal(1, platform.InitializeCount);
        Assert.NotEmpty(platform.Text.Show);

        service.HideWindow();
        Assert.False(window.IsVisible);

        platform.ShowWindow?.Invoke();
        Assert.True(window.IsVisible);
        Assert.Equal(WindowState.Normal, window.WindowState);

        localizer.SetLanguage(LauncherLanguages.Japanese);
        Assert.Equal(1, platform.UpdateCount);

        platform.ExitApplication?.Invoke();
        // 生命周期确认退出后才释放托盘；无桌面生命周期的无头宿主保持适配器可用。
        Assert.False(platform.Disposed);
        service.Dispose();
        Assert.True(platform.Disposed);
        window.Close();
    }

    [AvaloniaFact]
    public void Initialize_WhenPlatformReturnsFalse_DisposesPlatform()
    {
        var platform = new TestTrayPlatform { InitializeResult = false };
        using var service = new SystemTrayService(LauncherProfiles.Cafe, 
            new Window(),
            new LocalizationService(),
            platform);

        Assert.False(service.Initialize());
        Assert.True(platform.Disposed);
    }

    [AvaloniaFact]
    public void Initialize_WhenDisposed_ReturnsFalse()
    {
        var platform = new TestTrayPlatform();
        var service = new SystemTrayService(LauncherProfiles.Cafe, 
            new Window(),
            new LocalizationService(),
            platform);

        service.Dispose();

        Assert.False(service.Initialize());
        Assert.True(platform.Disposed);
    }

    [AvaloniaFact]
    public async Task Initialize_WhenLanguageChangesOffUiThread_PostsUpdateToPlatform()
    {
        var window = new Window();
        var localizer = new LocalizationService();
        localizer.SetLanguage(LauncherLanguages.English);
        var platform = new TestTrayPlatform();
        using var service = new SystemTrayService(LauncherProfiles.Cafe, window, localizer, platform);

        Assert.True(service.Initialize());

        await Task.Run(() => localizer.SetLanguage(LauncherLanguages.Japanese));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, platform.UpdateCount);
    }
}
