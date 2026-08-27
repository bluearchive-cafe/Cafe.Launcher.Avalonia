using Cafe.Launcher.Avalonia.Helpers;
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
        Assert.Equal(2, context.ViewModel.NewsCategories.Sum(category => category.Items.Count));
        Assert.Same(context.ViewModel.NewsCategories[0], context.ViewModel.SelectedNewsCategory);
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
    public void TryAdvanceCarousel_WithoutBanners_KeepsCurrentIndex()
    {
        using var context = CreateContext();

        var advanced = context.ViewModel.TryAdvanceCarousel();

        Assert.False(advanced);
        Assert.Equal(0, context.ViewModel.CarouselSelectedIndex);
    }

    [Fact]
    public void TryAdvanceCarousel_NextImageLoading_KeepsCurrentBanner()
    {
        using var context = CreateContext();
        context.ViewModel.Apply(
            CreateBannerState(2, loop: false),
            new LauncherSettings(),
            CancellationToken.None);
        context.ViewModel.BannerItems[1].MarkImageLoading();

        var advanced = context.ViewModel.TryAdvanceCarousel();

        Assert.False(advanced);
        Assert.Equal(0, context.ViewModel.CarouselSelectedIndex);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TryAdvanceCarousel_NextImageTerminal_Advances(bool failed)
    {
        using var context = CreateContext();
        context.ViewModel.Apply(
            CreateBannerState(2, loop: false),
            new LauncherSettings(),
            CancellationToken.None);

        if (failed)
        {
            context.ViewModel.BannerItems[1].MarkImageLoadFailed();
        }
        else
        {
            context.ViewModel.BannerItems[1].MarkImageLoaded();
        }

        var advanced = context.ViewModel.TryAdvanceCarousel();

        Assert.True(advanced);
        Assert.Equal(1, context.ViewModel.CarouselSelectedIndex);
    }

    [Fact]
    public void TryAdvanceCarousel_WrapsToLoadingFirstImage_KeepsCurrentBanner()
    {
        using var context = CreateContext();
        context.ViewModel.Apply(
            CreateBannerState(2, loop: false),
            new LauncherSettings(),
            CancellationToken.None);
        context.ViewModel.BannerItems[1].MarkImageLoaded();
        context.ViewModel.BannerItems[0].MarkImageLoading();
        context.ViewModel.CarouselSelectedIndex = 1;

        var advanced = context.ViewModel.TryAdvanceCarousel();

        Assert.False(advanced);
        Assert.Equal(1, context.ViewModel.CarouselSelectedIndex);
    }

    [Fact]
    public void CarouselPageText_WithMultipleBanners_UsesCompactLocalizedFormat()
    {
        using var context = CreateContext(LauncherLanguages.English);
        context.ViewModel.Apply(
            CreateBannerState(2, loop: false),
            new LauncherSettings(),
            CancellationToken.None);

        Assert.Equal("1 / 2", context.ViewModel.CarouselPageText);

        context.ViewModel.SelectNextBannerCommand.Execute(null);

        Assert.Equal("2 / 2", context.ViewModel.CarouselPageText);
    }

    [Fact]
    public void BannerPointerHover_PausesAndResumesTimerImmediately()
    {
        using var context = CreateContext();
        context.ViewModel.Apply(
            CreateBannerState(2, loop: true),
            new LauncherSettings(),
            CancellationToken.None);

        context.ViewModel.SetBannerPointerOver(true);

        Assert.True(context.ViewModel.IsCarouselPaused);
        Assert.False(context.ViewModel.IsCarouselTimerRunning);

        context.ViewModel.SetBannerPointerOver(false);

        Assert.False(context.ViewModel.IsCarouselPaused);
        Assert.True(context.ViewModel.IsCarouselTimerRunning);
    }

    [Fact]
    public void BannerFocusAndPointerPauseSourcesRemainActiveUntilBothLeave()
    {
        using var context = CreateContext();
        context.ViewModel.Apply(
            CreateBannerState(2, loop: true),
            new LauncherSettings(),
            CancellationToken.None);

        context.ViewModel.SetBannerPointerOver(true);
        context.ViewModel.SetBannerFocusWithin(true);
        context.ViewModel.SetBannerPointerOver(false);

        Assert.True(context.ViewModel.IsCarouselPaused);
        Assert.False(context.ViewModel.IsCarouselTimerRunning);

        context.ViewModel.SetBannerFocusWithin(false);

        Assert.False(context.ViewModel.IsCarouselPaused);
        Assert.True(context.ViewModel.IsCarouselTimerRunning);
    }

    [Fact]
    public void BannerPointerExited_WithSuppressedControls_HidesVisualsUntilNextFocusOrHover()
    {
        using var context = CreateContext();
        context.ViewModel.Apply(
            CreateBannerState(2, loop: true),
            new LauncherSettings(),
            CancellationToken.None);

        context.ViewModel.SetBannerPointerOver(true);
        context.ViewModel.SetBannerFocusWithin(true);
        context.ViewModel.SetBannerPointerOver(false, hideControls: true);

        Assert.True(context.ViewModel.IsCarouselPaused);
        Assert.False(context.ViewModel.IsBannerInteractionActive);

        context.ViewModel.SetBannerFocusWithin(true);

        Assert.True(context.ViewModel.IsBannerInteractionActive);
        Assert.True(context.ViewModel.IsCarouselPaused);

        context.ViewModel.SetBannerFocusWithin(false);

        Assert.False(context.ViewModel.IsBannerInteractionActive);
        Assert.False(context.ViewModel.IsCarouselPaused);
        Assert.True(context.ViewModel.IsCarouselTimerRunning);
    }

    [Fact]
    public void BannerPointerExited_WhenNotFocused_ResumesCarouselAndHidesControls()
    {
        using var context = CreateContext();
        context.ViewModel.Apply(
            CreateBannerState(2, loop: true),
            new LauncherSettings(),
            CancellationToken.None);

        context.ViewModel.SetBannerPointerOver(true);
        context.ViewModel.SetBannerPointerOver(false, hideControls: true);

        Assert.False(context.ViewModel.IsCarouselPaused);
        Assert.False(context.ViewModel.IsBannerInteractionActive);
        Assert.True(context.ViewModel.IsCarouselTimerRunning);
    }

    [Fact]
    public void ApplyMotionPreference_ReducedPausesAutomaticCarouselAndUsesZeroDurationSlide()
    {
        using var context = CreateContext();
        context.ViewModel.Apply(
            CreateBannerState(2, loop: true),
            new LauncherSettings(),
            CancellationToken.None);

        context.ViewModel.ApplyMotionPreference(true);
        Assert.False(context.ViewModel.IsCarouselTimerRunning);
        Assert.True(context.ViewModel.IsCarouselPaused);
        var transition = Assert.IsType<global::Cafe.Launcher.Avalonia.Helpers.BannerCarouselTransition>(
            context.ViewModel.CarouselTransition);
        Assert.Equal(TimeSpan.Zero, transition.Duration);

        context.ViewModel.SelectNextBannerCommand.Execute(null);
        Assert.Equal(1, context.ViewModel.CarouselSelectedIndex);
    }

    [Fact]
    public void ApplyMotionPreference_WhenReduced_OverridesBannerInteraction()
    {
        using var context = CreateContext();
        context.ViewModel.Apply(
            CreateBannerState(2, loop: true),
            new LauncherSettings(),
            CancellationToken.None);
        context.ViewModel.ApplyMotionPreference(true);

        context.ViewModel.SetBannerPointerOver(true);
        context.ViewModel.SetBannerPointerOver(false);

        Assert.True(context.ViewModel.IsCarouselPaused);
        Assert.False(context.ViewModel.IsCarouselTimerRunning);
        var transition = Assert.IsType<global::Cafe.Launcher.Avalonia.Helpers.BannerCarouselTransition>(
            context.ViewModel.CarouselTransition);
        Assert.Equal(TimeSpan.Zero, transition.Duration);
    }

    [Fact]
    public void ApplyMotionPreference_FullAfterReduced_ResumesCarouselWhenPointerIsOutside()
    {
        using var context = CreateContext();
        context.ViewModel.Apply(
            CreateBannerState(2, loop: true),
            new LauncherSettings(),
            CancellationToken.None);
        context.ViewModel.ApplyMotionPreference(true);

        context.ViewModel.ApplyMotionPreference(false);

        Assert.False(context.ViewModel.IsCarouselPaused);
        Assert.True(context.ViewModel.IsCarouselTimerRunning);
        var transition = Assert.IsType<global::Cafe.Launcher.Avalonia.Helpers.BannerCarouselTransition>(
            context.ViewModel.CarouselTransition);
        Assert.Equal(TimeSpan.FromMilliseconds(250), transition.Duration);
    }

    [Theory]
    [InlineData(MotionModes.Full, false)]
    [InlineData(MotionModes.System, true)]
    public void ApplyMotionPreference_FullOrSystemEffectiveStateRestoresTransitionAndCarousel(
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

        Assert.False(context.ViewModel.IsCarouselPaused);
        Assert.True(context.ViewModel.IsCarouselTimerRunning);
        Assert.Equal(
            TimeSpan.FromMilliseconds(250),
            Assert.IsType<global::Cafe.Launcher.Avalonia.Helpers.BannerCarouselTransition>(
                context.ViewModel.CarouselTransition).Duration);
    }

    [Fact]
    public void SelectNextBanner_MarksNextTransitionAsDirectionalForward()
    {
        using var context = CreateContext();
        context.ViewModel.Apply(
            CreateBannerState(2, loop: true),
            new LauncherSettings(),
            CancellationToken.None);

        context.ViewModel.SelectNextBannerCommand.Execute(null);

        var transition = Assert.IsType<global::Cafe.Launcher.Avalonia.Helpers.BannerCarouselTransition>(
            context.ViewModel.CarouselTransition);
        Assert.True(transition.NextSlideIsDirectional);
        Assert.False(transition.NextSlideIsBackward);
    }

    [Fact]
    public void SelectPreviousBanner_MarksNextTransitionAsDirectionalBackward()
    {
        using var context = CreateContext();
        context.ViewModel.Apply(
            CreateBannerState(2, loop: true),
            new LauncherSettings(),
            CancellationToken.None);

        context.ViewModel.SelectPreviousBannerCommand.Execute(null);

        var transition = Assert.IsType<global::Cafe.Launcher.Avalonia.Helpers.BannerCarouselTransition>(
            context.ViewModel.CarouselTransition);
        Assert.True(transition.NextSlideIsDirectional);
        Assert.True(transition.NextSlideIsBackward);
    }

    [Fact]
    public void TryAdvanceCarousel_AutomaticTick_ClearsDirectionalSlide()
    {
        using var context = CreateContext();
        context.ViewModel.Apply(
            CreateBannerState(2, loop: true),
            new LauncherSettings(),
            CancellationToken.None);
        context.ViewModel.SelectNextBannerCommand.Execute(null);

        Assert.True(context.ViewModel.TryAdvanceCarousel());

        var transition = Assert.IsType<global::Cafe.Launcher.Avalonia.Helpers.BannerCarouselTransition>(
            context.ViewModel.CarouselTransition);
        Assert.False(transition.NextSlideIsDirectional);
        Assert.False(transition.NextSlideIsBackward);
    }

    [Fact]
    public void SelectBanner_DotNavigation_ClearsDirectionalSlide()
    {
        using var context = CreateContext();
        context.ViewModel.Apply(
            CreateBannerState(2, loop: true),
            new LauncherSettings(),
            CancellationToken.None);
        context.ViewModel.SelectNextBannerCommand.Execute(null);

        context.ViewModel.SelectBannerCommand.Execute(0);

        var transition = Assert.IsType<global::Cafe.Launcher.Avalonia.Helpers.BannerCarouselTransition>(
            context.ViewModel.CarouselTransition);
        Assert.False(transition.NextSlideIsDirectional);
        Assert.False(transition.NextSlideIsBackward);
    }

    [Fact]
    public void SelectedNewsCategory_CanBeChangedForTabControlSelection()
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

        context.ViewModel.SelectedNewsCategory = second;

        Assert.Same(second, context.ViewModel.SelectedNewsCategory);
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
    public void SetLoadError_WhenRemoteContentIsEnabled_KeepsPanelVisibleAfterLoading()
    {
        using var context = CreateContext();

        context.ViewModel.BeginLoading(showRemoteContentCard: true);
        context.ViewModel.SetLoadError(true);
        context.ViewModel.EndLoading();

        Assert.True(context.ViewModel.HasLoadError);
        Assert.True(context.ViewModel.IsPanelVisible);
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

    private static TestContext CreateContext(string? language = null)
    {
        var factory = new HttpClientFactory(new ProxySettingsService());
        var cache = new ImageCacheService(
            factory,
            new Crc64Service(),
            RemoteHttpUrlValidator.CreateForTesting());
        var localizer = new LocalizationService();
        if (language is not null)
        {
            localizer.SetLanguage(language);
        }

        return new TestContext(
            new RemoteContentViewModel(localizer, cache),
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
