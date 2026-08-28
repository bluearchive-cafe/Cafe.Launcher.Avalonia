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
        var transition = new BannerCarouselTransition(TimeSpan.FromMilliseconds(500))
        {
            PendingSlide = slideMode
        };

        var transitionTask = transition.Start(from, to, forward: true, CancellationToken.None);

        await Task.Delay(50);
        Assert.NotNull(to.RenderTransform);
        Assert.True(to.IsVisible);
        Assert.Equal(0, from.ZIndex);
        Assert.Equal(1, to.ZIndex);
        Assert.InRange(to.Opacity, 0d, 1d);
        Assert.Equal(expectedOffsetSign, Math.Sign(to.RenderTransform.Value.M31));

        await transitionTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1d, to.Opacity);
        Assert.Null(to.RenderTransform);
    }
}
