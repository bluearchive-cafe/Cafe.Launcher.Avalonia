using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Cafe.Launcher.UI.Services;

namespace Cafe.Launcher.HeadlessTests;

/// <summary>
/// <see cref="AvaloniaSystemTrayPlatform"/> 与 Avalonia 托盘契约的回归测试。
/// 自 Avalonia 12.1.3 起，<c>ITrayIconImpl</c> 的建与销都挂在 <c>TrayIcon.Attach/Detach</c> 上，
/// 而 Attach 只由 Application 级 <c>TrayIcon.Icons</c> 集合的变更回调触发：单独
/// <c>new TrayIcon { IsVisible = true }</c> 是空操作，通知区域不会出现任何图标，
/// 且调用方拿到的仍是「成功」。
/// 无头平台没有托盘实现（<c>IWindowingPlatform.CreateTrayIcon</c> 返回 null），
/// 因此这里断言的是决定「有没有图标」的那条注册契约，而不是通知区域本身。
/// </summary>
public sealed class AvaloniaSystemTrayPlatformTests
{
    private const string Title = "tray-platform-test";

    [AvaloniaFact]
    public void Initialize_ThenDispose_RegistersAndReleasesTheIconInTheApplicationTrayIcons()
    {
        var application = Assert.IsAssignableFrom<Application>(Application.Current);
        int baseline = TrayIcon.GetIcons(application)?.Count ?? 0;

        using (var platform = new AvaloniaSystemTrayPlatform())
        {
            Assert.True(platform.Initialize(
                new SystemTrayMenuText(Title, "show", "show tip", "exit", "exit tip", "start", true, "settings", true),
                () => { },
                () => { },
                () => { },
                () => { }));

            var icons = TrayIcon.GetIcons(application);
            Assert.NotNull(icons);
            Assert.Equal(baseline + 1, icons!.Count);

            var icon = Assert.Single(
                icons,
                candidate => string.Equals(candidate.ToolTipText, Title, StringComparison.Ordinal));
            Assert.True(icon.IsVisible);
            Assert.NotNull(icon.Menu);
        }

        Assert.Equal(baseline, TrayIcon.GetIcons(application)?.Count ?? 0);
    }
}
