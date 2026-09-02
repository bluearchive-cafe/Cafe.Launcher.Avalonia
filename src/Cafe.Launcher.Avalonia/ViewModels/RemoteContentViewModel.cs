using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Threading;
using Cafe.Launcher.Avalonia.Constants;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cafe.Launcher.Avalonia.ViewModels;

public partial class RemoteContentViewModel : ViewModelBase, IDisposable
{
    private const int ManualNavResumeDelayMs = 5000;
    private const int MaxConcurrentBannerImageLoads = 4;
    private readonly LocalizationService localizer;
    private readonly ImageCacheService imageCacheService;
    private BannerCarouselTransition bannerTransition = new(MotionTokens.NormalDuration);
    private DispatcherTimer? carouselTimer;
    private CancellationTokenSource? carouselDelayCts;
    private CancellationTokenSource? bannerPreloadCts;
    private string proxyMode = ProxyModes.Auto;
    private bool showRemoteContentCard = true;
    private bool isMotionReduced;
    private bool isBannerPointerOver;
    private bool isBannerFocused;
    private bool isBannerControlsSuppressed;
    private bool disposed;

    [ObservableProperty]
    private string noticeText = "";

    [ObservableProperty]
    private bool hasNotice;

    [ObservableProperty]
    private bool hasBannerItems;

    [ObservableProperty]
    private bool hasNewsItems;

    [ObservableProperty]
    private bool hasSocialMediaItems;

    [ObservableProperty]
    private bool hasRemoteContent;

    [ObservableProperty]
    private bool isPanelVisible;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private bool hasLoadError;

    [ObservableProperty]
    private int carouselSelectedIndex;

    [ObservableProperty]
    private bool bannerIsLooping = true;

    [ObservableProperty]
    private bool isCarouselPaused;

    [ObservableProperty]
    private bool isBannerInteractionActive;

    [ObservableProperty]
    private string carouselPageText = "";

    [ObservableProperty]
    private bool hasMultipleBanners;

    [ObservableProperty]
    private int bannerIntervalMs = 5000;

    [ObservableProperty]
    private IPageTransition carouselTransition = null!;

    [ObservableProperty]
    private NewsCategory? selectedNewsCategory;

    public ObservableCollection<BannerDot> BannerDots { get; } = [];

    public ObservableCollection<RemoteContentItem> BannerItems { get; } = [];

    public ObservableCollection<NewsCategory> NewsCategories { get; } = [];

    public ObservableCollection<RemoteContentItem> SocialMediaItems { get; } = [];

    public Action<string?>? OpenExternalUrlRequested { get; set; }

    internal bool IsCarouselTimerRunning => carouselTimer?.IsEnabled == true;

    public RemoteContentViewModel(LocalizationService localizer, ImageCacheService imageCacheService)
    {
        this.localizer = localizer;
        this.imageCacheService = imageCacheService;
        carouselTransition = bannerTransition;
    }

    public void ApplyLanguage()
    {
        UpdateCarouselPageText();
        UpdateBannerDotAccessibleNames();
    }

    public void ApplyMotionPreference(bool reduceMotion)
    {
        if (disposed) return;
        isMotionReduced = reduceMotion;
        bannerTransition = new BannerCarouselTransition(
            reduceMotion ? TimeSpan.Zero : MotionTokens.NormalDuration);
        CarouselTransition = bannerTransition;
        UpdateCarouselPauseState();
    }

