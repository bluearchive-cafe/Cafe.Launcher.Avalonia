using System;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Xunit;

namespace Cafe.Launcher.Avalonia.HeadlessTests;

/// <summary>
/// AUD-TEST-013 回归守卫：快照设施必须还原它进入前的变体。还原一旦静默失效，被它保护的用例
/// 就重新变成「谁先跑谁说了算」，而这在 golden 上表现为偶然绿而不是红——所以这条守卫断言的是
/// 还原这个动作本身，不依赖任何渲染结果。
/// </summary>
public sealed class ThemeVariantSnapshotHeadlessTests
{
    [AvaloniaFact]
    public void Capture_WhenDisposed_RestoresTheVariantItFound()
    {
        var application = Application.Current
            ?? throw new InvalidOperationException("Headless application is not initialised.");
        var original = application.RequestedThemeVariant;
        var pinned = ReferenceEquals(original, ThemeVariant.Light)
            ? ThemeVariant.Dark
            : ThemeVariant.Light;

        using (ThemeVariantSnapshot.Capture(pinned))
        {
            Assert.Equal(pinned, application.RequestedThemeVariant);
        }

        Assert.Equal(original, application.RequestedThemeVariant);
    }
}
