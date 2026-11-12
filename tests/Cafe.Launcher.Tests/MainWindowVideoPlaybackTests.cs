using Avalonia.Controls;
using Cafe.Launcher.UI.Views;

namespace Cafe.Launcher.Tests;

public sealed class MainWindowVideoPlaybackTests
{
    [Theory]
    [InlineData(true, true, WindowState.Normal, true, false)]
    [InlineData(true, true, WindowState.Normal, false, true)]
    [InlineData(true, false, WindowState.Normal, true, true)]
    [InlineData(true, true, WindowState.Minimized, true, true)]
    [InlineData(false, true, WindowState.Normal, true, true)]
    public void IsPlaybackUnavailable_CoversSourceVisibilityStateAndFocus(
        bool hasSource, bool isVisible, WindowState windowState, bool isActive, bool expected)
    {
        // Headless 无法制造「可见但失焦」的窗口，失焦分支在这里覆盖。
        Assert.Equal(expected, new VideoPlaybackConditions(hasSource, isVisible, windowState, isActive).IsUnavailable);
    }
}