    public void Apply(LauncherRemoteState remote, LauncherSettings settings, CancellationToken cancellationToken)
    {
        if (disposed) return;
        cancellationToken.ThrowIfCancellationRequested();
        proxyMode = settings.ProxyMode;
        StopCarouselTimer();
        CancelCarouselDelay();
        var preloadToken = RestartBannerPreloadCancellation().Token;
        DisposeBannerBitmaps();
        BannerItems.Clear();
        SocialMediaItems.Clear();
        NewsCategories.Clear();

        var operations = remote.OperationsResource;
        if (operations?.OperationsResourceOpen == true)
        {
            BannerIsLooping = operations.BannerLoop;
            BannerIntervalMs = operations.TimeInterval > 0 ? operations.TimeInterval * 1000 : 5000;

            foreach (var item in operations.OperationsBannerList)
            {
                BannerItems.Add(new RemoteContentItem
                {
                    Title = localizer.T(LocalizationKeys.Banner),
                    Subtitle = item.BannerImg ?? "",
                    Url = item.JumpUrl ?? "",
                    ImageUrl = item.BannerImg ?? ""
                });
            }

            BannerDots.Clear();
            for (var i = 0; i < BannerItems.Count; i++)
            {
                BannerDots.Add(new BannerDot
                {
                    Index = i,
                    AccessibleName = localizer.F(LocalizationKeys.CarouselPage, i + 1, BannerItems.Count),
                    IsActive = i == 0
                });
            }

            CarouselSelectedIndex = 0;
            HasMultipleBanners = BannerItems.Count > 1;
            UpdateCarouselPageText();
            _ = PreloadBannerImagesAsync(preloadToken);
        }
        else
        {
            BannerDots.Clear();
            CarouselSelectedIndex = 0;
            HasMultipleBanners = false;
            UpdateCarouselPageText();
        }

        if (operations?.NewsList?.Code == 0)
        {
            foreach (var item in operations.NewsList.Data?.News ?? [])
            {
                var category = new NewsCategory { Label = item.TypeLabel ?? "" };
                const int maxItemsPerCategory = 50;
                foreach (var row in item.Rows.Take(maxItemsPerCategory))
                {
                    category.Items.Add(new RemoteContentItem
                    {
                        Title = row.Title ?? "",
                        Subtitle = FormatUnixMilliseconds(row.PublishTime, null),
                        Url = row.Link ?? ""
                    });
                }

                if (category.Items.Count > 0)
                {
                    NewsCategories.Add(category);
                }
            }
        }

        foreach (var noticeType in operations?.NoticeList ?? [])
        {
            var category = new NewsCategory { Label = noticeType.NoticeType ?? "" };
            const int maxNoticeItemsPerCategory = 50;
            foreach (var notice in noticeType.NoticeDetailList.Take(maxNoticeItemsPerCategory))
            {
                category.Items.Add(new RemoteContentItem
                {
                    Title = notice.NoticeTitle ?? "",
                    Subtitle = notice.NoticeTime ?? "",
                    Url = notice.JumpUrl ?? ""
                });
            }

            if (category.Items.Count > 0)
            {
                NewsCategories.Add(category);
            }
        }

        SelectedNewsCategory = NewsCategories.FirstOrDefault();

        var social = remote.SocialMediaResource;
        if (social?.SocialMediaResourceOpen == true)
        {
            foreach (var item in social.SocialMediaResourceList)
            {
                SocialMediaItems.Add(new RemoteContentItem
                {
                    Title = item.SocialMediaChannel ?? "",
                    Subtitle = string.IsNullOrWhiteSpace(item.QrImg) ? item.JumpUrl ?? "" : item.QrImg,
                    Url = item.JumpUrl ?? "",
                    ImageUrl = item.QrImg ?? "",
                    SocialIconKind = ResolveSocialIconKind(item.SocialMediaChannel)
                });
            }
        }

        if (social?.ContactCustomerComplaint == true)
        {
            SocialMediaItems.Add(new RemoteContentItem
            {
                Title = localizer.T(LocalizationKeys.ContactCustomerSupport),
                Subtitle = ResolveContactSubtitle(social),
                Url = ResolveContactUrl(social),
                SocialIconKind = "Headset"
            });
        }

        NoticeText = remote.BaseConfig?.NoticePopOpen == true ? remote.BaseConfig.NoticeContent ?? "" : "";
        HasNotice = !string.IsNullOrWhiteSpace(NoticeText);
        HasBannerItems = BannerItems.Count > 0;
        HasNewsItems = NewsCategories.Count > 0;
        HasSocialMediaItems = SocialMediaItems.Count > 0;
        IsLoading = false;
        UpdateRemoteContentVisibility(settings.ShowRemoteContentCard);
        UpdateCarouselPauseState();
    }

