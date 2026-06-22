using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cafe.Launcher.Avalonia.ViewModels;

public partial class RemoteContentViewModel : ViewModelBase, IDisposable
{
    private const int ManualNavResumeDelayMs = 5000;
    private readonly LocalizationService localizer;
    private readonly ImageCacheService imageCacheService;
    private DispatcherTimer? carouselTimer;
    private CancellationTokenSource? carouselDelayCts;
    private string proxyMode = ProxyModes.Direct;
    private bool showRemoteContentCard = true;

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
    private int carouselSelectedIndex;

    [ObservableProperty]
    private bool bannerIsLooping = true;

    [ObservableProperty]
    private bool isCarouselPaused;

    [ObservableProperty]
    private string carouselPauseIcon = "Pause";

    [ObservableProperty]
    private string carouselPauseTooltip = "";

    [ObservableProperty]
    private string carouselPageText = "";

    [ObservableProperty]
    private bool hasMultipleBanners;

    [ObservableProperty]
    private int bannerIntervalMs = 5000;

    [ObservableProperty]
    private NewsCategory? selectedNewsCategory;

    public ObservableCollection<BannerDot> BannerDots { get; } = [];

    public ObservableCollection<RemoteContentItem> BannerItems { get; } = [];

    public ObservableCollection<RemoteContentItem> NewsItems { get; } = [];

    public ObservableCollection<NewsCategory> NewsCategories { get; } = [];

    public ObservableCollection<RemoteContentItem> SocialMediaItems { get; } = [];

    public Action<string?>? OpenExternalUrlRequested { get; set; }

    internal bool IsCarouselTimerRunning => carouselTimer?.IsEnabled == true;

    public RemoteContentViewModel(LocalizationService localizer, ImageCacheService imageCacheService)
    {
        this.localizer = localizer;
        this.imageCacheService = imageCacheService;
    }

    public void ApplyLanguage()
    {
        CarouselPauseTooltip = IsCarouselPaused
            ? localizer.T("resumeCarousel")
            : localizer.T("pauseCarousel");
        UpdateCarouselPageText();
    }

    public void Apply(LauncherRemoteState remote, LauncherSettings settings, CancellationToken cancellationToken)
    {
        proxyMode = settings.ProxyMode;
        StopCarouselTimer();
        carouselDelayCts?.Cancel();
        DisposeBannerBitmaps();
        BannerItems.Clear();
        NewsItems.Clear();
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
                    Title = localizer.T("banner"),
                    Subtitle = item.BannerImg ?? "",
                    Url = item.JumpUrl ?? "",
                    ImageUrl = item.BannerImg ?? ""
                });
            }

            BannerDots.Clear();
            for (var i = 0; i < BannerItems.Count; i++)
            {
                BannerDots.Add(new BannerDot { Index = i, IsActive = i == 0 });
            }

            CarouselSelectedIndex = 0;
            HasMultipleBanners = BannerItems.Count > 1;
            UpdateCarouselPageText();
            IsCarouselPaused = false;
            CarouselPauseIcon = "Pause";
            CarouselPauseTooltip = localizer.T("pauseCarousel");
            _ = PreloadBannerImagesAsync(cancellationToken);
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
        if (SelectedNewsCategory is not null)
        {
            SelectedNewsCategory.IsActive = true;
        }

        foreach (var cat in NewsCategories)
        {
            foreach (var item in cat.Items)
            {
                NewsItems.Add(item);
            }
        }

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
                Title = localizer.T("contactCustomerSupport"),
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

        if (HasBannerItems)
        {
            StartCarouselTimer();
        }
    }

    public void UpdateRemoteContentVisibility(bool showRemoteContentCard)
    {
        this.showRemoteContentCard = showRemoteContentCard;
        var hasContent = HasNotice || HasBannerItems || HasNewsItems || HasSocialMediaItems;
        HasRemoteContent = showRemoteContentCard && hasContent;
        IsPanelVisible = showRemoteContentCard && (IsLoading || hasContent);
    }

    public void BeginLoading(bool showRemoteContentCard)
    {
        IsLoading = showRemoteContentCard;
        UpdateRemoteContentVisibility(showRemoteContentCard);
    }

    public void EndLoading()
    {
        IsLoading = false;
        UpdateRemoteContentVisibility(showRemoteContentCard);
    }

    public void StopCarouselTimer()
    {
        carouselTimer?.Stop();
        carouselTimer = null;
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
        carouselTimer.Tick += (_, _) =>
        {
            if (BannerItems.Count == 0)
            {
                return;
            }

            var next = CarouselSelectedIndex + 1;
            CarouselSelectedIndex = next % BannerItems.Count;
        };
        carouselTimer.Start();
    }

    [RelayCommand]
    private void ToggleCarouselLoop()
    {
        IsCarouselPaused = !IsCarouselPaused;
        if (IsCarouselPaused)
        {
            StopCarouselTimer();
            CarouselPauseIcon = "Play";
            CarouselPauseTooltip = localizer.T("resumeCarousel");
        }
        else
        {
            StartCarouselTimer();
            CarouselPauseIcon = "Pause";
            CarouselPauseTooltip = localizer.T("pauseCarousel");
        }
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

        SelectBanner(prev);
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

        SelectBanner(next);
    }

    [RelayCommand]
    private void SelectNewsCategory(NewsCategory? category)
    {
        if (category is null)
        {
            return;
        }

        foreach (var c in NewsCategories)
        {
            c.IsActive = c == category;
        }

        SelectedNewsCategory = category;
    }

    [RelayCommand]
    private void SelectBanner(int index)
    {
        if (index >= 0 && index < BannerItems.Count)
        {
            CarouselSelectedIndex = index;
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
            ? localizer.F("carouselPage", CarouselSelectedIndex + 1, BannerItems.Count)
            : "";
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
            try
            {
                var bytes = await imageCacheService.GetImageBytesAsync(item.ImageUrl, proxyMode, cancellationToken);
                if (bytes is null)
                {
                    await Dispatcher.UIThread.InvokeAsync(item.MarkImageLoadFailed);
                    continue;
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!BannerItems.Contains(item))
                    {
                        return;
                    }

                    item.BannerBitmap?.Dispose();
                    item.BannerBitmap = new Bitmap(new MemoryStream(bytes));
                    item.MarkImageLoaded();
                });
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                await Dispatcher.UIThread.InvokeAsync(item.MarkImageLoadFailed);
            }
        }
    }

    private async Task ScheduleCarouselResumeAfterDelayAsync()
    {
        carouselDelayCts?.Cancel();
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
        StopCarouselTimer();
        carouselDelayCts?.Cancel();
        carouselDelayCts?.Dispose();
        DisposeBannerBitmaps();
    }

    private void DisposeBannerBitmaps()
    {
        foreach (var item in BannerItems)
        {
            item.BannerBitmap?.Dispose();
            item.BannerBitmap = null;
        }
    }
}
