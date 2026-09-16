using System;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Cafe.Launcher.Avalonia.Features.Settings;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Testing;
using Cafe.Launcher.Avalonia.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cafe.Launcher.Avalonia.HeadlessTests;

/// <summary>
/// AUD-TEST-007 回归守卫：主题变体订阅的拆卸（696abdd 新增）此前无任何测试
/// 断言——删除 Dispose 中的退订行全套件仍然全绿，headless 共享 Application
/// 的跨测试订阅累积可静默回归。哨兵法：武装 System 模式的方案重刷处理器后，
/// 把方案画刷改为哨兵色；对照相证明变体翻转确实触发处理器，随后 Dispose，
/// 再翻转变体——哨兵色若被改写即订阅泄漏。
/// </summary>
public sealed class ThemeSubscriptionTeardownHeadlessTests
{
    private const string ProbeKey = "Launcher.Color.Dialog.Background";
    private static readonly Color SentinelColor = Color.Parse("#FF0A5A7A");

    [AvaloniaFact]
    public void Appearance_WhenDisposed_DoesNotReapplySchemeOnThemeVariantChange()
    {
        var application = Application.Current
            ?? throw new InvalidOperationException("Headless application is not initialised.");
        var variantSnapshot = application.RequestedThemeVariant;
        // 无头拆卸用尽力清理：窗口关闭与句柄释放是异步的。
        var directory = TestDirectory.Create(TestDirectoryCleanup.BestEffort);
        var provider = HeadlessTestHost.CreateServiceProvider(directory);
        try
        {
            var appearance = provider.GetRequiredService<MainWindowViewModel>().Settings.Appearance;
            // 武装：System 模式 + 已应用方案，ActualThemeVariantChanged 处理器在位。
            appearance.ApplyTheme(ThemeModes.System);
            var seed = Color.Parse("#FF2E9E46");
            appearance.ApplyScheme(
                seed,
                ThemeColorVariants.TonalSpot,
                isDark: false,
                NeutralColorStrategies.BrandBlue);
            var probeBrush = GetProbeBrush(application);

            // 对照相（自证哨兵有效）：变体翻转必须触发处理器重刷方案。
            probeBrush.Color = SentinelColor;
            application.RequestedThemeVariant = ThemeVariant.Dark;
            Assert.NotEqual(SentinelColor, probeBrush.Color);

            // 拆卸相：Dispose 后变体翻转不得再改写方案——哨兵色存活即订阅已退订。
            provider.Dispose();
            probeBrush.Color = SentinelColor;
            application.RequestedThemeVariant = ThemeVariant.Light;
            Assert.Equal(SentinelColor, probeBrush.Color);
        }
        finally
        {
            application.RequestedThemeVariant = variantSnapshot;
            directory.Dispose();
        }
    }

    private static SolidColorBrush GetProbeBrush(Application application)
    {
        Assert.True(
            application.Resources.TryGetResource(ProbeKey, ThemeVariant.Light, out var value),
            $"Missing scheme resource '{ProbeKey}'.");
        return Assert.IsType<SolidColorBrush>(value);
    }
}