    public void UpdateRemoteContentVisibility(bool showRemoteContentCard)
    {
        this.showRemoteContentCard = showRemoteContentCard;
        var hasContent = HasNotice || HasBannerItems || HasNewsItems || HasSocialMediaItems;
        HasRemoteContent = showRemoteContentCard && hasContent;
        IsPanelVisible = showRemoteContentCard && (IsLoading || hasContent || HasLoadError);
    }

    public void BeginLoading(bool showRemoteContentCard)
    {
        HasLoadError = false;
        IsLoading = showRemoteContentCard;
        UpdateRemoteContentVisibility(showRemoteContentCard);
    }

    public void SetLoadError(bool hasLoadError)
    {
        HasLoadError = hasLoadError;
        UpdateRemoteContentVisibility(showRemoteContentCard);
    }

    public void EndLoading()
    {
        IsLoading = false;
        UpdateRemoteContentVisibility(showRemoteContentCard);
    }

    public void StopCarouselTimer()
    {
        if (carouselTimer is null)
        {
            return;
        }

        carouselTimer.Stop();
        carouselTimer = null;
    }

    private void CancelCarouselDelay()
    {
        carouselDelayCts?.Cancel();
        carouselDelayCts?.Dispose();
        carouselDelayCts = null;
    }

    private CancellationTokenSource RestartBannerPreloadCancellation()
    {
        bannerPreloadCts?.Cancel();
        bannerPreloadCts?.Dispose();
        bannerPreloadCts = new CancellationTokenSource();
        return bannerPreloadCts;
    }

