using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using Cafe.Launcher.Avalonia.Testing;
using Cafe.Launcher.Avalonia.ViewModels;

namespace Cafe.Launcher.Avalonia.Tests;

[Collection(nameof(LocalizationServiceTestIsolation))]
public sealed class RemoteContentViewModelTests
{
    private static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(5);

    static RemoteContentViewModelTests()
    {
        TestLocalizationHelper.Initialize();
    }

    [Fact]
    public void Apply_WhenBannersAreRemoved_StopsExistingCarouselTimer()
    {
        using var imageCacheService = new ImageCacheService(
            new StubRemoteHttpTransport(),
            new Crc64Service(), TestDataRoot.ForCurrentProcess());
        using var viewModel = new RemoteContentViewModel(
            new LocalizationService(),
            imageCacheService,
            new LocalDiagnostics());
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
        Assert.Equal(
            global::Cafe.Launcher.Avalonia.Helpers.BannerCarouselTransition.CarouselSlideMode.Forward,
            transition.PendingSlide);
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
        Assert.Equal(
            global::Cafe.Launcher.Avalonia.Helpers.BannerCarouselTransition.CarouselSlideMode.Backward,
            transition.PendingSlide);
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
        Assert.Equal(
            global::Cafe.Launcher.Avalonia.Helpers.BannerCarouselTransition.CarouselSlideMode.Fade,
            transition.PendingSlide);
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
        Assert.Equal(
            global::Cafe.Launcher.Avalonia.Helpers.BannerCarouselTransition.CarouselSlideMode.Fade,
            transition.PendingSlide);
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

    [Theory]
    [InlineData("https://example.invalid/page", "https://example.invalid/page")]
    [InlineData("http://example.invalid/page", "http://example.invalid/page")]
    [InlineData("mailto:support@example.invalid", "mailto:support@example.invalid")]
    [InlineData("file:///C:/Windows/System32/cmd.exe", "")]
    [InlineData("cmd://calc", "")]
    [InlineData("javascript:alert(1)", "")]
    [InlineData("not a url", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void SanitizeLinkUrl_AllowsOnlyWebAndMailtoSchemes(string? input, string expected)
    {
        Assert.Equal(expected, RemoteContentViewModel.SanitizeLinkUrl(input));
    }

    [Theory]
    [InlineData("https://example.invalid/banner.png", "https://example.invalid/banner.png")]
    [InlineData("mailto:support@example.invalid", "")]
    [InlineData("file:///C:/Windows/System32/cmd.exe", "")]
    [InlineData(null, "")]
    public void SanitizeImageUrl_AllowsOnlyHttpSchemes(string? input, string expected)
    {
        Assert.Equal(expected, RemoteContentViewModel.SanitizeImageUrl(input));
    }

    [Fact]
    public void FormatUnixMilliseconds_WhenValueIsInvalid_ReturnsTypeLabel()
    {
        Assert.Equal("News", RemoteContentViewModel.FormatUnixMilliseconds(0, "News"));
        Assert.Equal(
            "News",
            RemoteContentViewModel.FormatUnixMilliseconds(long.MaxValue, "News"));
    }

    [Fact]
    public void CarouselTimerTick_WhenRunning_AdvancesToNextBanner()
    {
        using var context = CreateSeamedContext();
        context.ViewModel.Apply(CreateBannerState(2, loop: true), new LauncherSettings(), CancellationToken.None);

        Assert.True(context.Timer.IsRunning);
        context.Timer.Fire();

        Assert.Equal(1, context.ViewModel.CarouselSelectedIndex);
    }

    [Fact]
    public void CarouselTimerTick_AfterManualStop_DoesNotAdvance()
    {
        using var context = CreateSeamedContext();
        context.ViewModel.Apply(CreateBannerState(2, loop: true), new LauncherSettings(), CancellationToken.None);
        context.ViewModel.StopCarouselTimer();

        context.Timer.Fire();

        Assert.Equal(0, context.ViewModel.CarouselSelectedIndex);
    }

    [Fact]
    public async Task ManualNavigation_AfterResumeDelay_RestartsCarouselTimer()
    {
        using var context = CreateSeamedContext();
        context.ViewModel.Apply(CreateBannerState(2, loop: true), new LauncherSettings(), CancellationToken.None);

        context.ViewModel.SelectNextBannerCommand.Execute(null);
        Assert.False(context.Timer.IsRunning);

        context.Delay.Gates[0].TrySetResult();
        await WaitUntil(() => context.Timer.IsRunning);

        Assert.Equal(1, context.ViewModel.CarouselSelectedIndex);
    }

    [Fact]
    public async Task ManualNavigation_WhenPausedWithinResumeWindow_CancelsResume()
    {
        using var context = CreateSeamedContext();
        context.ViewModel.Apply(CreateBannerState(2, loop: true), new LauncherSettings(), CancellationToken.None);

        context.ViewModel.SelectNextBannerCommand.Execute(null);
        context.ViewModel.SetBannerPointerOver(true);

        await WaitUntil(() => context.Delay.CancelledCount >= 1);
        context.Delay.Gates[0].TrySetResult();

        Assert.True(context.ViewModel.IsCarouselPaused);
        Assert.False(context.Timer.IsRunning);
    }

    [Fact]
    public async Task ManualNavigation_SecondNavigation_SupersedesFirstResumeDelay()
    {
        using var context = CreateSeamedContext();
        context.ViewModel.Apply(CreateBannerState(2, loop: true), new LauncherSettings(), CancellationToken.None);

        context.ViewModel.SelectNextBannerCommand.Execute(null);
        var firstResumeTask = context.ViewModel.CarouselResumeTask;
        context.ViewModel.SelectPreviousBannerCommand.Execute(null);
        Assert.False(context.Timer.IsRunning);

        // 第一次导航的恢复窗口被第二次导航取消，而非到时恢复。
        await WaitUntil(() => context.Delay.CancelledCount >= 1);
        context.Delay.Gates[0].TrySetResult();
        await firstResumeTask.WaitAsync(GateTimeout);
        Assert.False(context.Timer.IsRunning);

        context.Delay.Gates[1].TrySetResult();
        await WaitUntil(() => context.Timer.IsRunning);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
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
        var cache = new ImageCacheService(new StubRemoteHttpTransport(), new Crc64Service(), TestDataRoot.ForCurrentProcess());
        var localizer = new LocalizationService();
        if (language is not null)
        {
            localizer.SetLanguage(language);
        }

        return new TestContext(
            new RemoteContentViewModel(localizer, cache, new LocalDiagnostics()),
            cache);
    }

    private sealed record TestContext(
        RemoteContentViewModel ViewModel,
        ImageCacheService Cache) : IDisposable
    {
        public void Dispose()
        {
            ViewModel.Dispose();
            Cache.Dispose();
        }
    }

    private sealed record SeamedContext(
        RemoteContentViewModel ViewModel,
        ManualCarouselTimer Timer,
        GateDelay Delay,
        ImageCacheService Cache) : IDisposable
    {
        public void Dispose()
        {
            ViewModel.Dispose();
            Cache.Dispose();
        }
    }

    private static SeamedContext CreateSeamedContext()
    {
        var cache = new ImageCacheService(new StubRemoteHttpTransport(), new Crc64Service(), TestDataRoot.ForCurrentProcess());
        var timer = new ManualCarouselTimer();
        var delay = new GateDelay();
        return new SeamedContext(
            new RemoteContentViewModel(
                new LocalizationService(),
                cache,
                new LocalDiagnostics(),
                delay.Wait,
                timer),
            timer,
            delay,
            cache);
    }

    /// <summary>手动触发 tick 的计时器替身：Stop 之后触发必须无效，与 DispatcherTimer 语义一致。</summary>
    private sealed class ManualCarouselTimer : ICarouselTimer
    {
        private Action? onTick;

        public bool IsRunning { get; private set; }

        public void Start(TimeSpan interval, Action onTick)
        {
            this.onTick = onTick;
            IsRunning = true;
        }

        public void Stop() => IsRunning = false;

        public void Fire()
        {
            if (IsRunning)
            {
                onTick?.Invoke();
            }
        }
    }

    /// <summary>每次调用生成独立闸门的延迟替身：Release 放行，取消令牌使等待以 OCE 结束。</summary>
    private sealed class GateDelay
    {
        private readonly List<TaskCompletionSource> gates = [];

        public IReadOnlyList<TaskCompletionSource> Gates => gates;

        public int CancelledCount { get; private set; }

        public Func<TimeSpan, CancellationToken, Task> Wait => (_, cancellationToken) =>
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            gates.Add(gate);
            if (cancellationToken.CanBeCanceled)
            {
                cancellationToken.Register(() =>
                {
                    CancelledCount++;
                    gate.TrySetCanceled(cancellationToken);
                });
            }

            return gate.Task;
        };
    }
}
