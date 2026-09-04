using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.HeadlessTests;

public sealed partial class MainWindowHeadlessTests
{
    [AvaloniaFact]
    public void MainWindow_BannerControls_HideAfterPointerLeavesBannerStage()
    {
        using var context = CreateContext();
        context.ViewModel.RemoteContent.Apply(
            new LauncherRemoteState
            {
                OperationsResource = new OperationsResourceResponse
                {
                    OperationsResourceOpen = true,
                    BannerLoop = false,
                    OperationsBannerList =
                    [
                        new OperationsBannerItem { BannerImg = "", JumpUrl = "https://banner.example.invalid/1" },
                        new OperationsBannerItem { BannerImg = "", JumpUrl = "https://banner.example.invalid/2" }
                    ]
                }
            },
            new LauncherSettings { ShowRemoteContentCard = true },
            CancellationToken.None);
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

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
        context.ViewModel.RemoteContent.Apply(
            new LauncherRemoteState
            {
                OperationsResource = new OperationsResourceResponse
                {
                    OperationsResourceOpen = true,
                    BannerLoop = false,
                    OperationsBannerList =
                    [
                        new OperationsBannerItem { BannerImg = "", JumpUrl = "https://banner.example.invalid/1" },
                        new OperationsBannerItem { BannerImg = "", JumpUrl = "https://banner.example.invalid/2" }
                    ]
                }
            },
            new LauncherSettings { ShowRemoteContentCard = true },
            CancellationToken.None);
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

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
        context.ViewModel.RemoteContent.Apply(
            new LauncherRemoteState
            {
                OperationsResource = new OperationsResourceResponse
                {
                    OperationsResourceOpen = true,
                    BannerLoop = false,
                    OperationsBannerList =
                    [
                        new OperationsBannerItem { BannerImg = "", JumpUrl = "https://banner.example.invalid/1" },
                        new OperationsBannerItem { BannerImg = "", JumpUrl = "https://banner.example.invalid/2" }
                    ]
                }
            },
            new LauncherSettings { ShowRemoteContentCard = true },
            CancellationToken.None);
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

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
        context.ViewModel.RemoteContent.Apply(
            new LauncherRemoteState
            {
                OperationsResource = new OperationsResourceResponse
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
                }
            },
            new LauncherSettings { ShowRemoteContentCard = true },
            CancellationToken.None);
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

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

    [AvaloniaFact]
    public void MainWindow_SocialActions_AddTopRightVerticalButtonsInSourceOrder()
    {
        using var context = CreateContext();
        context.ViewModel.RemoteContent.Apply(
            new LauncherRemoteState
            {
                SocialMediaResource = new SocialMediaResourceResponse
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
                }
            },
            new LauncherSettings { ShowRemoteContentCard = true },
            CancellationToken.None);
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var actionButtons = context.Window
            .GetVisualDescendants()
            .OfType<Button>()
            .Where(control => control.Classes.Contains("social-action"))
            .ToArray();

        var officialSiteButton = Assert.Single(
            actionButtons,
            control => control.Classes.Contains("official-site"));
        var itemButtons = actionButtons
            .Where(control => control.DataContext is RemoteContentItem)
            .ToArray();

        Assert.Equal(2, itemButtons.Length);
        Assert.Equal(["YouTube", "Discord"], itemButtons.Select(button => ((RemoteContentItem)button.DataContext!).Title));
        Assert.DoesNotContain(
            context.Window.GetVisualDescendants().OfType<Border>(),
            control => control.Classes.Contains("social-media-card"));
        Assert.All(actionButtons, button =>
        {
            Assert.Equal(36, button.Bounds.Width);
            Assert.Equal(36, button.Bounds.Height);
            AssertControlInsideWindow(button, context.Window);
            Assert.True(AutomationProperties.GetName(button) is not null);
        });

        var officialTop = officialSiteButton.TranslatePoint(default, context.Window);
        var firstTop = itemButtons[0].TranslatePoint(default, context.Window);
        var secondTop = itemButtons[1].TranslatePoint(default, context.Window);
        Assert.NotNull(officialTop);
        Assert.NotNull(firstTop);
        Assert.NotNull(secondTop);
        Assert.True(officialTop.Value.X > context.Window.ClientSize.Width / 2);
        Assert.True(officialTop.Value.Y < firstTop.Value.Y);
        Assert.True(firstTop.Value.Y < secondTop.Value.Y);

        var socialActions = context.Window
            .GetVisualDescendants()
            .OfType<StackPanel>()
            .Single(control => control.Classes.Contains("social-actions"));
        var socialItems = socialActions
            .GetVisualDescendants()
            .OfType<ItemsControl>()
            .Single(control => control.ItemsSource is not null);
        Assert.True(socialActions.IsEffectivelyVisible);
        Assert.True(socialItems.IsEffectivelyVisible);

        context.ViewModel.RemoteContent.UpdateRemoteContentVisibility(false);
        Dispatcher.UIThread.RunJobs();
        Assert.True(socialActions.IsEffectivelyVisible);
        Assert.False(socialItems.IsEffectivelyVisible);
        Assert.False(officialSiteButton.IsEffectivelyVisible);

        context.ViewModel.RemoteContent.UpdateRemoteContentVisibility(true);
        Dispatcher.UIThread.RunJobs();
        Assert.True(socialActions.IsEffectivelyVisible);
        Assert.True(socialItems.IsEffectivelyVisible);
        Assert.True(officialSiteButton.IsEffectivelyVisible);
    }
}