    public void StartCarouselTimer()
    {
        StopCarouselTimer();
        if (!BannerIsLooping || BannerItems.Count <= 1 || IsCarouselPaused)
        {
            return;
        }

        carouselTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(BannerIntervalMs)
        };
        carouselTimer.Tick += (_, _) => TryAdvanceCarousel();
        carouselTimer.Start();
    }

    internal bool TryAdvanceCarousel()
    {
        if (BannerItems.Count == 0)
        {
            return false;
        }

        var next = (CarouselSelectedIndex + 1) % BannerItems.Count;
        if (BannerItems[next].IsImageLoading)
        {
            return false;
        }

        NavigateToBanner(next, BannerCarouselTransition.CarouselSlideMode.Fade);
        return true;
    }

    private void NavigateToBanner(int index, BannerCarouselTransition.CarouselSlideMode slideMode)
    {
        bannerTransition.PendingSlide = slideMode;
        CarouselSelectedIndex = index;
    }

    internal void SetBannerPointerOver(bool isPointerOver, bool hideControls = false)
    {
        if (disposed)
        {
            return;
        }

        isBannerPointerOver = isPointerOver;
        isBannerControlsSuppressed = hideControls;
        UpdateCarouselPauseState();
    }

    internal void SetBannerFocusWithin(bool isFocused)
    {
        if (disposed)
        {
            return;
        }

        isBannerFocused = isFocused;
        isBannerControlsSuppressed = false;
        UpdateCarouselPauseState();
    }

    [RelayCommand]
    private void SelectPreviousBanner()
    {
        if (BannerItems.Count == 0)
        {
            return;
        }

        var prev = CarouselSelectedIndex - 1;
        if (prev < 0)
        {
            prev = BannerItems.Count - 1;
        }

        NavigateManuallyTo(prev, backward: true);
    }

    [RelayCommand]
    private void SelectNextBanner()
    {
        if (BannerItems.Count == 0)
        {
            return;
        }

        var next = CarouselSelectedIndex + 1;
        if (next >= BannerItems.Count)
        {
            next = 0;
        }

        NavigateManuallyTo(next, backward: false);
    }

    private void NavigateManuallyTo(int index, bool backward)
    {
        if (disposed
            || index < 0
            || index >= BannerItems.Count)
        {
            return;
        }

        NavigateToBanner(
            index,
            backward
                ? BannerCarouselTransition.CarouselSlideMode.Backward
                : BannerCarouselTransition.CarouselSlideMode.Forward);
        StopCarouselTimer();
        _ = ScheduleCarouselResumeAfterDelayAsync();
    }

    [RelayCommand]
    private void SelectBanner(int index)
    {
        if (index >= 0 && index < BannerItems.Count)
        {
            // Dot navigation has no spatial neighbour relation; it always cross-fades.
            NavigateToBanner(index, BannerCarouselTransition.CarouselSlideMode.Fade);
            StopCarouselTimer();
            _ = ScheduleCarouselResumeAfterDelayAsync();
        }
    }

    [RelayCommand]
    private void OpenExternalUrl(string? url)
    {
        OpenExternalUrlRequested?.Invoke(url);
    }

    partial void OnCarouselSelectedIndexChanged(int value)
    {
        for (var i = 0; i < BannerDots.Count; i++)
        {
            BannerDots[i].IsActive = i == value;
        }

        UpdateCarouselPageText();
    }

    private void UpdateCarouselPageText()
    {
        CarouselPageText = BannerItems.Count > 1
            ? localizer.F(LocalizationKeys.CarouselPage, CarouselSelectedIndex + 1, BannerItems.Count)
            : "";
    }

    private void UpdateBannerDotAccessibleNames()
    {
        for (var i = 0; i < BannerDots.Count; i++)
        {
            BannerDots[i].AccessibleName = localizer.F(LocalizationKeys.CarouselPage, i + 1, BannerItems.Count);
        }
    }

    private async Task PreloadBannerImagesAsync(CancellationToken cancellationToken)
    {
        var snapshot = BannerItems.ToArray();
        foreach (var item in snapshot)
        {
            if (string.IsNullOrWhiteSpace(item.ImageUrl))
            {
                item.MarkImageLoadFailed();
                continue;
            }

            item.MarkImageLoading();
        }

        using var concurrencyGate = new SemaphoreSlim(MaxConcurrentBannerImageLoads);
        var imageLoads = snapshot
            .Where(item => !string.IsNullOrWhiteSpace(item.ImageUrl))
            .Select(item => PreloadBannerImageAsync(item, concurrencyGate, cancellationToken));
        await Task.WhenAll(imageLoads);
    }

    private async Task PreloadBannerImageAsync(
        RemoteContentItem item,
        SemaphoreSlim concurrencyGate,
        CancellationToken cancellationToken)
    {
        try
        {
            await concurrencyGate.WaitAsync(cancellationToken);
            try
            {
                var bytes = await imageCacheService.GetCachedOrDownloadImageBytesAsync(
                    item.ImageUrl,
                    proxyMode,
                    cancellationToken);
                if (bytes is null)
                {
                    await Dispatcher.UIThread.InvokeAsync(item.MarkImageLoadFailed);
                    return;
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (disposed || !BannerItems.Contains(item))
                    {
                        return;
                    }

                    // 先替换绑定引用再释放旧位图：绑定同步清空 Image.Source，保证任何
                    // 渲染帧都不会读到已释放的位图实现（ObjectDisposedException 崩溃面）。
                    var previous = item.BannerBitmap;
                    item.BannerBitmap = new Bitmap(new MemoryStream(bytes));
                    previous?.Dispose();
                    item.MarkImageLoaded();
                });
            }
            finally
            {
                concurrencyGate.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"RemoteContent: banner image load failed for '{item.ImageUrl}': {ex.Message}");
            await Dispatcher.UIThread.InvokeAsync(item.MarkImageLoadFailed);
        }
    }

    private async Task ScheduleCarouselResumeAfterDelayAsync()
    {
        if (disposed) return;
        CancelCarouselDelay();
        carouselDelayCts = new CancellationTokenSource();
        var token = carouselDelayCts.Token;
        try
        {
            await Task.Delay(ManualNavResumeDelayMs, token);
            if (!IsCarouselPaused && BannerIsLooping && BannerItems.Count > 1)
            {
                StartCarouselTimer();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public static string FormatUnixMilliseconds(long value, string? typeLabel)
    {
        var prefix = string.IsNullOrWhiteSpace(typeLabel) ? "" : $"{typeLabel} | ";
        if (value <= 0)
        {
            return prefix.TrimEnd(' ', '|');
        }

        try
        {
            var date = DateTimeOffset.FromUnixTimeMilliseconds(value).LocalDateTime;
            return $"{prefix}{date:yyyy/MM/dd}";
        }
        catch (ArgumentOutOfRangeException)
        {
            return prefix.TrimEnd(' ', '|');
        }
    }

    public static string ResolveSocialIconKind(string? channelName)
    {
        if (string.IsNullOrWhiteSpace(channelName))
        {
            return "Link";
        }

        var name = channelName.ToLowerInvariant();
        if (name.Contains("twitter") || string.Equals(name, "x", StringComparison.Ordinal)) return "Twitter";
        if (name.Contains("youtube")) return "Youtube";
        if (name.Contains("discord")) return "Discord";
        if (name.Contains("line")) return "Chat";
        if (name.Contains("公式") || name.Contains("official") || name.Contains("website")) return "Web";
        if (name.Contains("niconico") || name.Contains("ニコ")) return "Television";
        if (name.Contains("pixiv")) return "Palette";
        if (name.Contains("forum") || name.Contains("コミュ")) return "Forum";
        if (name.Contains("mail") || name.Contains("メール")) return "Email";
        if (name.Contains("instagram")) return "Instagram";
        if (name.Contains("facebook")) return "Facebook";
        if (name.Contains("tiktok")) return "MusicNote";
        return "Link";
    }

    public static string ResolveContactSubtitle(SocialMediaResourceResponse social)
    {
        return social.ContactCustomerComplaintType switch
        {
            0 => social.WebCustomerComplaintUrl ?? "",
            1 => social.AiHelpCustomerComplaint?.AihelpDomain ?? "",
            _ => social.MailCustomerComplaintUrl ?? ""
        };
    }

    public static string ResolveContactUrl(SocialMediaResourceResponse social)
    {
        return social.ContactCustomerComplaintType switch
        {
            0 => social.WebCustomerComplaintUrl ?? "",
            1 => "",
            _ => string.IsNullOrWhiteSpace(social.MailCustomerComplaintUrl)
                ? ""
                : $"mailto:{social.MailCustomerComplaintUrl}"
        };
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        StopCarouselTimer();
        CancelCarouselDelay();
        bannerPreloadCts?.Cancel();
        bannerPreloadCts?.Dispose();
        bannerPreloadCts = null;
        DisposeBannerBitmaps();
    }

    private void DisposeBannerBitmaps()
    {
        foreach (var item in BannerItems)
        {
            var previous = item.BannerBitmap;
            item.BannerBitmap = null;
            previous?.Dispose();
        }
    }

    private void UpdateCarouselPauseState()
    {
        IsBannerInteractionActive = !isBannerControlsSuppressed
            && (isBannerPointerOver || isBannerFocused);
        var paused = isMotionReduced || isBannerPointerOver || isBannerFocused;
        IsCarouselPaused = paused;
        if (paused)
        {
            CancelCarouselDelay();
            StopCarouselTimer();
        }
        else
        {
            StartCarouselTimer();
        }
    }
}
