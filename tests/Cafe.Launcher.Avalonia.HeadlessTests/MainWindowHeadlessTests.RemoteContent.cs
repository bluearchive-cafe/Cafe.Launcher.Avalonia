using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.HeadlessTests;

public sealed partial class MainWindowHeadlessTests
{
    [AvaloniaFact]
    public void MainWindow_NewsList_WithMoreThanThreeItems_KeepsAllItemsAccessibleByScrolling()
    {
        using var context = CreateContext();
        const string longTitle =
            "This deliberately long launcher news title must be truncated to one visible line without being clipped by the fixed-height clickable row";
        var rows = Enumerable.Range(1, 5)
            .Select(index => new NewsRowItem
            {
                Title = index == 1 ? longTitle : $"News item {index}",
                Link = $"https://news.example.invalid/{index}",
                PublishTime = 1_700_000_000_000 + index
            })
            .ToList();
        context.ViewModel.RemoteContent.Apply(
            new LauncherRemoteState
            {
                OperationsResource = new OperationsResourceResponse
                {
                    OperationsResourceOpen = true,
                    NewsList = new NewsListEnvelope
                    {
                        Code = 0,
                        Data = new NewsListData
                        {
                            News =
                            [
                                new NewsTypeItem
                                {
                                    TypeLabel = "News",
                                    Rows = rows
                                }
                            ]
                        }
                    }
                }
            },
            new LauncherSettings { ShowRemoteContentCard = true },
            CancellationToken.None);
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var viewport = context.Window
            .GetVisualDescendants()
            .OfType<ScrollViewer>()
            .Single(control => control.Classes.Contains("news-viewport"));
        var rowButtons = viewport
            .GetVisualDescendants()
            .OfType<Button>()
            .Where(control => control.Classes.Contains("news-row"))
            .ToArray();

        Assert.Equal(5, context.ViewModel.RemoteContent.SelectedNewsCategory?.Items.Count);
        Assert.Equal(5, rowButtons.Length);
        Assert.Equal(184, viewport.Viewport.Height);
        Assert.All(rowButtons, row => Assert.Equal(36, row.Bounds.Height));
        Assert.True(viewport.Extent.Height > viewport.Viewport.Height);
        Assert.Equal(longTitle, ToolTip.GetTip(rowButtons[0]));

        var longTitleText = rowButtons[0]
            .GetVisualDescendants()
            .OfType<TextBlock>()
            .Single(control => control.Text == longTitle);
        var longTitleTop = longTitleText.TranslatePoint(default, rowButtons[0]);
        Assert.Single(longTitleText.TextLayout.TextLines);
        Assert.True(longTitleText.TextLayout.Height <= longTitleText.Bounds.Height);
        Assert.NotNull(longTitleTop);
        Assert.True(longTitleTop.Value.Y >= 0);
        Assert.True(longTitleTop.Value.Y + longTitleText.Bounds.Height <= rowButtons[0].Bounds.Height);

        viewport.Offset = new Vector(0, viewport.Extent.Height - viewport.Viewport.Height);
        Dispatcher.UIThread.RunJobs();

        var lastRowTop = rowButtons[^1].TranslatePoint(default, viewport);
        Assert.NotNull(lastRowTop);
        Assert.InRange(lastRowTop.Value.Y, 0, viewport.Viewport.Height - rowButtons[^1].Bounds.Height);
    }

    [AvaloniaFact]
    public void MainWindow_NewsList_WithThreeItems_SizesViewportToContent()
    {
        using var context = CreateContext();
        var rows = Enumerable.Range(1, 3)
            .Select(index => new NewsRowItem
            {
                Title = $"News item {index}",
                Link = $"https://news.example.invalid/{index}",
                PublishTime = 1_700_000_000_000 + index
            })
            .ToList();
        context.ViewModel.RemoteContent.Apply(
            new LauncherRemoteState
            {
                OperationsResource = new OperationsResourceResponse
                {
                    OperationsResourceOpen = true,
                    NewsList = new NewsListEnvelope
                    {
                        Code = 0,
                        Data = new NewsListData
                        {
                            News =
                            [
                                new NewsTypeItem
                                {
                                    TypeLabel = "News",
                                    Rows = rows
                                }
                            ]
                        }
                    }
                }
            },
            new LauncherSettings { ShowRemoteContentCard = true },
            CancellationToken.None);
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var viewport = context.Window
            .GetVisualDescendants()
            .OfType<ScrollViewer>()
            .Single(control => control.Classes.Contains("news-viewport"));
        var rowButtons = viewport
            .GetVisualDescendants()
            .OfType<Button>()
            .Where(control => control.Classes.Contains("news-row"))
            .ToArray();

        Assert.Equal(3, rowButtons.Length);
        Assert.All(rowButtons, row => Assert.Equal(36, row.Bounds.Height));
        Assert.Equal(124, viewport.Viewport.Height);
        Assert.Equal(viewport.Viewport.Height, viewport.Extent.Height);
    }

