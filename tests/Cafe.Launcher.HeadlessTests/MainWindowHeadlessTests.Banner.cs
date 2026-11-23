using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.Core.Models;

namespace Cafe.Launcher.HeadlessTests;

public sealed partial class MainWindowHeadlessTests
{
    [AvaloniaFact]
    public void MainWindow_BannerControls_HideAfterPointerLeavesBannerStage()
    {
        using var context = CreateContext();
        ApplyRemoteState(context, state =>
        {
            state.OperationsResource = new OperationsResourceResponse
            {
                OperationsResourceOpen = true,
                BannerLoop = false,
                OperationsBannerList =
                [
                    new OperationsBannerItem { BannerImg = "", JumpUrl = "https://banner.example.invalid/1" },
                    new OperationsBannerItem { BannerImg = "", JumpUrl = "https://banner.example.invalid/2" }
                ]
            };
        });

        var bannerStage = context.Window
            .GetVisualDescendants()
            .OfType<Grid>()
            .Single(control => control.Classes.Contains("banner-stage"));
        var navigationButtons = bannerStage
            .GetVisualDescendants()
            .OfType<Button>()
            .Where(control => control.Classes.Contains("carousel-navigation"))
            .ToArray();
        var edgeGradients = bannerStage
            .GetVisualDescendants()
            .OfType<Border>()
            .Where(control => control.Classes.Contains("banner-edge-gradient"))
            .ToArray();
        Assert.Equal(2, navigationButtons.Length);
        Assert.Equal(2, edgeGradients.Length);
        Assert.All(navigationButtons, button => Assert.Equal(0, button.Opacity));
        Assert.All(edgeGradients, gradient => Assert.Equal(0, gradient.Opacity));

        var bannerTopLeft = bannerStage.TranslatePoint(default, context.Window);
        Assert.NotNull(bannerTopLeft);
        context.Window.MouseMove(
            bannerTopLeft.Value + new Point(bannerStage.Bounds.Width / 2, bannerStage.Bounds.Height / 2),
            RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.All(navigationButtons, button => Assert.Equal(1, button.Opacity));
        Assert.All(edgeGradients, gradient => Assert.Equal(1, gradient.Opacity));

        var firstButtonTopLeft = navigationButtons[0].TranslatePoint(default, context.Window);
        Assert.NotNull(firstButtonTopLeft);
        var firstButtonCenter = firstButtonTopLeft.Value
            + new Point(navigationButtons[0].Bounds.Width / 2, navigationButtons[0].Bounds.Height / 2);
        context.Window.MouseDown(firstButtonCenter, MouseButton.Left, RawInputModifiers.None);
        context.Window.MouseUp(firstButtonCenter, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        context.Window.MouseMove(new Point(0, 0), RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.All(navigationButtons, button => Assert.Equal(0, button.Opacity));
        Assert.All(edgeGradients, gradient => Assert.Equal(0, gradient.Opacity));
    }

    [AvaloniaFact]
    public void MainWindow_BannerIndicators_AreVisualOnlyAndFollowHoverVisibility()
    {
        using var context = CreateContext();
        ApplyRemoteState(context, state =>
        {
            state.OperationsResource = new OperationsResourceResponse
            {
                OperationsResourceOpen = true,
                BannerLoop = false,
                OperationsBannerList =
                [
                    new OperationsBannerItem { BannerImg = "", JumpUrl = "https://banner.example.invalid/1" },
                    new OperationsBannerItem { BannerImg = "", JumpUrl = "https://banner.example.invalid/2" }
                ]
            };
        });

        var bannerStage = context.Window
            .GetVisualDescendants()
            .OfType<Grid>()
            .Single(control => control.Classes.Contains("banner-stage"));
        var indicators = bannerStage
            .GetVisualDescendants()
            .OfType<Grid>()
            .Single(control => control.Classes.Contains("banner-indicators"));
        var dots = indicators
            .GetVisualDescendants()
            .OfType<Border>()
            .Where(control => control.Classes.Contains("banner-dot"))
            .ToArray();

        Assert.Equal(2, dots.Length);
        Assert.False(indicators.IsHitTestVisible);
        Assert.All(dots, dot => Assert.False(dot.IsHitTestVisible));
        Assert.DoesNotContain(
            indicators.GetVisualDescendants().OfType<Button>(),
            button => button.Classes.Contains("dot"));
        Assert.Equal(0, indicators.Opacity);
        Assert.Equal(12, dots[0].Bounds.Width);
        Assert.Equal(4, dots[1].Bounds.Width);

        var firstDotPosition = dots[0].TranslatePoint(default, indicators);
        var secondDotPosition = dots[1].TranslatePoint(default, indicators);
        Assert.NotNull(firstDotPosition);
        Assert.NotNull(secondDotPosition);
        Assert.Equal(
            8,
            secondDotPosition!.Value.X - (firstDotPosition!.Value.X + dots[0].Bounds.Width));

        var bannerTopLeft = bannerStage.TranslatePoint(default, context.Window);
        Assert.NotNull(bannerTopLeft);
        context.Window.MouseMove(
            bannerTopLeft!.Value + new Point(bannerStage.Bounds.Width / 2, bannerStage.Bounds.Height / 2),
            RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, indicators.Opacity);

        context.Window.MouseMove(new Point(0, 0), RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, indicators.Opacity);
    }

    [AvaloniaFact]
    public void MainWindow_BannerControls_ShowWhileBannerStageIsFocused()
    {
        using var context = CreateContext();
        ApplyRemoteState(context, state =>
        {
            state.OperationsResource = new OperationsResourceResponse
            {
                OperationsResourceOpen = true,
                BannerLoop = false,
                OperationsBannerList =
                [
                    new OperationsBannerItem { BannerImg = "", JumpUrl = "https://banner.example.invalid/1" },
                    new OperationsBannerItem { BannerImg = "", JumpUrl = "https://banner.example.invalid/2" }
                ]
            };
        });

        var bannerStage = context.Window
            .GetVisualDescendants()
            .OfType<Grid>()
            .Single(control => control.Classes.Contains("banner-stage"));
        var navigationButtons = bannerStage
            .GetVisualDescendants()
            .OfType<Button>()
            .Where(control => control.Classes.Contains("carousel-navigation"))
            .ToArray();
        Assert.Equal(2, navigationButtons.Length);
        Assert.All(navigationButtons, button => Assert.Equal(0, button.Opacity));

        navigationButtons[0].Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();

        Assert.True(context.ViewModel.RemoteContent.IsBannerInteractionActive);
        Assert.True(context.ViewModel.RemoteContent.IsCarouselPaused);
        Assert.All(navigationButtons, button => Assert.Equal(1, button.Opacity));
    }

    [AvaloniaFact]
    public void MainWindow_BannerLink_WhenPressed_PreservesBannerCompositionBounds()
    {
        using var context = CreateContext();
        ApplyRemoteState(context, state =>
        {
            state.OperationsResource = new OperationsResourceResponse
            {
                OperationsResourceOpen = true,
                BannerLoop = false,
                OperationsBannerList =
                [
                    new OperationsBannerItem
                    {
                        BannerImg = "",
                        JumpUrl = "https://banner.example.invalid/1"
                    },
                    new OperationsBannerItem
                    {
                        BannerImg = "",
                        JumpUrl = "https://banner.example.invalid/2"
                    }
                ]
            };
        });

        var bannerStage = context.Window
            .GetVisualDescendants()
            .OfType<Grid>()
            .Single(control => control.Classes.Contains("banner-stage"));
        var bannerLink = bannerStage
            .GetVisualDescendants()
            .OfType<Button>()
            .Single(control => control.Classes.Contains("banner-link"));
        var bannerMedia = bannerStage
            .GetVisualDescendants()
            .OfType<Border>()
            .Single(control => control.Classes.Contains("banner-media"));
        Assert.Same(bannerLink.Parent, bannerMedia.Parent);
        var bannerTopLeft = bannerLink.TranslatePoint(default, context.Window);
        Assert.NotNull(bannerTopLeft);
        var bannerCenter = bannerTopLeft.Value
            + new Point(bannerLink.Bounds.Width / 2, bannerLink.Bounds.Height / 2);
        var initialMediaBounds = bannerMedia.Bounds;

        context.Window.MouseMove(bannerCenter, RawInputModifiers.None);
        context.Window.MouseDown(bannerCenter, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(initialMediaBounds, bannerMedia.Bounds);

        context.Window.MouseUp(bannerCenter, MouseButton.Left, RawInputModifiers.None);
    }

    [AvaloniaTheory]
    [InlineData(1300, 754)]
    [InlineData(1024, 640)]
    public void MainWindow_BannerAndNews_FitArtworkAndShowFirstRowWithoutScrolling(double width, double height)
    {
        using var context = CreateContext();
        context.Window.Width = width;
        context.Window.Height = height;
        ApplyRemoteState(context, state =>
        {
            state.OperationsResource = new OperationsResourceResponse
            {
                OperationsResourceOpen = true,
                OperationsBannerList =
                [
                    new OperationsBannerItem { BannerImg = "", JumpUrl = "https://banner.example.invalid/1" }
                ],
                NewsList = new NewsListEnvelope
                {
                    Code = 0,
                    Data = new NewsListData
                    {
                        News = new[] { "すべて", "メンテナンス", "お知らせ", "イベント" }
                            .Select(label => new NewsTypeItem
                            {
                                TypeLabel = label,
                                Rows = Enumerable.Range(1, 4)
                                    .Select(index => new NewsRowItem
                                    {
                                        Title = $"News item {index}",
                                        Link = $"https://news.example.invalid/{index}",
                                        PublishTime = 1_700_000_000_000
                                    }).ToList()
                            }).ToList()
                    }
                }
            };
        });

        var bannerFrame = context.Window.GetVisualDescendants()
            .OfType<Border>().Single(control => control.Classes.Contains("banner-frame"));
        var newsCard = context.Window.GetVisualDescendants()
            .OfType<Border>().Single(control => control.Classes.Contains("news-card"));
        var bannerBottom = bannerFrame.TranslatePoint(new Point(0, bannerFrame.Bounds.Height), context.Window)!.Value.Y;
        var newsTop = newsCard.TranslatePoint(default, context.Window)!.Value.Y;
        Assert.InRange(newsTop - bannerBottom, -0.5, 0.5);
        Assert.Equal(new CornerRadius(8, 8, 0, 0), bannerFrame.CornerRadius);
        Assert.Equal(new CornerRadius(0, 0, 8, 8), newsCard.CornerRadius);

        var bannerImage = Assert.Single(bannerFrame.GetVisualDescendants().OfType<Image>());
        Assert.Equal(Stretch.Uniform, bannerImage.Stretch);
        var tabs = newsCard.GetVisualDescendants().OfType<TabItem>().ToArray();
        Assert.Equal(4, tabs.Length);
        Assert.Single(tabs.Select(tab => tab.TranslatePoint(default, newsCard)!.Value.Y).Distinct());

        var firstRow = newsCard.GetVisualDescendants().OfType<Button>()
            .First(button => button.Classes.Contains("news-row"));
        var viewport = context.Window.GetVisualDescendants().OfType<ScrollViewer>()
            .Single(viewer => viewer.Classes.Contains("remote-content-layout-host"));
        var rowTop = firstRow.TranslatePoint(default, viewport)!.Value.Y;
        Assert.Equal(default, viewport.Offset);
        Assert.InRange(rowTop + firstRow.Bounds.Height, 1, viewport.Bounds.Height - viewport.Padding.Bottom);
    }

    [AvaloniaFact]
    public void MainWindow_GameLinks_AppearInNewsInSourceOrder()
    {
        using var context = CreateContext();
        ApplyRemoteState(context, state =>
        {
            state.SocialMediaResource = new SocialMediaResourceResponse
            {
                SocialMediaResourceOpen = true,
                SocialMediaResourceList =
                [
                    new SocialMediaResourceItem
                    {
                        SocialMediaChannel = "YouTube",
                        JumpUrl = "https://youtube.example.invalid"
                    },
                    new SocialMediaResourceItem
                    {
                        SocialMediaChannel = "Discord",
                        JumpUrl = "https://discord.example.invalid"
                    }
                ]
            };
        });

        var news = context.Window.FindControl<Border>("HomeNews")!;
        var links = news.GetVisualDescendants().OfType<WrapPanel>()
            .Single(control => control.Classes.Contains("social-actions"));
        var buttons = links.GetVisualDescendants().OfType<Button>().ToArray();
        Assert.Equal(3, buttons.Length);
        Assert.Same(context.ViewModel.WindowChrome.OpenOfficialSiteCommand, buttons[0].Command);
        Assert.Equal(["YouTube", "Discord"], buttons.Skip(1).Select(AutomationProperties.GetName));
        Assert.Equal(["https://youtube.example.invalid", "https://discord.example.invalid"],
            buttons.Skip(1).Select(button => button.CommandParameter));
        Assert.All(buttons, button =>
        {
            Assert.True(button.IsEffectivelyVisible);
            AssertControlInsideWindow(button, context.Window);
        });

        context.ViewModel.RemoteContent.UpdateRemoteContentVisibility(false);
        Dispatcher.UIThread.RunJobs();
        Assert.All(buttons, button => Assert.False(button.IsEffectivelyVisible));

        context.ViewModel.RemoteContent.UpdateRemoteContentVisibility(true);
        Dispatcher.UIThread.RunJobs();
        Assert.All(buttons, button => Assert.True(button.IsEffectivelyVisible));
    }
}
