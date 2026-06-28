using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.ViewModels;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class RemoteContentViewModelTests
{
    static RemoteContentViewModelTests()
    {
        TestLocalizationHelper.Initialize();
    }

    [Fact]
    public void Apply_WhenBannersAreRemoved_StopsExistingCarouselTimer()
    {
        using var httpClientFactory = new HttpClientFactory(new ProxySettingsService());
        using var imageCacheService = new ImageCacheService(
            httpClientFactory,
            new Crc64Service(),
            RemoteHttpUrlValidator.CreateForTesting());
        using var viewModel = new RemoteContentViewModel(
            new LocalizationService(),
            imageCacheService);
        var settings = new LauncherSettings();

        viewModel.Apply(
            new LauncherRemoteState
            {
                OperationsResource = new OperationsResourceResponse
                {
                    OperationsResourceOpen = true,
                    BannerLoop = true,
                    OperationsBannerList =
                    [
                        new OperationsBannerItem(),
                        new OperationsBannerItem()
                    ]
                }
            },
            settings,
            CancellationToken.None);
        Assert.True(viewModel.IsCarouselTimerRunning);

        viewModel.Apply(new LauncherRemoteState(), settings, CancellationToken.None);

        Assert.False(viewModel.IsCarouselTimerRunning);
    }

    [Fact]
    public void Apply_MapsBannerNewsNoticeAndSocialContent()
    {
        using var context = CreateContext();
        var remote = new LauncherRemoteState
        {
            BaseConfig = new BaseConfigResponse
            {
                NoticePopOpen = true,
                NoticeContent = "launcher notice"
            },
            OperationsResource = new OperationsResourceResponse
            {
                OperationsResourceOpen = true,
                BannerLoop = false,
                OperationsBannerList =
                [
                    new OperationsBannerItem
                    {
                        BannerImg = "",
                        JumpUrl = "https://banner.example.invalid"
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
                                Rows =
                                [
                                    new NewsRowItem
                                    {
                                        Title = "News title",
                                        Link = "https://news.example.invalid",
                                        PublishTime = 1_700_000_000_000
                                    }
                                ]
                            }
                        ]
                    }
                },
                NoticeList =
                [
                    new NoticeTypeItem
                    {
                        NoticeType = "Notice",
                        NoticeDetailList =
                        [
                            new NoticeDetailItem
                            {
                                NoticeTitle = "Notice title",
                                NoticeTime = "2026/06/22",
                                JumpUrl = "https://notice.example.invalid"
                            }
                        ]
                    }
                ]
            },
            SocialMediaResource = new SocialMediaResourceResponse
            {
                SocialMediaResourceOpen = true,
                SocialMediaResourceList =
                [
                    new SocialMediaResourceItem
                    {
                        SocialMediaChannel = "YouTube",
                        JumpUrl = "https://youtube.example.invalid"
                    }
                ],
                ContactCustomerComplaint = true,
                ContactCustomerComplaintType = 2,
                MailCustomerComplaintUrl = "support@example.invalid"
            }
        };

        context.ViewModel.Apply(remote, new LauncherSettings(), CancellationToken.None);

        Assert.True(context.ViewModel.HasNotice);
        Assert.Single(context.ViewModel.BannerItems);
        Assert.Equal(2, context.ViewModel.NewsCategories.Count);
        Assert.Equal(2, context.ViewModel.NewsItems.Count);
        Assert.Equal(2, context.ViewModel.SocialMediaItems.Count);
        Assert.Equal("Youtube", context.ViewModel.SocialMediaItems[0].SocialIconKind);
        Assert.Equal("mailto:support@example.invalid", context.ViewModel.SocialMediaItems[1].Url);
        Assert.True(context.ViewModel.HasRemoteContent);
        Assert.True(context.ViewModel.IsPanelVisible);
    }

    [Fact]
    public void Apply_LimitsEachNewsCategoryToFiftyItems()
    {
        using var context = CreateContext();
        var rows = Enumerable.Range(0, 60)
            .Select(index => new NewsRowItem { Title = $"Item {index}" })
            .ToList();

        context.ViewModel.Apply(
            new LauncherRemoteState
            {
                OperationsResource = new OperationsResourceResponse
                {
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
            new LauncherSettings(),
            CancellationToken.None);

        Assert.Equal(50, context.ViewModel.NewsCategories[0].Items.Count);
        Assert.Equal(50, context.ViewModel.NewsItems.Count);
    }

    [Fact]
    public void BannerCommands_WrapSelectionAndUpdateDots()
    {
        using var context = CreateContext();
        context.ViewModel.Apply(
            CreateBannerState(3, loop: false),
            new LauncherSettings(),
            CancellationToken.None);

        context.ViewModel.SelectPreviousBannerCommand.Execute(null);

        Assert.Equal(2, context.ViewModel.CarouselSelectedIndex);
        Assert.True(context.ViewModel.BannerDots[2].IsActive);

        context.ViewModel.SelectNextBannerCommand.Execute(null);

        Assert.Equal(0, context.ViewModel.CarouselSelectedIndex);
        Assert.True(context.ViewModel.BannerDots[0].IsActive);
    }

    [Fact]
    public void ToggleCarouselLoopCommand_PausesAndResumesTimer()
    {
        using var context = CreateContext();
        context.ViewModel.Apply(
            CreateBannerState(2, loop: true),
            new LauncherSettings(),
            CancellationToken.None);

        context.ViewModel.ToggleCarouselLoopCommand.Execute(null);

        Assert.True(context.ViewModel.IsCarouselPaused);
        Assert.False(context.ViewModel.IsCarouselTimerRunning);
        Assert.Equal("Play", context.ViewModel.CarouselPauseIcon);

        context.ViewModel.ToggleCarouselLoopCommand.Execute(null);

        Assert.False(context.ViewModel.IsCarouselPaused);
        Assert.True(context.ViewModel.IsCarouselTimerRunning);
        Assert.Equal("Pause", context.ViewModel.CarouselPauseIcon);
    }

    [Fact]
    public void ApplyMotionPreference_ReducedStopsAutomaticCarouselButKeepsManualNavigation()
    {
        using var context = CreateContext();
        context.ViewModel.Apply(
            CreateBannerState(2, loop: true),
            new LauncherSettings(),
            CancellationToken.None);

        context.ViewModel.ApplyMotionPreference(true);
        Assert.False(context.ViewModel.IsCarouselTimerRunning);
        Assert.Equal(TimeSpan.Zero, Assert.IsType<global::Avalonia.Animation.CrossFade>(
            context.ViewModel.CarouselTransition).Duration);

        context.ViewModel.SelectNextBannerCommand.Execute(null);
        Assert.Equal(1, context.ViewModel.CarouselSelectedIndex);

        context.ViewModel.StartCarouselTimer();
        Assert.False(context.ViewModel.IsCarouselTimerRunning);
    }

    [Theory]
    [InlineData(MotionModes.Full, false)]
    [InlineData(MotionModes.System, true)]
    public void ApplyMotionPreference_FullOrSystemEffectiveStateRestoresTransitionAndAutomaticCarousel(
        string motionMode,
        bool windowsAnimationsEnabled)
    {
        using var context = CreateContext();
        context.ViewModel.Apply(
            CreateBannerState(2, loop: true),
            new LauncherSettings(),
            CancellationToken.None);
        context.ViewModel.ApplyMotionPreference(true);

        context.ViewModel.ApplyMotionPreference(
            MotionSettingsResolver.ShouldReduceMotion(motionMode, windowsAnimationsEnabled));

        Assert.True(context.ViewModel.IsCarouselTimerRunning);
        Assert.Equal(
            TimeSpan.FromMilliseconds(350),
            Assert.IsType<global::Avalonia.Animation.CrossFade>(
                context.ViewModel.CarouselTransition).Duration);
    }

    [Fact]
    public void SelectNewsCategoryCommand_UpdatesActiveCategory()
    {
        using var context = CreateContext();
        context.ViewModel.Apply(
            new LauncherRemoteState
            {
                OperationsResource = new OperationsResourceResponse
                {
                    NoticeList =
                    [
                        new NoticeTypeItem
                        {
                            NoticeType = "A",
                            NoticeDetailList = [new NoticeDetailItem { NoticeTitle = "A1" }]
                        },
                        new NoticeTypeItem
                        {
                            NoticeType = "B",
                            NoticeDetailList = [new NoticeDetailItem { NoticeTitle = "B1" }]
                        }
                    ]
                }
            },
            new LauncherSettings(),
            CancellationToken.None);
        var second = context.ViewModel.NewsCategories[1];

        context.ViewModel.SelectNewsCategoryCommand.Execute(second);

        Assert.Same(second, context.ViewModel.SelectedNewsCategory);
        Assert.False(context.ViewModel.NewsCategories[0].IsActive);
        Assert.True(second.IsActive);
    }

    [Fact]
    public void LoadingAndVisibility_RespectUserSetting()
    {
        using var context = CreateContext();

        context.ViewModel.BeginLoading(showRemoteContentCard: true);
        Assert.True(context.ViewModel.IsPanelVisible);
        Assert.True(context.ViewModel.IsLoading);

        context.ViewModel.EndLoading();
        Assert.False(context.ViewModel.IsPanelVisible);

        context.ViewModel.Apply(
            new LauncherRemoteState
            {
                BaseConfig = new BaseConfigResponse
                {
                    NoticePopOpen = true,
                    NoticeContent = "notice"
                }
            },
            new LauncherSettings { ShowRemoteContentCard = false },
            CancellationToken.None);

        Assert.False(context.ViewModel.HasRemoteContent);
        Assert.False(context.ViewModel.IsPanelVisible);
    }

    [Fact]
    public void OpenExternalUrlCommand_ForwardsExactUrl()
    {
        using var context = CreateContext();
        string? opened = null;
        context.ViewModel.OpenExternalUrlRequested = value => opened = value;

        context.ViewModel.OpenExternalUrlCommand.Execute("https://example.invalid");

        Assert.Equal("https://example.invalid", opened);
    }

    [Theory]
    [InlineData("Twitter", "Twitter")]
    [InlineData("x", "Twitter")]
    [InlineData("pixiv", "Palette")]
    [InlineData("Discord", "Discord")]
    [InlineData("unknown", "Link")]
    [InlineData(null, "Link")]
    public void ResolveSocialIconKind_MapsKnownChannels(string? channel, string expected)
    {
        Assert.Equal(expected, RemoteContentViewModel.ResolveSocialIconKind(channel));
    }

    [Theory]
    [InlineData(0, "https://support.example.invalid")]
    [InlineData(1, "")]
    [InlineData(2, "mailto:support@example.invalid")]
    public void ResolveContactUrl_MapsConfiguredContactType(int type, string expected)
    {
        var social = new SocialMediaResourceResponse
        {
            ContactCustomerComplaintType = type,
            WebCustomerComplaintUrl = "https://support.example.invalid",
            MailCustomerComplaintUrl = "support@example.invalid"
        };

        Assert.Equal(expected, RemoteContentViewModel.ResolveContactUrl(social));
    }

    [Fact]
    public void FormatUnixMilliseconds_WhenValueIsInvalid_ReturnsTypeLabel()
    {
        Assert.Equal("News", RemoteContentViewModel.FormatUnixMilliseconds(0, "News"));
        Assert.Equal(
            "News",
            RemoteContentViewModel.FormatUnixMilliseconds(long.MaxValue, "News"));
    }

    private static LauncherRemoteState CreateBannerState(int count, bool loop) =>
        new()
        {
            OperationsResource = new OperationsResourceResponse
            {
                OperationsResourceOpen = true,
                BannerLoop = loop,
                OperationsBannerList = Enumerable.Range(0, count)
                    .Select(_ => new OperationsBannerItem())
                    .ToList()
            }
        };

    private static TestContext CreateContext()
    {
        var factory = new HttpClientFactory(new ProxySettingsService());
        var cache = new ImageCacheService(
            factory,
            new Crc64Service(),
            RemoteHttpUrlValidator.CreateForTesting());
        return new TestContext(
            new RemoteContentViewModel(new LocalizationService(), cache),
            cache,
            factory);
    }

    private sealed record TestContext(
        RemoteContentViewModel ViewModel,
        ImageCacheService Cache,
        HttpClientFactory Factory) : IDisposable
    {
        public void Dispose()
        {
            ViewModel.Dispose();
            Cache.Dispose();
            Factory.Dispose();
        }
    }
}
