using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.HeadlessTests;

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
        using var service = new SystemTrayService(window, localizer, platform);

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
        Assert.True(platform.Disposed);
        window.Close();
    }

    [AvaloniaFact]
    public void Initialize_WhenPlatformReturnsFalse_DisposesPlatform()
    {
        var platform = new TestTrayPlatform { InitializeResult = false };
        using var service = new SystemTrayService(
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
        var service = new SystemTrayService(
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
        using var service = new SystemTrayService(window, localizer, platform);

        Assert.True(service.Initialize());

        await Task.Run(() => localizer.SetLanguage(LauncherLanguages.Japanese));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, platform.UpdateCount);
    }

    private sealed class TestTrayPlatform : ISystemTrayPlatform
    {
        public bool InitializeResult { get; set; } = true;
        public int InitializeCount { get; private set; }
        public int UpdateCount { get; private set; }
        public bool Disposed { get; private set; }
        public SystemTrayMenuText Text { get; private set; } =
            new("", "", "", "", "");
        public Action? ShowWindow { get; private set; }
        public Action? ExitApplication { get; private set; }

        public bool Initialize(
            SystemTrayMenuText text,
            Action showWindow,
            Action exitApplication)
        {
            InitializeCount++;
            Text = text;
            ShowWindow = showWindow;
            ExitApplication = exitApplication;
            return InitializeResult;
        }

        public void UpdateText(SystemTrayMenuText text)
        {
            UpdateCount++;
            Text = text;
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }
}