    [AvaloniaFact]
    public void MainWindow_RemoteContent_WithOverflow_ScrollsOuterTransparentContainer()
    {
        using var context = CreateContext();
        context.ViewModel.RemoteContent.Apply(
            new LauncherRemoteState
            {
                BaseConfig = new BaseConfigResponse
                {
                    NoticePopOpen = true,
                    NoticeContent = new string('N', 500)
                },
                OperationsResource = new OperationsResourceResponse
                {
                    OperationsResourceOpen = true,
                    OperationsBannerList =
                    [
                        new OperationsBannerItem
                        {
                            BannerImg = "",
                            JumpUrl = "https://news.example.invalid/banner"
                        }
                    ],
                    NewsList = new NewsListEnvelope
                    {
                        Code = 0,
                        Data = new NewsListData
                        {
                            News =
                            [
                                new NewsTypeItem
                                {
                                    TypeLabel = "News",
                                    Rows = Enumerable.Range(1, 3)
                                        .Select(index => new NewsRowItem
                                        {
                                            Title = $"News item {index}",
                                            Link = $"https://news.example.invalid/{index}",
                                            PublishTime = 1_700_000_000_000 + index
                                        })
                                        .ToList()
                                }
                            ]
                        }
                    }
                }
            },
            new LauncherSettings { ShowRemoteContentCard = true },
            CancellationToken.None);
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var layoutHost = context.Window
            .GetVisualDescendants()
            .OfType<ScrollViewer>()
            .Single(control => control.Classes.Contains("remote-content-layout-host"));

        Assert.True(layoutHost.Extent.Height > layoutHost.Viewport.Height);
    }

    [AvaloniaFact]
    public void MainWindow_NewsTabs_KeepVisualGapBetweenHeaderAndSelectedIndicator()
    {
        using var context = CreateContext();
        context.ViewModel.RemoteContent.Apply(
            new LauncherRemoteState
            {
                OperationsResource = new OperationsResourceResponse
                {
                    OperationsResourceOpen = true,
                    NewsList = new NewsListEnvelope
                    {
                        Code = 0,
                        Data = new NewsListData
                        {
                            News =
                            [
                                new NewsTypeItem
                                {
                                    TypeLabel = "News",
                                    Rows =
                                    [
                                        new NewsRowItem
                                        {
                                            Title = "News item",
                                            Link = "https://news.example.invalid/1",
                                            PublishTime = 1_700_000_000_000
                                        }
                                    ]
                                },
                                new NewsTypeItem
                                {
                                    TypeLabel = "Events",
                                    Rows =
                                    [
                                        new NewsRowItem
                                        {
                                            Title = "Event item",
                                            Link = "https://news.example.invalid/2",
                                            PublishTime = 1_700_000_000_001
                                        }
                                    ]
                                }
                            ]
                        }
                    }
                }
            },
            new LauncherSettings { ShowRemoteContentCard = true },
            CancellationToken.None);
        context.Window.Show();
        Dispatcher.UIThread.RunJobs();

        var selectedTab = context.Window.GetVisualDescendants().OfType<TabItem>().Single(tab => tab.IsSelected);
        var header = selectedTab.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == "News");
        var indicator = selectedTab.GetVisualDescendants().OfType<Border>()
            .Single(border => border.Name == "PART_SelectedPipe" && border.IsEffectivelyVisible);
        var headerTop = header.TranslatePoint(default, selectedTab);
        var indicatorTop = indicator.TranslatePoint(default, selectedTab);

        Assert.NotNull(headerTop);
        Assert.NotNull(indicatorTop);
        var gap = indicatorTop.Value.Y - (headerTop.Value.Y + header.Bounds.Height);
        // The measured gap includes the header's own text height, whose metrics vary
        // by ±1px between the real Skia font rasterization (golden era) and the classic
        // headless drawing path. The design intends a visible non-zero gap; 3px is the
        // real-font floor, so assert the widened gap stays >= 3px.
        Assert.True(gap >= 3, $"Expected at least 3 px between tab header and indicator, but measured {gap} px.");
    }
}
