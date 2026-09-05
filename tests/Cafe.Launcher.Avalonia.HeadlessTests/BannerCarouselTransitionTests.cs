using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Cafe.Launcher.Avalonia.Helpers;

namespace Cafe.Launcher.Avalonia.HeadlessTests;

public sealed class BannerCarouselTransitionTests
{
    [AvaloniaTheory]
    [InlineData(BannerCarouselTransition.CarouselSlideMode.Forward, 1)]
    [InlineData(BannerCarouselTransition.CarouselSlideMode.Backward, -1)]
    public async Task Start_DirectionalSlide_AnimatesIncomingBannerAboveOutgoingBanner(
        BannerCarouselTransition.CarouselSlideMode slideMode,
        int expectedOffsetSign)
    {
        var from = new Border { ZIndex = 1 };
        var to = new Border { IsVisible = false };
        var transition = new BannerCarouselTransition(TimeSpan.FromMilliseconds(250))
        {
            PendingSlide = slideMode
        };

        var transitionTask = transition.Start(from, to, forward: true, CancellationToken.None);

        // 轮询采样直到观察到位移进行中：单点定时采样（如固定 50ms）在慢机上
        // 会错过过渡窗口，导致动画已结算而误报。首轮采样先于任何异步等待，
        // 避免高负载（coverage 插桩）下首帧采样晚于动画结算。
        var sawIntermediateFrame = false;
        var samplingDeadline = DateTime.UtcNow.AddSeconds(2);
        while (!sawIntermediateFrame)
        {
            if (DateTime.UtcNow >= samplingDeadline)
            {
                Assert.Fail("未在 2 秒预算内观察到方向滑入的中间帧，过渡疑似瞬变。");
            }

            sawIntermediateFrame = to.RenderTransform is not null
                && Math.Abs(to.RenderTransform.Value.M31) > 0.5d;
            if (sawIntermediateFrame)
            {
                break;
            }

            await Task.Delay(1);
        }

        Assert.True(to.IsVisible);
        Assert.Equal(0, from.ZIndex);
        Assert.Equal(1, to.ZIndex);
        Assert.InRange(to.Opacity, 0d, 1d);
        Assert.NotNull(to.RenderTransform);
        Assert.Equal(expectedOffsetSign, Math.Sign(to.RenderTransform.Value.M31));

        await transitionTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1d, to.Opacity);
        Assert.Null(to.RenderTransform);
    }
}
